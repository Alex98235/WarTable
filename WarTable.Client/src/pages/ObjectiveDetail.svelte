<script>
	import {
		ACTOR,
		apiDelete,
		apiGet,
		apiPost,
		apiPut,
		stepProgress,
		timeAgo,
		formatDate
	} from '../lib/api.js';
	import { addToast, showConfirm } from '../lib/stores.js';
	import { onActivity } from '../lib/events.svelte.js';
	import Markdown from '../components/Markdown.svelte';
	import MarkdownEditor from '../components/MarkdownEditor.svelte';
	import StatusBadge from '../components/StatusBadge.svelte';
	import StepCard from '../components/StepCard.svelte';

	let { route } = $props();
	let campaignId = $derived(route.params.id);
	let alias = $derived(parseInt(route.params.alias, 10));

	let campaign = $state(null);
	let objective = $state(null);

	let loading = $state(true);
	let refreshing = $state(false);
	let error = $state('');
	let notFound = $state(false);
	let loadToken = 0;

	let editing = $state(false);
	let editForm = $state({ title: '', description: '' });
	let saving = $state(false);

	let showAddStep = $state(false);
	let newStep = $state({
		title: '',
		context: '',
		assignee: '',
		assigner: '',
		dependsOn: []
	});
	let addingStep = $state(false);

	let steps = $derived(
		[...(objective?.steps || [])].sort(
			(a, b) =>
				(a.sortOrder ?? 0) - (b.sortOrder ?? 0) ||
				new Date(a.createdAt) - new Date(b.createdAt)
		)
	);

	let progress = $derived(stepProgress(steps));

	let readySteps = $derived(
		steps.filter(
			(step) =>
				step.status === 'open' &&
				objective?.planState === 'Active' &&
				(step.dependsOn || []).every((dep) => dep.status === 'done')
		)
	);

	let waitingSteps = $derived(
		steps.filter(
			(step) =>
				step.status !== 'done' &&
				(step.status === 'blocked' ||
					(step.dependsOn || []).some((dep) => dep.status !== 'done'))
		)
	);

	function go(path) {
		window.location.hash = path;
	}

	async function load({ silent = false } = {}) {
		if (silent) refreshing = true;
		else loading = true;
		error = '';
		notFound = false;
		const token = ++loadToken;

		try {
			const campaignData = await apiGet(`/api/campaigns/${campaignId}`);
			if (token !== loadToken) return;

			campaign = campaignData;
			const summary = (campaignData.objectives || []).find((item) => item.alias === alias);

			if (!summary) {
				notFound = true;
				objective = null;
				return;
			}

			const objectiveData = await apiGet(`/api/objectives/${summary.id}`);
			if (token !== loadToken) return;

			objective = objectiveData;
		} catch (err) {
			if (token !== loadToken) return;
			if (String(err.message || '').includes('404')) notFound = true;
			else error = err.message || 'Could not load the objective.';
			addToast('Could not load the objective.', 'error');
		} finally {
			if (token === loadToken) {
				loading = false;
				refreshing = false;
			}
		}
	}

	function openEdit() {
		editForm = {
			title: objective?.title || '',
			description: objective?.description || ''
		};
		editing = true;
	}

	async function saveObjective(event) {
		event.preventDefault();
		if (!objective) return;
		saving = true;
		try {
			await apiPut(`/api/objectives/${objective.id}`, {
				title: editForm.title.trim(),
				description: editForm.description,
				actor: ACTOR
			});
			addToast('Objective updated.', 'success');
			editing = false;
			await load({ silent: true });
		} catch (err) {
			addToast(err.message || 'Could not save the objective.', 'error');
		} finally {
			saving = false;
		}
	}

	async function setPlanState(planState) {
		if (!objective || objective.planState === planState) return;
		try {
			await apiPut(`/api/objectives/${objective.id}`, {
				planState,
				actor: ACTOR
			});
			addToast(
				planState === 'Active'
					? 'Objective activated.'
					: planState === 'Seed'
						? 'Objective moved back to a seed.'
						: 'Objective parked as a draft.',
				'success'
			);
			await load({ silent: true });
		} catch (err) {
			addToast(err.message || 'Could not change the plan state.', 'error');
		}
	}

	function deleteObjective() {
		if (!objective) return;
		showConfirm(
			`Delete objective #${objective.alias} "${objective.title}" and its steps? This cannot be undone.`,
			async () => {
				try {
					await apiDelete(`/api/objectives/${objective.id}`, {
						actor: ACTOR
					});
					addToast('Objective deleted.', 'success');
					go(`/campaign/${campaignId}`);
				} catch (err) {
					addToast(err.message || 'Could not delete the objective.', 'error');
				}
			}
		);
	}

	async function moveStep(step, direction) {
		const index = steps.findIndex((candidate) => candidate.id === step.id);
		const swapIndex = index + direction;
		if (index < 0 || swapIndex < 0 || swapIndex >= steps.length) return;

		const ids = steps.map((candidate) => candidate.id);
		[ids[index], ids[swapIndex]] = [ids[swapIndex], ids[index]];
		const orders = Object.fromEntries(ids.map((id, position) => [id, position]));

		try {
			await apiPut('/api/steps/reorder', {
				objectiveId: objective.id,
				orders,
				actor: ACTOR
			});
			await load({ silent: true });
		} catch (err) {
			addToast(err.message || 'Could not reorder steps.', 'error');
		}
	}

	function openAddStep() {
		newStep = {
			title: '',
			context: '',
			assignee: '',
			assigner: '',
			dependsOn: []
		};
		showAddStep = true;
	}

	async function createStep(event) {
		event.preventDefault();
		if (!objective) return;
		if (!newStep.title.trim()) {
			addToast('A step needs a title.', 'warning');
			return;
		}
		addingStep = true;
		try {
			const created = await apiPost('/api/steps', {
				objectiveId: objective.id,
				title: newStep.title.trim(),
				context: newStep.context,
				assignee: newStep.assignee,
				assigner: newStep.assigner,
				dependsOn: newStep.dependsOn,
				actor: ACTOR
			});
			addToast(`Step #${objective.alias}.${created.stepNumber} added.`, 'success');
			showAddStep = false;
			newStep = {
				title: '',
				context: '',
				assignee: '',
				assigner: '',
				dependsOn: []
			};
			await load({ silent: true });
		} catch (err) {
			addToast(err.message || 'Could not add the step.', 'error');
		} finally {
			addingStep = false;
		}
	}

	load();

	$effect(() => {
		return onActivity(() => load({ silent: true }));
	});
