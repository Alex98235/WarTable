<script>
	import {
		ACTOR,
		STEP_STATUSES,
		UPDATE_KINDS,
		EVIDENCE_KINDS,
		actorLabel,
		apiDelete,
		apiPatch,
		apiPost,
		apiPut,
		isConflict,
		formatDate,
		latestUpdate,
		stepRef,
		timeAgo
	} from '../lib/api.js';
	import { addToast, showConfirm } from '../lib/stores.js';
	import Markdown from './Markdown.svelte';
	import MarkdownEditor from './MarkdownEditor.svelte';
	import StatusBadge from './StatusBadge.svelte';

	/**
	 * Full step card: status, dependencies, updates thread, evidence.
	 * Props:
	 *   step, objective, campaignId, allSteps (same objective),
	 *   onchanged (refresh callback), onMoveUp, onMoveDown, canMoveUp, canMoveDown
	 */
	let {
		step,
		objective,
		campaignId = '',
		allSteps = [],
		onchanged = () => {},
		onMoveUp = null,
		onMoveDown = null,
		canMoveUp = false,
		canMoveDown = false
	} = $props();

	let ref = $derived(stepRef(objective?.alias, step.stepNumber));
	let deps = $derived(Array.isArray(step.dependsOn) ? step.dependsOn : []);
	let unmet = $derived(deps.filter((d) => d.status !== 'done'));
	let ready = $derived(
		objective?.planState === 'Active' && step.status === 'open' && unmet.length === 0
	);
	let updates = $derived(
		[...(step.updates || [])].sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt))
	);
	let evidence = $derived(
		[...(step.evidence || [])].sort((a, b) => new Date(a.createdAt) - new Date(b.createdAt))
	);
	let latest = $derived(latestUpdate(step));
	let historyOpen = $state(false);
	let contextOpen = $state(false);

	let editing = $state(false);
	let saving = $state(false);
	let editForm = $state({ title: '', context: '', assignee: '', assigner: '' });
	let editDeps = $state([]);

	let showUpdate = $state(false);
	let updateBody = $state('');
	let updateKind = $state('Note');
	let updateSaving = $state(false);

	let showEvidence = $state(false);
	let evidenceLabel = $state('');
	let evidenceKind = $state('Link');
	let evidenceValue = $state('');
	let evidenceSaving = $state(false);

	let statusSaving = $state(false);

	let dependencyOptions = $derived(
		(allSteps || []).filter((candidate) => candidate.id !== step.id)
	);

	async function applyStatus(status) {
		if (!status || status === step.status) return;
		statusSaving = true;
		try {
			await apiPatch(`/api/steps/${step.id}/status`, {
				status,
				actor: ACTOR,
				rowVersion: step.rowVersion
			});
			addToast(`${ref} marked ${status}.`, 'success');
			onchanged();
		} catch (err) {
			if (isConflict(err)) {
				showConfirm(
					'This step was changed elsewhere while you were editing. Your changes are not saved. Reload to see the current version?',
					() => onchanged()
				);
				return;
			}
			addToast(err.message || 'Could not update step status.', 'error');
		} finally {
			statusSaving = false;
		}
	}

	async function setStatus(event) {
		await applyStatus(event.currentTarget.value);
	}

	async function toggleFlag() {
		await applyStatus(step.status === 'flagged' ? 'open' : 'flagged');
	}

	function startEdit() {
		editForm = {
			title: step.title || '',
			context: step.context || '',
			assignee: step.assignee || '',
			assigner: step.assigner || ''
		};
		editDeps = deps.map((d) => d.stepId);
		editing = true;
		contextOpen = false;
	}

	async function saveEdit(event) {
		event?.preventDefault?.();
		if (!editForm.title.trim()) {
			addToast('A step needs a title.', 'warning');
			return;
		}
		saving = true;
		try {
			await apiPut(`/api/steps/${step.id}`, {
				title: editForm.title.trim(),
				context: editForm.context,
				assignee: editForm.assignee,
				assigner: editForm.assigner,
				dependsOn: editDeps,
				actor: ACTOR,
				rowVersion: step.rowVersion
			});
			addToast(`${ref} updated.`, 'success');
			editing = false;
			onchanged();
		} catch (err) {
			if (isConflict(err)) {
				showConfirm(
					'This step was changed elsewhere while you were editing. Your changes are not saved. Reload to see the current version?',
					() => {
						editing = false;
						onchanged();
					}
				);
				return;
			}
			addToast(err.message || 'Could not save the step.', 'error');
		} finally {
			saving = false;
		}
	}

	async function postUpdate(event) {
		event?.preventDefault?.();
		if (!updateBody.trim()) return;
		updateSaving = true;
		try {
			await apiPost(`/api/steps/${step.id}/updates`, {
				body: updateBody.trim(),
				kind: updateKind,
				actor: ACTOR
			});
			updateBody = '';
			updateKind = 'Note';
			showUpdate = false;
			historyOpen = true;
			addToast('Update added.', 'success');
			onchanged();
		} catch (err) {
			addToast(err.message || 'Could not add the update.', 'error');
		} finally {
			updateSaving = false;
		}
	}

	async function postEvidence(event) {
		event?.preventDefault?.();
		if (!evidenceLabel.trim()) {
			addToast('Evidence needs a label.', 'warning');
			return;
		}
		evidenceSaving = true;
		try {
			await apiPost(`/api/steps/${step.id}/evidence`, {
				label: evidenceLabel.trim(),
				kind: evidenceKind,
				value: evidenceValue.trim(),
				actor: ACTOR
			});
			evidenceLabel = '';
			evidenceValue = '';
			evidenceKind = 'Link';
			showEvidence = false;
			historyOpen = true;
			addToast('Evidence attached.', 'success');
			onchanged();
		} catch (err) {
			addToast(err.message || 'Could not attach the evidence.', 'error');
		} finally {
			evidenceSaving = false;
		}
	}

	function deleteStep() {
		showConfirm(`Delete ${ref} "${step.title}"? This cannot be undone.`, () =>
			removeStep(false)
		);
	}

	async function removeStep(force) {
		try {
			await apiDelete(
				`/api/steps/${step.id}`,
				force
					? { actor: ACTOR, force: true, rowVersion: step.rowVersion }
					: { actor: ACTOR, rowVersion: step.rowVersion }
			);
			addToast('Step deleted.', 'success');
			onchanged();
		} catch (err) {
			const message = err.message || '';
			if (isConflict(err)) {
				showConfirm(
					'This step was changed elsewhere. Reload to see the current state?',
					() => onchanged()
				);
				return;
			}
			if (!force && /depend|force=true|required by/i.test(message)) {
				showConfirm(
					`${ref} is required by other steps. Delete it and remove those dependency edges?`,
					() => removeStep(true)
				);
				return;
			}
			addToast(err.message || 'Could not delete the step.', 'error');
		}
	}

	function looksLikeUrl(value) {
		return /^https?:\/\//i.test(value || '');
	}

	function statusLabel(status) {
		return status === 'in-progress'
			? 'In progress'
			: status.charAt(0).toUpperCase() + status.slice(1);
	}