</script>

<svelte:head>
	<title
		>{objective
			? `#${objective.alias} ${objective.title} — WarTable`
			: 'Objective — WarTable'}</title
	>
</svelte:head>

{#if loading}
	<div class="loading-state">
		<div class="loading-spinner"></div>
		<p>Opening the objective…</p>
	</div>
{:else if error}
	<div class="error-state">
		<p class="eyebrow">Connection trouble</p>
		<h1>The objective could not be opened.</h1>
		<p class="lede">{error}</p>
		<div class="page-actions">
			<button type="button" class="btn btn-primary" onclick={() => load()}>Try again</button>
			<a class="btn btn-secondary" href="#/campaign/{campaignId}">Back to campaign</a>
		</div>
	</div>
{:else if notFound || !objective}
	<div class="empty-state">
		<h3>Objective not found</h3>
		<p>
			No objective with alias #{alias} exists in this campaign. Old references still resolve until
			the campaign is deleted.
		</p>
		<a class="btn btn-primary" href="#/campaign/{campaignId}">Back to campaign</a>
	</div>
{:else}
	<div class="objective-page">
		<div class="page-intro reveal" style="--i: 0">
			<div>
				<a class="back-link" href="#/campaign/{campaignId}"
					>← {campaign?.title || 'Campaign'}</a
				>
				<div class="objective-ids">
					<span class="ref-chip large">#{objective.alias}</span>
					<StatusBadge status={objective.planState} />
					<StatusBadge status={objective.status || 'planned'} />
					{#if readySteps.length > 0}
						<span class="chip chip-ready">{readySteps.length} ready</span>
					{/if}
					{#if waitingSteps.length > 0}
						<span class="chip chip-blocked">{waitingSteps.length} waiting</span>
					{/if}
				</div>
				<h1 class="page-title objective-title">{objective.title}</h1>
				<p class="objective-dates">
					Created {formatDate(objective.createdAt)} · updated {timeAgo(
						objective.updatedAt
					)}
				</p>
			</div>
			<div class="page-actions">
				{#if refreshing}<span class="sync-note">Syncing…</span>{/if}
				<button type="button" class="btn btn-secondary" onclick={openEdit}>Edit</button>
				<button type="button" class="btn btn-primary" onclick={openAddStep}>+ Step</button>
			</div>
		</div>

		<div class="objective-controls reveal" style="--i: 1">
			<div class="control-block">
				<p class="zone-label">Plan state</p>
				<div class="segmented" role="group" aria-label="Plan state">
					<button
						type="button"
						class="seg-btn"
						class:is-active={objective.planState === 'Seed'}
						onclick={() => setPlanState('Seed')}>Seed</button
					>
					<button
						type="button"
						class="seg-btn"
						class:is-active={objective.planState === 'Draft'}
						onclick={() => setPlanState('Draft')}>Draft</button
					>
					<button
						type="button"
						class="seg-btn"
						class:is-active={objective.planState === 'Active'}
						onclick={() => setPlanState('Active')}>Active</button
					>
				</div>
				<p class="control-hint">
					{objective.planState === 'Seed'
						? 'Seed: an unfinished thought. Shape it into a draft when it is ready.'
						: objective.planState === 'Draft'
							? 'Parked: visible, but not in the ready set.'
							: 'Active: open, unblocked steps are ready to pull.'}
				</p>
			</div>

			<div class="control-block">
				<p class="zone-label">Derived status</p>
				<div class="control-line">
					<StatusBadge status={objective.status || 'planned'} />
					<span class="control-hint">computed from steps, not edited by hand</span>
				</div>
			</div>

			<div class="control-block grow">
				<p class="zone-label">Progress</p>
				<div class="progress-row">
					<div class="progress-track" aria-hidden="true">
						<span class="progress-fill" style="width: {progress.pct}%"></span>
					</div>
					<span class="progress-numbers">{progress.done}/{progress.total} steps</span>
				</div>
				<p class="control-hint">
					{progress.flagged > 0 ? `${progress.flagged} flagged · ` : ''}
					{progress.blocked > 0 ? `${progress.blocked} blocked · ` : ''}
					{progress.inProgress > 0 ? `${progress.inProgress} in progress · ` : ''}
					{progress.total - progress.done} remaining
				</p>
			</div>
		</div>

		{#if objective.description}
			<section class="panel description-panel reveal" style="--i: 2">
				<header class="panel-head compact">
					<div class="panel-heading">
						<p class="eyebrow">Outcome &amp; constraints</p>
						<h2 class="panel-title">Description</h2>
					</div>
				</header>
				<div class="description-body">
					<Markdown source={objective.description} {campaignId} />
				</div>
			</section>
		{/if}

		<section class="panel steps-panel reveal" style="--i: 3">
			<header class="panel-head">
				<div class="panel-heading">
					<p class="eyebrow">Atomic work</p>
					<h2 class="panel-title">Steps</h2>
					<p class="panel-subtitle">
						Dependencies are explicit. Ready means open, unblocked, and in an active
						objective.
					</p>
				</div>
				<div class="panel-head-actions">
					<span class="inbox-count">{steps.length}</span>
					<button type="button" class="btn btn-ghost btn-sm" onclick={openAddStep}
						>+ Step</button
					>
				</div>
			</header>

			{#if steps.length === 0}
				<div class="calm-state">
					<span class="calm-mark" aria-hidden="true">▸</span>
					<div>
						<h3>No steps yet.</h3>
						<p>
							Break the outcome into verifiable units. Add dependencies where order
							matters.
						</p>
						<button type="button" class="btn btn-primary btn-sm" onclick={openAddStep}
							>Add the first step</button
						>
					</div>
				</div>
			{:else}
				<div class="step-card-list">
					{#each steps as step, index (step.id)}
						<StepCard
							{step}
							{objective}
							{campaignId}
							allSteps={steps}
							onchanged={() => load({ silent: true })}
							onMoveUp={() => moveStep(step, -1)}
							onMoveDown={() => moveStep(step, 1)}
							canMoveUp={index > 0}
							canMoveDown={index < steps.length - 1}
						/>
					{/each}
				</div>
			{/if}

			{#if showAddStep}
				<form class="add-step-panel" onsubmit={createStep}>
					<div class="add-step-head">
						<div>
							<p class="eyebrow">New step</p>
							<h3>Add atomic work</h3>
						</div>
						<button
							type="button"
							class="modal-close"
							onclick={() => (showAddStep = false)}
							aria-label="Close">×</button
						>
					</div>
					<div class="form-group">
						<label class="form-label" for="new-step-title">Title</label>
						<input
							id="new-step-title"
							class="form-input"
							type="text"
							bind:value={newStep.title}
							required
						/>
					</div>
					<div class="form-group">
						<label class="form-label" for="new-step-context"
							>Context <span class="optional">(markdown)</span></label
						>
						<MarkdownEditor
							id="new-step-context"
							bind:value={newStep.context}
							{campaignId}
							rows={3}
						/>
					</div>
					<div class="form-row">
						<div class="form-group">
							<label class="form-label" for="new-step-assignee">Assignee</label>
							<input
								id="new-step-assignee"
								class="form-input"
								type="text"
								bind:value={newStep.assignee}
								placeholder="agent:fixer, human…"
							/>
						</div>
						<div class="form-group">
							<label class="form-label" for="new-step-assigner">Assigner</label>
							<input
								id="new-step-assigner"
								class="form-input"
								type="text"
								bind:value={newStep.assigner}
								placeholder="orchestrator"
							/>
						</div>
					</div>
					{#if steps.length > 0}
						<fieldset class="dep-picker">
							<legend class="form-label">Depends on</legend>
							{#each steps as candidate (candidate.id)}
								<label class="check-row">
									<input
										type="checkbox"
										value={candidate.id}
										bind:group={newStep.dependsOn}
									/>
									<span class="ref-chip"
										>#{objective.alias}.{candidate.stepNumber}</span
									>
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
							onclick={() => (showAddStep = false)}>Cancel</button
						>
						<button type="submit" class="btn btn-primary btn-sm" disabled={addingStep}>
							{addingStep ? 'Adding…' : 'Add step'}
						</button>
					</div>
				</form>
			{/if}
		</section>
	</div>
{/if}

{#if editing}
	<div
		class="modal-overlay"
		role="presentation"
		onclick={(e) => {
			if (e.target === e.currentTarget) editing = false;
		}}
	>
		<div
			class="modal modal-wide"
			role="dialog"
			aria-modal="true"
			aria-labelledby="edit-objective-title"
			tabindex="-1"
			onclick={(e) => e.stopPropagation()}
			onkeydown={(e) => {
				if (e.key === 'Escape') editing = false;
			}}
		>
			<form onsubmit={saveObjective}>
				<div class="modal-header">
					<div>
						<p class="eyebrow">#{objective?.alias}</p>
						<h2 id="edit-objective-title">Edit objective</h2>
					</div>
					<button
						type="button"
						class="modal-close"
						onclick={() => (editing = false)}
						aria-label="Close">×</button
					>
				</div>
				<div class="modal-body">
					<div class="form-group">
						<label class="form-label" for="edit-objective-title-input">Title</label>
						<input
							id="edit-objective-title-input"
							class="form-input"
							type="text"
							bind:value={editForm.title}
							required
						/>
					</div>
					<div class="form-group">
						<label class="form-label" for="edit-objective-description"
							>Description <span class="optional">(markdown)</span></label
						>
						<MarkdownEditor
							id="edit-objective-description"
							bind:value={editForm.description}
							{campaignId}
							rows={7}
						/>
					</div>
				</div>
				<div class="modal-actions spread">
					<button type="button" class="btn btn-danger" onclick={deleteObjective}
						>Delete objective</button
					>
					<span class="modal-actions-group">
						<button
							type="button"
							class="btn btn-ghost"
							onclick={() => (editing = false)}>Cancel</button
						>
						<button type="submit" class="btn btn-primary" disabled={saving}>
							{saving ? 'Saving…' : 'Save objective'}
						</button>
					</span>
				</div>
			</form>
		</div>
	</div>
{/if}