</script>

<article class="step-card status-{step.status}" class:is-flagged={step.status === 'flagged'}>
	<header class="step-card-head">
		<div class="step-id-group">
			<span class="step-ref">{ref}</span>
			<StatusBadge status={step.status} sm />
			{#if ready}<span class="chip chip-ready">Ready</span>{/if}
			{#if unmet.length > 0}
				<span class="chip chip-blocked">Blocked by {unmet.length}</span>
			{/if}
		</div>

		<div class="step-actions">
			<button
				type="button"
				class="btn btn-ghost btn-icon btn-xs"
				class:flag-on={step.status === 'flagged'}
				onclick={toggleFlag}
				title={step.status === 'flagged' ? 'Clear flag' : 'Flag for human attention'}
				aria-label={step.status === 'flagged' ? 'Clear flag' : 'Flag step'}
				disabled={statusSaving}>!</button
			>
			<button
				type="button"
				class="btn btn-ghost btn-icon btn-xs"
				onclick={startEdit}
				title="Edit step"
				aria-label="Edit step">✎</button
			>
			{#if onMoveUp}
				<button
					type="button"
					class="btn btn-ghost btn-icon btn-xs"
					onclick={onMoveUp}
					disabled={!canMoveUp}
					title="Move up"
					aria-label="Move step up">↑</button
				>
			{/if}
			{#if onMoveDown}
				<button
					type="button"
					class="btn btn-ghost btn-icon btn-xs"
					onclick={onMoveDown}
					disabled={!canMoveDown}
					title="Move down"
					aria-label="Move step down">↓</button
				>
			{/if}
			<button
				type="button"
				class="btn btn-ghost btn-icon btn-xs danger-hover"
				onclick={deleteStep}
				title="Delete step"
				aria-label="Delete step">×</button
			>
		</div>
	</header>

	{#if editing}
		<form class="step-edit-form" onsubmit={saveEdit}>
			<div class="form-group">
				<label class="form-label" for="step-{step.id}-title">Title</label>
				<input
					id="step-{step.id}-title"
					class="form-input"
					type="text"
					bind:value={editForm.title}
				/>
			</div>
			<div class="form-group">
				<label class="form-label" for="step-{step.id}-context">Context</label>
				<MarkdownEditor
					id="step-{step.id}-context"
					bind:value={editForm.context}
					{campaignId}
					rows={4}
				/>
			</div>
			<div class="form-row">
				<div class="form-group">
					<label class="form-label" for="step-{step.id}-assignee">Assignee</label>
					<input
						id="step-{step.id}-assignee"
						class="form-input"
						type="text"
						bind:value={editForm.assignee}
						placeholder="agent:fixer, human…"
					/>
				</div>
				<div class="form-group">
					<label class="form-label" for="step-{step.id}-assigner">Assigner</label>
					<input
						id="step-{step.id}-assigner"
						class="form-input"
						type="text"
						bind:value={editForm.assigner}
						placeholder="orchestrator"
					/>
				</div>
			</div>
			{#if dependencyOptions.length > 0}
				<fieldset class="dep-picker">
					<legend class="form-label">Depends on</legend>
					{#each dependencyOptions as candidate (candidate.id)}
						<label class="check-row">
							<input type="checkbox" value={candidate.id} bind:group={editDeps} />
							<span class="ref-chip">#{objective?.alias}.{candidate.stepNumber}</span>
							<span>{candidate.title}</span>
							<StatusBadge status={candidate.status} sm />
						</label>
					{/each}
				</fieldset>
			{/if}
			<div class="form-actions inline-actions">
				<button
					type="button"
					class="btn btn-ghost btn-sm"
					onclick={() => (editing = false)}
				>
					Cancel
				</button>
				<button type="submit" class="btn btn-primary btn-sm" disabled={saving}>
					{saving ? 'Saving…' : 'Save step'}
				</button>
			</div>
		</form>
	{:else}
		<div class="step-title-row">
			<h3 class="step-title">{step.title}</h3>
			<label class="status-control">
				<span class="sr-only">Step status</span>
				<select
					class="status-select"
					value={step.status}
					onchange={setStatus}
					disabled={statusSaving}
				>
					{#each STEP_STATUSES as status}
						<option value={status}>{statusLabel(status)}</option>
					{/each}
				</select>
			</label>
		</div>

		<div class="step-meta">
			{#if step.assignee}
				<span class="assignee">@{actorLabel(step.assignee)}</span>
			{/if}
			{#if step.assigner}
				<span>assigned by {actorLabel(step.assigner)}</span>
			{/if}
			<span title={formatDate(step.updatedAt)}>updated {timeAgo(step.updatedAt)}</span>
		</div>

		{#if deps.length > 0}
			<div class="dep-row">
				<span class="dep-label">Depends on</span>
				{#each deps as dep (dep.stepId)}
					<a
						class="dep-chip {dep.status === 'done' ? 'is-done' : 'is-pending'}"
						href="#/campaign/{campaignId}/objective/{dep.objectiveAlias}"
						title={dep.title}
					>
						#{dep.objectiveAlias}.{dep.stepNumber}
						<span class="dep-chip-status"
							>{dep.status === 'done' ? 'done' : dep.status}</span
						>
					</a>
				{/each}
			</div>
		{/if}

		{#if unmet.length > 0 && step.status !== 'blocked'}
			<p class="blocked-note">
				Waiting on {unmet.map((d) => `#${d.objectiveAlias}.${d.stepNumber}`).join(', ')}
			</p>
		{/if}

		{#if step.context}
			<div class="context-block">
				<button
					type="button"
					class="text-toggle"
					onclick={() => (contextOpen = !contextOpen)}
					aria-expanded={contextOpen}
					>{contextOpen ? 'Hide context' : 'Show context'}</button
				>
				{#if contextOpen}
					<div class="context-body">
						<Markdown source={step.context} {campaignId} />
					</div>
				{/if}
			</div>
		{/if}

		{#if latest}
			<div class="latest-update">
				<div class="latest-update-head">
					<StatusBadge
						status={latest.kind}
						sm
						tone={latest.kind === 'Blocker'
							? 'blocked'
							: latest.kind === 'Result'
								? 'done'
								: 'neutral'}
					/>
					<span class="latest-update-meta"
						>{actorLabel(latest.actor)} · {timeAgo(latest.createdAt)}</span
					>
				</div>
				<div class="latest-update-body">
					<Markdown source={latest.body} {campaignId} clamp={2} collapsible />
				</div>
			</div>
		{/if}

		<div class="step-foot">
			<button
				type="button"
				class="text-toggle"
				onclick={() => (historyOpen = !historyOpen)}
				aria-expanded={historyOpen}
			>
				{historyOpen ? 'Hide' : 'View'} updates ({updates.length}) &amp; evidence ({evidence.length})
			</button>
			<span class="step-foot-actions">
				<button
					type="button"
					class="btn btn-ghost btn-sm"
					onclick={() => {
						showUpdate = !showUpdate;
						showEvidence = false;
					}}
				>
					+ Update
				</button>
				<button
					type="button"
					class="btn btn-ghost btn-sm"
					onclick={() => {
						showEvidence = !showEvidence;
						showUpdate = false;
					}}
				>
					+ Evidence
				</button>
			</span>
		</div>

		{#if showUpdate}
			<form class="inline-form" onsubmit={postUpdate}>
				<div class="form-row">
					<div class="form-group">
						<label class="form-label" for="update-body-{step.id}">Update</label>
						<textarea
							id="update-body-{step.id}"
							class="form-textarea"
							rows="3"
							bind:value={updateBody}
							placeholder="Progress, a blocker, a result, or a handoff."></textarea>
					</div>
					<div class="form-group narrow">
						<label class="form-label" for="update-kind-{step.id}">Kind</label>
						<select
							id="update-kind-{step.id}"
							class="form-select"
							bind:value={updateKind}
						>
							{#each UPDATE_KINDS as kind}<option value={kind}>{kind}</option>{/each}
						</select>
					</div>
				</div>
				<div class="form-actions inline-actions">
					<button
						type="button"
						class="btn btn-ghost btn-sm"
						onclick={() => (showUpdate = false)}>Cancel</button
					>
					<button type="submit" class="btn btn-primary btn-sm" disabled={updateSaving}>
						{updateSaving ? 'Posting…' : 'Post update'}
					</button>
				</div>
			</form>
		{/if}

		{#if showEvidence}
			<form class="inline-form" onsubmit={postEvidence}>
				<div class="form-row">
					<div class="form-group">
						<label class="form-label" for="evidence-label-{step.id}">Label</label>
						<input
							id="evidence-label-{step.id}"
							class="form-input"
							type="text"
							bind:value={evidenceLabel}
							placeholder="What does this prove?"
						/>
					</div>
					<div class="form-group narrow">
						<label class="form-label" for="evidence-kind-{step.id}">Kind</label>
						<select
							id="evidence-kind-{step.id}"
							class="form-select"
							bind:value={evidenceKind}
						>
							{#each EVIDENCE_KINDS as kind}<option value={kind}>{kind}</option
								>{/each}
						</select>
					</div>
				</div>
				<div class="form-group">
					<label class="form-label" for="evidence-value-{step.id}">Value</label>
					<input
						id="evidence-value-{step.id}"
						class="form-input"
						type="text"
						bind:value={evidenceValue}
						placeholder="https://… or a commit sha"
					/>
				</div>
				<div class="form-actions inline-actions">
					<button
						type="button"
						class="btn btn-ghost btn-sm"
						onclick={() => (showEvidence = false)}>Cancel</button
					>
					<button type="submit" class="btn btn-primary btn-sm" disabled={evidenceSaving}>
						{evidenceSaving ? 'Attaching…' : 'Attach evidence'}
					</button>
				</div>
			</form>
		{/if}

		{#if historyOpen}
			<div class="step-history">
				<div class="history-column">
					<p class="zone-label">Updates</p>
					{#if updates.length === 0}
						<p class="micro-empty">
							No updates yet. The latest update appears here when work moves.
						</p>
					{:else}
						<div class="update-thread">
							{#each updates as update (update.id)}
								<div class="update-item">
									<div class="update-head">
										<StatusBadge
											status={update.kind}
											sm
											tone={update.kind === 'Blocker'
												? 'blocked'
												: update.kind === 'Result'
													? 'done'
													: update.kind === 'Handoff'
														? 'review'
														: 'neutral'}
										/>
										<span class="update-meta"
											>{actorLabel(update.actor)} · {timeAgo(
												update.createdAt
											)}</span
										>
									</div>
									<div class="update-body">
										<Markdown source={update.body} {campaignId} />
									</div>
								</div>
							{/each}
						</div>
					{/if}
				</div>

				<div class="history-column">
					<p class="zone-label">Evidence</p>
					{#if evidence.length === 0}
						<p class="micro-empty">
							No evidence attached. Attach proof when marking work done.
						</p>
					{:else}
						<ul class="evidence-list">
							{#each evidence as item (item.id)}
								<li class="evidence-item">
									<span class="evidence-kind">{item.kind}</span>
									<span class="evidence-label">{item.label}</span>
									{#if looksLikeUrl(item.value)}
										<a
											class="evidence-value"
											href={item.value}
											target="_blank"
											rel="noopener noreferrer">{item.value}</a
										>
									{:else if item.value}
										<code class="evidence-value">{item.value}</code>
									{/if}
									<span class="evidence-meta"
										>{actorLabel(item.createdBy)} · {timeAgo(
											item.createdAt
										)}</span
									>
								</li>
							{/each}
						</ul>
					{/if}
				</div>
			</div>
		{/if}
	{/if}
</article>
