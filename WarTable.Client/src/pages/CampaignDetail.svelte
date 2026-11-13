<script>
	import {
		ACTOR,
		apiDelete,
		apiGet,
		apiPatch,
		apiPost,
		apiPostAction,
		apiPut,
		isConflict,
		parseTags,
		plainText,
		stepProgress,
		timeAgo,
		latestUpdate,
		actorLabel
	} from '../lib/api.js';
	import { addToast, showConfirm } from '../lib/stores.js';
	import { onActivity } from '../lib/events.svelte.js';
	import Markdown from '../components/Markdown.svelte';
	import MarkdownEditor from '../components/MarkdownEditor.svelte';
	import StatusBadge from '../components/StatusBadge.svelte';
	import SeedPreview from '../components/SeedPreview.svelte';

	let { route } = $props();
	let campaignId = $derived(route.params.id);

	let campaign = $state(null);
	let objectives = $state([]);
	let objectiveDetails = $state({});
	let notes = $state([]);
	let preferences = $state([]);

	let loading = $state(true);
	let refreshing = $state(false);
	let error = $state('');
	let loadToken = 0;

	let expanded = $state({});
	let expansionInitialized = false;

	let finishedOpen = $state(false);
	let finishedInitialized = false;

	let railSearch = $state('');

	let editingCampaign = $state(false);
	let campaignForm = $state({ title: '', tags: '' });
	let campaignSaving = $state(false);

	let editingCharter = $state(false);
	let charterForm = $state({ body: '' });
	let charterSaving = $state(false);

	let showObjectiveModal = $state(false);
	let objectiveForm = $state({
		title: '',
		description: '',
		planState: 'Backlog'
	});
	let objectiveSaving = $state(false);

	let editingObjective = $state(null);
	let objectiveEditForm = $state({
		title: '',
		description: '',
		planState: 'Backlog'
	});
	let objectiveEditSaving = $state(false);
	let shapingSeed = $state(false);

	let seedForm = $state({ title: '', description: '' });
	let seedDetailOpen = $state(false);
	let seedSaving = $state(false);
	let seedInputEl = $state(null);

	// Capture feedback. `parkedSeed` drives the inline confirmation in the strip;
	// `highlightSeedId` drives the row flash. Both are set only by a park from
	// this client, never derived from updatedAt, so a live update from another
	// device cannot make an old seed look new.
	let parkedSeed = $state(null);
	let highlightSeedId = $state(null);
	let parkedTimer = null;
	let highlightTimer = null;

	let previewSeed = $state(null);
	let seedOpener = null;

	let seedObjectives = $derived(
		objectives
			.filter((objective) => objective.planState === 'Seed')
			.map((objective) => objectiveDetails[objective.id] || objective)
			.sort((a, b) => String(b.updatedAt || '').localeCompare(String(a.updatedAt || '')))
	);

	let planObjectives = $derived(
		[...objectives]
			.filter((objective) => objective.planState !== 'Seed')
			.sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
			.map((objective) => objectiveDetails[objective.id] || objective)
	);

	// The plan is split into ranked tiers so the reader sees live work before
	// parked captures and finished outcomes. A completed objective always lands
	// in Finished, even if it was parked as backlog when it finished.
	let finishedObjectives = $derived(
		planObjectives.filter((objective) => objective.status === 'completed')
	);

	let inPlayObjectives = $derived(
		planObjectives.filter(
			(objective) => objective.status !== 'completed' && objective.planState === 'Ready'
		)
	);

	let backlogObjectives = $derived(
		planObjectives.filter(
			(objective) => objective.status !== 'completed' && objective.planState === 'Backlog'
		)
	);

	let tags = $derived(campaign ? parseTags(campaign.tags) : []);

	let railNotes = $derived(
		notes
			.filter((note) => note.status === 'Active' && note.kind !== 'Directive')
			.filter((note) => {
				const query = railSearch.trim().toLowerCase();
				if (!query) return true;
				return (
					(note.title || '').toLowerCase().includes(query) ||
					(note.body || '').toLowerCase().includes(query) ||
					(note.tags || '').toLowerCase().includes(query)
				);
			})
			.slice(0, 6)
	);

	let totalSteps = $derived(
		planObjectives.reduce((sum, objective) => sum + (objective.steps?.length || 0), 0)
	);

	let completedSteps = $derived(
		planObjectives.reduce(
			(sum, objective) =>
				sum + (objective.steps || []).filter((step) => step.status === 'done').length,
			0
		)
	);

	let attentionCount = $derived(
		planObjectives.reduce((sum, obj) => {
			return (
				sum +
				(obj.steps || []).filter((s) => s.status === 'flagged' || s.status === 'blocked')
					.length
			);
		}, 0)
	);

	let allActiveNotes = $derived(notes.filter((n) => n.status === 'Active'));
	let directiveCount = $derived(allActiveNotes.filter((n) => n.kind === 'Directive').length);
	let railDirectives = $derived(allActiveNotes.filter((n) => n.kind === 'Directive').slice(0, 2));

	async function load({ silent = false } = {}) {
		if (silent) refreshing = true;
		else loading = true;
		error = '';
		const token = ++loadToken;

		try {
			const [campaignData, objectiveData, noteData, prefData] = await Promise.all([
				apiGet(`/api/campaigns/${campaignId}`),
				apiGet('/api/objectives', { campaignId }),
				apiGet('/api/notes', { campaignId }),
				apiGet('/api/preferences').catch(() => [])
			]);

			if (token !== loadToken) return;

			campaign = campaignData;
			notes = Array.isArray(noteData) ? noteData : [];
			objectives = Array.isArray(objectiveData) ? objectiveData : [];
			preferences = Array.isArray(prefData) ? prefData.filter((p) => p.active) : [];

			const details = await Promise.all(
				objectives.map((objective) =>
					apiGet(`/api/objectives/${objective.id}`).catch(() => objective)
				)
			);
			if (token !== loadToken) return;

			objectiveDetails = Object.fromEntries(details.map((detail) => [detail.id, detail]));

			if (!expansionInitialized) {
				expanded = Object.fromEntries(
					details.map((detail) => [
						detail.id,
						(detail.steps || []).some(
							(step) => step.status === 'flagged' || step.status === 'blocked'
						)
					])
				);
				expansionInitialized = true;
			}

			// Only auto-open Finished when there is nothing else to see. The state is
			// then left alone so live updates never yank the reader's toggle.
			if (!finishedInitialized) {
				const plan = details.filter((objective) => objective.planState !== 'Seed');
				const hasOpenWork = plan.some((objective) => objective.status !== 'completed');
				finishedOpen = plan.length > 0 && !hasOpenWork;
				finishedInitialized = true;
			}
		} catch (err) {
			if (token !== loadToken) return;
			error = err.message || 'Could not load the campaign.';
			addToast('Could not load the campaign.', 'error');
		} finally {
			if (token === loadToken) {
				loading = false;
				refreshing = false;
			}
		}
	}

	function go(path) {
		window.location.hash = path;
	}

	function toggleExpanded(id) {
		expanded = { ...expanded, [id]: !expanded[id] };
	}

	function objectiveHref(objective) {
		return `#/campaign/${campaignId}/objective/${objective.alias}`;
	}

	function noteHref(note) {
		return `#/campaign/${campaignId}/bunker/${note.alias}`;
	}

	function openEditCampaign() {
		campaignForm = {
			title: campaign?.title || '',
			tags: Array.isArray(campaign?.tags) ? campaign.tags.join(', ') : campaign?.tags || ''
		};
		editingCampaign = true;
	}

	async function saveCampaign(event) {
		event.preventDefault();
		campaignSaving = true;
		try {
			await apiPut(`/api/campaigns/${campaignId}`, {
				title: campaignForm.title.trim(),
				tags: parseTags(campaignForm.tags).join(', '),
				actor: ACTOR,
				rowVersion: campaign?.rowVersion
			});
			addToast('Campaign updated.', 'success');
			editingCampaign = false;
			await load({ silent: true });
		} catch (err) {
			if (isConflict(err)) {
				showConfirm(
					'This campaign was changed elsewhere while you were editing. Your changes are not saved. Reload to see the current version?',
					async () => {
						editingCampaign = false;
						await load();
					}
				);
				return;
			}
			addToast(err.message || 'Could not update the campaign.', 'error');
		} finally {
			campaignSaving = false;
		}
	}

	function openCharterEditor() {
		charterForm = { body: campaign?.charter || '' };
		editingCharter = true;
	}

	async function saveCharter(event) {
		event.preventDefault();
		charterSaving = true;
		try {
			await apiPut(`/api/campaigns/${campaignId}`, {
				charter: charterForm.body,
				actor: ACTOR,
				rowVersion: campaign?.rowVersion
			});
			addToast('Charter saved.', 'success');
			editingCharter = false;
			await load({ silent: true });
		} catch (err) {
			if (isConflict(err)) {
				showConfirm(
					'The charter was changed elsewhere while you were editing. Your changes are not saved. Reload to see the current version?',
					async () => {
						editingCharter = false;
						await load();
					}
				);
				return;
			}
			addToast(err.message || 'Could not save the charter.', 'error');
		} finally {
			charterSaving = false;
		}
	}

	function count(n, word) {
		return `${n} ${word}${n === 1 ? '' : 's'}`;
	}

	async function setArchived(archived) {
		campaignSaving = true;
		try {
			await apiPostAction(
				`/api/campaigns/${campaignId}/${archived ? 'archive' : 'unarchive'}`,
				{
					actor: ACTOR,
					rowVersion: campaign?.rowVersion
				}
			);
			addToast(archived ? 'Campaign archived.' : 'Campaign restored.', 'success');
			editingCampaign = false;
			await load({ silent: true });
		} catch (err) {
			if (isConflict(err)) {
				addToast(
					'This campaign was changed elsewhere. Reopen the editor and try again.',
					'error'
				);
				return;
			}
			addToast(err.message || 'Could not change the campaign archive state.', 'error');
		} finally {
			campaignSaving = false;
		}
	}

	function deleteCampaign() {
		const stepCount = objectives.reduce((n, o) => n + (o.steps?.length ?? 0), 0);
		const scale = `${count(objectives.length, 'objective')}, ${count(stepCount, 'step')} and ${count(notes.length, 'note')}`;
		showConfirm(
			`Delete "${campaign?.title}" and everything in it — ${scale}? This cannot be undone. If you only want it out of the way, archive it instead: archiving keeps all of it and can be reversed.`,
			async () => {
				try {
					await apiDelete(`/api/campaigns/${campaignId}`, {
						actor: ACTOR,
						rowVersion: campaign?.rowVersion
					});
					addToast('Campaign deleted.', 'success');
					go('/campaigns');
				} catch (err) {
					if (isConflict(err)) {
						addToast(
							'This campaign was changed elsewhere. Reload to see the current state.',
							'error'
						);
						return;
					}
					addToast(err.message || 'Could not delete the campaign.', 'error');
				}
			},
			{ confirmLabel: 'Delete permanently', holdMs: 6000 }
		);
	}

	function openObjectiveModal() {
		objectiveForm = { title: '', description: '', planState: 'Backlog' };
		showObjectiveModal = true;
	}

	function openObjectiveEdit(objective) {
		shapingSeed = false;
		editingObjective = objective;
		objectiveEditForm = {
			title: objective.title || '',
			description: objective.description || '',
			planState: objective.planState || 'Backlog'
		};
	}

	function shapeSeed(seed) {
		shapingSeed = true;
		editingObjective = seed;
		objectiveEditForm = {
			title: seed.title || '',
			description: seed.description || '',
			planState: 'Backlog'
		};
	}

	async function saveObjectiveEdit(event) {
		event.preventDefault();
		if (!editingObjective) return;
		objectiveEditSaving = true;
		try {
			await apiPut(`/api/objectives/${editingObjective.id}`, {
				title: objectiveEditForm.title.trim(),
				description: objectiveEditForm.description,
				planState: objectiveEditForm.planState,
				actor: ACTOR,
				rowVersion: editingObjective.rowVersion
			});
			addToast(shapingSeed ? 'Seed shaped into the plan.' : 'Objective updated.', 'success');
			editingObjective = null;
			shapingSeed = false;
			expansionInitialized = false;
			await load({ silent: true });
		} catch (err) {
			if (isConflict(err)) {
				showConfirm(
					'This objective was changed elsewhere while you were editing. Your changes are not saved. Reload to see the current version?',
					async () => {
						editingObjective = null;
						shapingSeed = false;
						await load();
					}
				);
				return;
			}
			addToast(err.message || 'Could not update the objective.', 'error');
		} finally {
			objectiveEditSaving = false;
		}
	}

	async function parkSeed(event) {
		event.preventDefault();
		seedSaving = true;
		try {
			const created = await apiPost('/api/objectives', {
				campaignId,
				title: seedForm.title.trim(),
				description: seedForm.description,
				planState: 'Seed',
				actor: ACTOR
			});
			seedForm = { title: '', description: '' };
			seedDetailOpen = false;
			await load({ silent: true });
			announceParked(created);
			seedInputEl?.focus();
		} catch (err) {
			addToast(err.message || 'Could not park the thought.', 'error');
		} finally {
			seedSaving = false;
		}
	}

	/**
	 * The confirmation names the alias and the destination, because the seed
	 * lands in Parked, which is often below the fold. It replaces the success
	 * toast: a toast 400px above the list cannot answer "did that work?".
	 */
	function announceParked(seed) {
		parkedSeed = { id: seed.id, alias: seed.alias };
		clearTimeout(parkedTimer);
		parkedTimer = setTimeout(() => (parkedSeed = null), 8000);
		highlightSeed(seed.id);
	}

	function highlightSeed(id) {
		highlightSeedId = id;
		clearTimeout(highlightTimer);
		highlightTimer = setTimeout(() => (highlightSeedId = null), 2400);
	}

	function dismissParked() {
		if (!parkedSeed) return;
		clearTimeout(parkedTimer);
		parkedSeed = null;
	}

	// No auto-scroll on park: it would yank a reader past the work they were
	// looking at. The confirmation offers the jump instead.
	function showParked() {
		if (!parkedSeed) return;
		const row = document.getElementById(`seed-${parkedSeed.id}`);
		if (!row) return;
		row.scrollIntoView({ block: 'center', behavior: 'smooth' });
		highlightSeed(parkedSeed.id);
	}

	function focusCapture() {
		seedInputEl?.focus();
	}

	function openSeedPreview(seed, opener) {
		seedOpener = opener || null;
		previewSeed = seed;
	}

	function closeSeedPreview() {
		previewSeed = null;
		const opener = seedOpener;
		seedOpener = null;
		if (opener) requestAnimationFrame(() => opener.focus());
	}

	// Shaping and deleting both leave the preview for another surface, so they
	// drop the opener instead of restoring focus to a row that is about to move.
	function shapeFromPreview() {
		const seed = previewSeed;
		previewSeed = null;
		seedOpener = null;
		if (seed) shapeSeed(seed);
	}

	function deleteFromPreview() {
		const seed = previewSeed;
		previewSeed = null;
		seedOpener = null;
		if (seed) deleteSeed(seed);
	}

	function deleteSeed(seed) {
		showConfirm(
			`Delete seed #${seed.alias} "${seed.title}"? This cannot be undone.`,
			async () => {
				try {
					await apiDelete(`/api/objectives/${seed.id}`, {
						actor: ACTOR,
						rowVersion: seed.rowVersion
					});
					addToast('Seed deleted.', 'success');
					await load({ silent: true });
				} catch (err) {
					if (isConflict(err)) {
						addToast(
							'This seed was changed elsewhere. Reload to see the current state.',
							'error'
						);
						return;
					}
					addToast(err.message || 'Could not delete the seed.', 'error');
				}
			}
		);
	}

	async function createObjective(event) {
		event.preventDefault();
		if (!objectiveForm.title.trim()) {
			addToast('An objective needs a title.', 'warning');
			return;
		}
		objectiveSaving = true;
		try {
			const created = await apiPost('/api/objectives', {
				campaignId,
				title: objectiveForm.title.trim(),
				description: objectiveForm.description,
				planState: objectiveForm.planState,
				actor: ACTOR
			});
			addToast(`Objective #${created.alias} created.`, 'success');
			showObjectiveModal = false;
			expansionInitialized = false;
			await load({ silent: true });
		} catch (err) {
			addToast(err.message || 'Could not create the objective.', 'error');
		} finally {
			objectiveSaving = false;
		}
	}

	async function setPlanState(objective, planState) {
		try {
			await apiPut(`/api/objectives/${objective.id}`, {
				planState,
				actor: ACTOR,
				rowVersion: objective.rowVersion
			});
			addToast(
				planState === 'Ready' ? 'Objective released.' : 'Objective moved to the backlog.',
				'success'
			);
			await load({ silent: true });
		} catch (err) {
			if (isConflict(err)) {
				addToast(
					'This objective was changed elsewhere. Reload to see the current state.',
					'error'
				);
				return;
			}
			addToast(err.message || 'Could not change the plan state.', 'error');
		}
	}

	/**
	 * Reordering only makes sense inside the In play tier. The swap happens
	 * between this objective and its nearest neighbour in that tier, then every
	 * plan objective is re-indexed so sort orders stay unique across tiers.
	 * Parked or finished objectives keep their relative order.
	 */
	async function moveObjective(objective, direction) {
		const index = inPlayObjectives.findIndex((candidate) => candidate.id === objective.id);
		const neighbour = inPlayObjectives[index + direction];
		if (index < 0 || !neighbour) return;

		const ids = planObjectives.map((candidate) => candidate.id);
		const from = ids.indexOf(objective.id);
		const to = ids.indexOf(neighbour.id);
		if (from < 0 || to < 0) return;

		[ids[from], ids[to]] = [ids[to], ids[from]];
		const orders = Object.fromEntries(ids.map((id, position) => [id, position]));

		try {
			await apiPut('/api/objectives/reorder', {
				campaignId,
				orders,
				actor: ACTOR
			});
			await load({ silent: true });
		} catch (err) {
			addToast(err.message || 'Could not reorder objectives.', 'error');
		}
	}

	async function toggleFlag(step) {
		const status = step.status === 'flagged' ? 'open' : 'flagged';
		try {
			await apiPatch(`/api/steps/${step.id}/status`, {
				status,
				actor: ACTOR,
				rowVersion: step.rowVersion
			});
			addToast(
				status === 'flagged' ? 'Step flagged for human attention.' : 'Flag cleared.',
				'success'
			);
			await load({ silent: true });
		} catch (err) {
			if (isConflict(err)) {
				addToast(
					'This step was changed elsewhere. Reload to see the current state.',
					'error'
				);
				return;
			}
			addToast(err.message || 'Could not update the step.', 'error');
		}
	}

	load();

	$effect(() => {
		return onActivity(() => load({ silent: true }));
	});
</script>

<svelte:head>
	<title>{campaign ? `${campaign.title} — WarTable` : 'Campaign — WarTable'}</title>
</svelte:head>

{#if loading}
	<div class="loading-state">
		<div class="loading-spinner"></div>
		<p>Opening the campaign…</p>
	</div>
{:else if error}
	<div class="error-state">
		<p class="eyebrow">Connection trouble</p>
		<h1>The campaign could not be opened.</h1>
		<p class="lede">{error}</p>
		<div class="page-actions">
			<button type="button" class="btn btn-primary" onclick={() => load()}>Try again</button>
			<a class="btn btn-secondary" href="#/campaigns">Back to campaigns</a>
		</div>
	</div>
{:else if !campaign}
	<div class="empty-state">
		<h3>Campaign not found</h3>
		<p>This campaign may have been deleted. The alias history it created is gone with it.</p>
		<a class="btn btn-primary" href="#/campaigns">Back to campaigns</a>
	</div>
{:else}
	<div class="campaign-hero reveal" style="--i: 0">
		<a class="back-link" href="#/campaigns">← All campaigns</a>
		{#if campaign.archivedAt}
			<div class="hero-archived">
				<div class="hero-archived-copy">
					<p class="eyebrow">Archived</p>
					<p>
						Hidden from the campaign list, and every objective, step and note inside is
						kept.
					</p>
				</div>
				<button
					type="button"
					class="btn btn-secondary btn-sm"
					disabled={campaignSaving}
					onclick={() => setArchived(false)}>Restore campaign</button
				>
			</div>
		{/if}
		<div class="hero-main">
			<div class="hero-copy">
				<p class="eyebrow">Campaign</p>
				<h1 class="page-title hero-title">{campaign.title}</h1>
				<div class="hero-meta">
					{#each tags as tag}<span class="tag">{tag}</span>{/each}
					<span class="hero-stat">{inPlayObjectives.length} in play</span>
					{#if finishedObjectives.length > 0}
						<span class="hero-stat">{finishedObjectives.length} finished</span>
					{/if}
					{#if totalSteps > 0}
						<span class="hero-stat">{completedSteps}/{totalSteps} steps done</span>
					{/if}
					<span class="hero-stat">updated {timeAgo(campaign.updatedAt)}</span>
					{#if attentionCount > 0}
						<a class="hero-stat hero-stat-attention" href="#/attention"
							>{attentionCount} need{attentionCount === 1 ? 's' : ''} you</a
						>
					{/if}
				</div>
				{#if preferences.length > 0}
					<div class="hero-rules">
						<span class="hero-rules-label"
							><span class="hero-rules-mark" aria-hidden="true">✦</span> Preferences</span
						>
						{#each preferences.slice(0, 3) as pref (pref.id)}
							<span class="pref-chip" title={pref.text} aria-label={pref.text}
								>{pref.text}</span
							>
						{/each}
						{#if preferences.length > 3}
							<a class="pref-chip pref-more" href="#/preferences"
								>+{preferences.length - 3} more</a
							>
						{/if}
					</div>
				{/if}
			</div>

			<div class="hero-actions">
				{#if refreshing}<span class="sync-note">Syncing…</span>{/if}
				<button type="button" class="btn btn-secondary" onclick={openEditCampaign}
					>Edit campaign</button
				>
				<a class="btn btn-secondary" href="#/campaign/{campaignId}/bunker">Bunker</a>
				<button type="button" class="btn btn-primary" onclick={openObjectiveModal}
					>+ Objective</button
				>
			</div>
		</div>

		<div class="hero-charter">
			{#if campaign.charter}
				<div class="hero-charter-head">
					<p class="eyebrow">Charter</p>
					<div class="hero-charter-actions">
						<button
							type="button"
							class="btn btn-ghost btn-sm"
							onclick={openCharterEditor}>Edit charter</button
						>
					</div>
				</div>
				<div class="hero-charter-body markdown-wrap">
					<Markdown
						source={campaign.charter}
						{campaignId}
						clamp={3}
						collapsible
						showLabel="Read charter"
					/>
				</div>
			{:else}
				<div class="hero-charter-empty">
					<span class="hero-charter-mark" aria-hidden="true">✦</span>
					<p>
						No charter yet. Write down why this campaign exists, what is in scope, and
						how it is put together.
					</p>
					<button
						type="button"
						class="btn btn-secondary btn-sm"
						onclick={openCharterEditor}>Write charter</button
					>
				</div>
			{/if}
		</div>
	</div>

	<div class="campaign-layout">
		<div class="campaign-main">
			<form class="capture-strip reveal" style="--i: 1" onsubmit={parkSeed}>
				<div class="capture-strip-row">
					<label class="capture-strip-label" for="seed-thought">Park a thought</label>
					<input
						id="seed-thought"
						class="capture-input"
						type="text"
						placeholder="Something unfinished…"
						bind:this={seedInputEl}
						bind:value={seedForm.title}
						oninput={dismissParked}
						required
					/>
					<div class="capture-strip-actions">
						<button
							type="button"
							class="text-toggle"
							aria-expanded={seedDetailOpen}
							onclick={() => (seedDetailOpen = !seedDetailOpen)}
							>{seedDetailOpen ? 'Hide detail' : 'Add detail'}</button
						>
						<button type="submit" class="btn btn-ghost btn-sm" disabled={seedSaving}>
							{seedSaving ? 'Parking…' : 'Park it'}
						</button>
					</div>
				</div>
				{#if parkedSeed}
					<p class="capture-confirm" role="status">
						<span class="capture-confirm-mark" aria-hidden="true">✓</span>
						<span>Parked as #{parkedSeed.alias} — in Parked below</span>
						<button type="button" class="text-toggle" onclick={showParked}
							>Show it</button
						>
					</p>
				{/if}
				{#if seedDetailOpen}
					<div class="capture-detail-wrap">
						<label class="sr-only" for="seed-thought-detail">Detail</label>
						<textarea
							id="seed-thought-detail"
							class="form-textarea capture-detail"
							rows="3"
							placeholder="Context, links, half-formed angles…"
							bind:value={seedForm.description}></textarea>
					</div>
				{/if}
				{#if seedObjectives.length === 0}
					<p class="capture-hint">
						A seed is a thought you are not ready to shape. It waits in Parked.
					</p>
				{/if}
			</form>

			<section class="panel plan-panel reveal" style="--i: 2">
				<header class="panel-head">
					<div class="panel-heading">
						<p class="eyebrow">The plan</p>
						<h2 class="panel-title">Objectives &amp; steps</h2>
						<p class="panel-subtitle">
							What is in play first, then parked work, then what is finished.
						</p>
					</div>
					<div class="panel-head-actions">
						<button
							type="button"
							class="btn btn-ghost btn-sm"
							onclick={openObjectiveModal}>+ Objective</button
						>
					</div>
				</header>

				{#if planObjectives.length === 0 && seedObjectives.length === 0}
					<div class="calm-state">
						<span class="calm-mark" aria-hidden="true">◇</span>
						<div>
							<h3>No objectives yet.</h3>
							<p>
								Add the first durable outcome, then break it into steps agents can
								pull.
							</p>
							<button
								type="button"
								class="btn btn-primary btn-sm"
								onclick={openObjectiveModal}>Add an objective</button
							>
						</div>
					</div>
				{:else}
					{#snippet objectiveRow(objective, index, movable)}
						{@const progress = stepProgress(objective.steps)}
						{@const isOpen = !!expanded[objective.id]}
						<article
							class="objective-node"
							class:is-backlog={objective.planState === 'Backlog'}
						>
							<div class="objective-node-line" aria-hidden="true">
								<span class="node-dot"></span>
							</div>

							<div class="objective-row">
								<div class="objective-main">
									<div class="objective-title-line">
										<span class="ref-chip large">#{objective.alias}</span>
										<a class="objective-title" href={objectiveHref(objective)}
											>{objective.title}</a
										>
									</div>
									<div class="objective-badges">
										<StatusBadge
											status={objective.planState === 'Ready'
												? objective.status || 'not-started'
												: objective.planState}
											sm
										/>
										{#if progress.flagged > 0}
											<span class="chip chip-flag"
												>{progress.flagged} flagged</span
											>
										{/if}
										{#if progress.blocked > 0}
											<span class="chip chip-blocked"
												>{progress.blocked} blocked</span
											>
										{/if}
										{#if objective.planState === 'Backlog'}
											<span class="calm-note"
												>Parked · not in the ready set</span
											>
										{/if}
									</div>
								</div>

								<div class="objective-actions">
									{#if movable}
										<button
											type="button"
											class="btn btn-ghost btn-icon btn-sm"
											onclick={() => moveObjective(objective, -1)}
											disabled={index === 0}
											title="Move up"
											aria-label="Move objective up">↑</button
										>
										<button
											type="button"
											class="btn btn-ghost btn-icon btn-sm"
											onclick={() => moveObjective(objective, 1)}
											disabled={index === inPlayObjectives.length - 1}
											title="Move down"
											aria-label="Move objective down">↓</button
										>
									{/if}
									<button
										type="button"
										class="btn btn-ghost btn-icon btn-sm"
										onclick={() => openObjectiveEdit(objective)}
										title="Edit objective"
										aria-label="Edit objective">✎</button
									>
									{#if objective.planState === 'Backlog'}
										<button
											type="button"
											class="btn btn-secondary btn-sm"
											onclick={() => setPlanState(objective, 'Ready')}
										>
											Release
										</button>
									{:else}
										<button
											type="button"
											class="btn btn-ghost btn-sm"
											onclick={() => setPlanState(objective, 'Backlog')}
										>
											Park
										</button>
									{/if}
								</div>
							</div>

							<div class="objective-progress-row">
								<div class="progress-track slim" aria-hidden="true">
									<span class="progress-fill" style="width: {progress.pct}%"
									></span>
								</div>
								<span class="progress-numbers">
									{progress.done}/{progress.total} steps
								</span>
								<button
									type="button"
									class="text-toggle"
									aria-expanded={isOpen}
									onclick={() => toggleExpanded(objective.id)}
								>
									{isOpen
										? 'Hide steps'
										: `Show ${progress.total} step${progress.total === 1 ? '' : 's'}`}
								</button>
								<a class="text-link objective-open" href={objectiveHref(objective)}
									>Open objective →</a
								>
							</div>

							{#if isOpen}
								<div class="step-cluster">
									{#if !objective.steps || objective.steps.length === 0}
										<p class="micro-empty">
											No steps yet. Open the objective to add the first atomic
											unit of work.
										</p>
									{:else}
										{#each objective.steps as step (step.id)}
											{@const latest = latestUpdate(step)}
											{@const deps = (step.dependsOn || []).filter(
												(dep) => dep.status !== 'done'
											)}
											<div
												class="step-line"
												class:is-flagged={step.status === 'flagged'}
											>
												<div class="step-line-top">
													<span class="step-ref"
														>#{objective.alias}.{step.stepNumber}</span
													>
													<a
														class="step-line-title"
														href={objectiveHref(objective)}
														>{step.title}</a
													>
													<StatusBadge status={step.status} sm />
													<span class="step-line-spacer"></span>
													<button
														type="button"
														class="btn btn-ghost btn-icon btn-xs"
														class:flag-on={step.status === 'flagged'}
														onclick={() => toggleFlag(step)}
														title={step.status === 'flagged'
															? 'Clear flag'
															: 'Flag for human attention'}
														aria-label={step.status === 'flagged'
															? 'Clear flag'
															: 'Flag step'}>!</button
													>
												</div>
												<div class="step-line-meta">
													{#if step.assignee}
														<span class="assignee"
															>@{actorLabel(step.assignee)}</span
														>
													{/if}
													{#if deps.length > 0}
														<span class="dep-inline">
															waits on
															{#each deps as dep (dep.stepId)}
																<span class="dep-chip is-pending"
																	>#{dep.objectiveAlias}.{dep.stepNumber}</span
																>
															{/each}
														</span>
													{:else if step.status === 'open' && objective.planState === 'Ready'}
														<span class="chip chip-ready">ready</span>
													{/if}
													{#if step.updatedAt}<span title={step.updatedAt}
															>updated {timeAgo(step.updatedAt)}</span
														>{/if}
												</div>
												{#if latest}
													<p class="step-line-latest">
														<StatusBadge
															status={latest.kind}
															sm
															tone={latest.kind === 'Blocker'
																? 'blocked'
																: latest.kind === 'Result'
																	? 'done'
																	: 'neutral'}
														/>
														<span>{plainText(latest.body, 150)}</span>
														<span class="latest-time"
															>{timeAgo(latest.createdAt)}</span
														>
													</p>
												{/if}
											</div>
										{/each}
									{/if}
								</div>
							{/if}
						</article>
					{/snippet}

					<section class="plan-tier">
						<header class="tier-head">
							<h3 class="tier-title">In play</h3>
							{#if inPlayObjectives.length > 0}
								<span class="tier-count">{inPlayObjectives.length}</span>
							{/if}
							<span class="tier-hint">Ready and in progress</span>
						</header>
						{#if inPlayObjectives.length === 0}
							<p class="tier-empty">
								Nothing in play. Shape a seed or release a backlog objective below,
								or add an objective.
							</p>
						{:else}
							<div class="plan-tree">
								{#each inPlayObjectives as objective, index (objective.id)}
									{@render objectiveRow(objective, index, true)}
								{/each}
							</div>
						{/if}
					</section>

					<section class="plan-tier parked-tier">
						<header class="tier-head">
							<h3 class="tier-title">Parked</h3>
							{#if seedObjectives.length + backlogObjectives.length > 0}
								<span class="tier-count"
									>{seedObjectives.length + backlogObjectives.length}</span
								>
							{/if}
							<span class="tier-hint">Backlog and seeds — not in the ready set</span>
						</header>

						{#if seedObjectives.length === 0 && backlogObjectives.length === 0}
							<p class="tier-empty">
								Nothing parked. A thought captured
								<button type="button" class="text-toggle" onclick={focusCapture}
									>above</button
								>
								waits here until it is shaped.
							</p>
						{/if}

						{#if seedObjectives.length > 0}
							<div class="plan-tree">
								{#each seedObjectives as seed (seed.id)}
									<article
										id={`seed-${seed.id}`}
										class="objective-node is-seed"
										class:is-new={highlightSeedId === seed.id}
									>
										<div class="objective-node-line" aria-hidden="true">
											<span class="node-dot"></span>
										</div>
										<div class="objective-row">
											<div class="objective-main">
												<div class="objective-title-line">
													<span class="ref-chip large">#{seed.alias}</span
													>
													<button
														type="button"
														class="objective-title is-link"
														onclick={(event) =>
															openSeedPreview(
																seed,
																event.currentTarget
															)}>{seed.title}</button
													>
												</div>
												<div class="objective-badges">
													<StatusBadge status="Seed" sm />
													<span class="row-meta"
														>updated {timeAgo(seed.updatedAt)}</span
													>
												</div>
												{#if seed.description}
													<p class="seed-excerpt">
														{plainText(seed.description, 140)}
													</p>
												{/if}
											</div>
											<div class="objective-actions">
												<button
													type="button"
													class="btn btn-secondary btn-sm"
													onclick={() => shapeSeed(seed)}
												>
													Shape
												</button>
												<button
													type="button"
													class="btn btn-ghost btn-sm"
													onclick={() => deleteSeed(seed)}
												>
													Delete
												</button>
											</div>
										</div>
									</article>
								{/each}
							</div>
						{/if}

						{#if backlogObjectives.length > 0}
							<div class="plan-tree">
								{#each backlogObjectives as objective, index (objective.id)}
									{@render objectiveRow(objective, index, false)}
								{/each}
							</div>
						{/if}
					</section>

					{#if finishedObjectives.length > 0}
						<section class="plan-tier finished-tier">
							<h3 class="sr-only">Completed</h3>
							<button
								type="button"
								class="tier-head tier-toggle"
								aria-expanded={finishedOpen}
								aria-controls="finished-list"
								onclick={() => (finishedOpen = !finishedOpen)}
							>
								<svg
									class="tier-caret"
									viewBox="0 0 16 16"
									width="14"
									height="14"
									aria-hidden="true"
									fill="none"
									stroke="currentColor"
									stroke-width="1.75"
									stroke-linecap="round"
									stroke-linejoin="round"
								>
									<path d="M6 3.5 10.5 8 6 12.5" />
								</svg>
								<span class="tier-title">Completed</span>
								<span class="tier-count">{finishedObjectives.length}</span>
							</button>
							{#if finishedOpen}
								<ul class="finished-list" id="finished-list">
									{#each finishedObjectives as objective (objective.id)}
										<li class="finished-row">
											<a
												class="finished-link"
												href={objectiveHref(objective)}
												aria-label={`#${objective.alias} ${objective.title}`}
												title={objective.title}
											>
												<span class="ref-chip">#{objective.alias}</span>
												<span class="finished-title">{objective.title}</span
												>
												<span class="finished-meta"
													>completed · last activity {timeAgo(
														objective.updatedAt
													)}</span
												>
												<span class="finished-arrow" aria-hidden="true"
													>→</span
												>
											</a>
										</li>
									{/each}
								</ul>
							{/if}
						</section>
					{/if}
				{/if}
			</section>
		</div>

		<aside class="campaign-rail">
			<section class="panel rail-panel reveal" style="--i: 3">
				<header class="panel-head compact">
					<div class="panel-heading">
						<p class="eyebrow">Knowledge</p>
						<h2 class="panel-title">Bunker</h2>
					</div>
					<a class="text-link" href="#/campaign/{campaignId}/bunker">Open →</a>
				</header>

				{#if railDirectives.length > 0}
					<div class="rail-zone rail-directives">
						<p class="zone-label">Directives</p>
						<ul class="rail-list">
							{#each railDirectives as note (note.id)}
								<li>
									<a class="rail-item" href={noteHref(note)}>
										<span class="rail-item-top">
											<span class="ref-chip">#{note.alias}</span>
											<StatusBadge status="directive" sm />
										</span>
										<span class="rail-item-title">{note.title}</span>
									</a>
								</li>
							{/each}
						</ul>
						{#if directiveCount > railDirectives.length}
							<a class="text-link rail-more" href="#/campaign/{campaignId}/bunker"
								>+{directiveCount - railDirectives.length} more</a
							>
						{/if}
					</div>
				{/if}

				<label class="search-field compact">
					<span class="search-glyph" aria-hidden="true">⌕</span>
					<span class="sr-only">Search Bunker notes</span>
					<input type="search" placeholder="Search notes…" bind:value={railSearch} />
				</label>

				<div class="rail-zone">
					<p class="zone-label">
						{railSearch ? 'Matches' : 'Recent notes'}
					</p>
					{#if railNotes.length === 0}
						<p class="micro-empty">
							{railSearch ? 'No notes match that search.' : 'The Bunker is empty.'}
						</p>
					{:else}
						<ul class="rail-list">
							{#each railNotes as note (note.id)}
								<li>
									<a class="rail-item" href={noteHref(note)}>
										<span class="rail-item-top">
											<span class="ref-chip">#{note.alias}</span>
											<StatusBadge status={note.kind} sm />
										</span>
										<span class="rail-item-title">{note.title}</span>
										<span class="rail-item-sub"
											>{timeAgo(note.updatedAt)} · {plainText(
												note.body,
												60
											)}</span
										>
									</a>
								</li>
							{/each}
						</ul>
					{/if}
				</div>

				<a class="btn btn-secondary btn-sm w-full" href="#/campaign/{campaignId}/bunker"
					>Search the Bunker</a
				>
			</section>
		</aside>
	</div>
{/if}

{#if editingCampaign}
	<div
		class="modal-overlay"
		role="presentation"
		onclick={(e) => {
			if (e.target === e.currentTarget) editingCampaign = false;
		}}
	>
		<div
			class="modal modal-wide"
			role="dialog"
			aria-modal="true"
			aria-labelledby="edit-campaign-title"
			tabindex="-1"
			onclick={(e) => e.stopPropagation()}
			onkeydown={(e) => {
				if (e.key === 'Escape') editingCampaign = false;
			}}
		>
			<form onsubmit={saveCampaign}>
				<div class="modal-header">
					<div>
						<p class="eyebrow">Campaign</p>
						<h2 id="edit-campaign-title">Edit campaign</h2>
					</div>
					<button
						type="button"
						class="modal-close"
						onclick={() => (editingCampaign = false)}
						aria-label="Close">×</button
					>
				</div>
				<div class="modal-body">
					<div class="form-group">
						<label class="form-label" for="edit-campaign-title-input">Title</label>
						<input
							id="edit-campaign-title-input"
							class="form-input"
							type="text"
							bind:value={campaignForm.title}
							required
						/>
					</div>
					<div class="form-group">
						<label class="form-label" for="edit-campaign-tags"
							>Tags <span class="optional">(comma separated)</span></label
						>
						<input
							id="edit-campaign-tags"
							class="form-input"
							type="text"
							bind:value={campaignForm.tags}
						/>
					</div>
				</div>
				<div class="modal-actions spread">
					<span class="modal-actions-group">
						<button type="button" class="btn btn-danger" onclick={deleteCampaign}
							>Delete campaign</button
						>
						<button
							type="button"
							class="btn btn-ghost"
							disabled={campaignSaving}
							onclick={() => setArchived(!campaign?.archivedAt)}
						>
							{campaign?.archivedAt ? 'Restore campaign' : 'Archive campaign'}
						</button>
					</span>
					<span class="modal-actions-group">
						<button
							type="button"
							class="btn btn-ghost"
							onclick={() => (editingCampaign = false)}>Cancel</button
						>
						<button type="submit" class="btn btn-primary" disabled={campaignSaving}>
							{campaignSaving ? 'Saving…' : 'Save campaign'}
						</button>
					</span>
				</div>
			</form>
		</div>
	</div>
{/if}

{#if editingCharter}
	<div
		class="modal-overlay"
		role="presentation"
		onclick={(e) => {
			if (e.target === e.currentTarget) editingCharter = false;
		}}
	>
		<div
			class="modal modal-wide"
			role="dialog"
			aria-modal="true"
			aria-labelledby="charter-editor-title"
			tabindex="-1"
			onclick={(e) => e.stopPropagation()}
			onkeydown={(e) => {
				if (e.key === 'Escape') editingCharter = false;
			}}
		>
			<form onsubmit={saveCharter}>
				<div class="modal-header">
					<div>
						<p class="eyebrow">
							{campaign?.charter ? 'Charter' : 'Campaign framing'}
						</p>
						<h2 id="charter-editor-title">
							{campaign?.charter ? 'Edit charter' : 'Write charter'}
						</h2>
						<p class="panel-subtitle">
							Why the campaign exists, what it covers, and how it is put together.
							Agents read this first.
						</p>
					</div>
					<button
						type="button"
						class="modal-close"
						onclick={() => (editingCharter = false)}
						aria-label="Close">×</button
					>
				</div>
				<div class="modal-body">
					<div class="form-group">
						<label class="form-label" for="charter-body"
							>Charter <span class="optional">(markdown)</span></label
						>
						<MarkdownEditor
							id="charter-body"
							bind:value={charterForm.body}
							{campaignId}
							placeholder="Why this campaign exists, what is in scope, and the architecture that holds it together."
							rows={8}
						/>
					</div>
				</div>
				<div class="modal-actions">
					<button
						type="button"
						class="btn btn-ghost"
						onclick={() => (editingCharter = false)}>Cancel</button
					>
					<button type="submit" class="btn btn-primary" disabled={charterSaving}>
						{charterSaving
							? 'Saving…'
							: campaign?.charter
								? 'Save charter'
								: 'Write charter'}
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}

{#if showObjectiveModal}
	<div
		class="modal-overlay"
		role="presentation"
		onclick={(e) => {
			if (e.target === e.currentTarget) showObjectiveModal = false;
		}}
	>
		<div
			class="modal modal-wide"
			role="dialog"
			aria-modal="true"
			aria-labelledby="new-objective-title"
			tabindex="-1"
			onclick={(e) => e.stopPropagation()}
			onkeydown={(e) => {
				if (e.key === 'Escape') showObjectiveModal = false;
			}}
		>
			<form onsubmit={createObjective}>
				<div class="modal-header">
					<div>
						<p class="eyebrow">New outcome</p>
						<h2 id="new-objective-title">Add an objective</h2>
					</div>
					<button
						type="button"
						class="modal-close"
						onclick={() => (showObjectiveModal = false)}
						aria-label="Close">×</button
					>
				</div>
				<div class="modal-body">
					<div class="form-group">
						<label class="form-label" for="new-objective-title-input">Title</label>
						<input
							id="new-objective-title-input"
							class="form-input"
							type="text"
							bind:value={objectiveForm.title}
							required
						/>
					</div>
					<div class="form-group">
						<label class="form-label" for="new-objective-description"
							>Description <span class="optional">(markdown)</span></label
						>
						<MarkdownEditor
							id="new-objective-description"
							bind:value={objectiveForm.description}
							{campaignId}
							rows={4}
						/>
					</div>
					<div class="form-group">
						<label class="form-label" for="new-objective-state">Plan state</label>
						<select
							id="new-objective-state"
							class="form-select"
							bind:value={objectiveForm.planState}
						>
							<option value="Backlog">Backlog — parked, not released</option>
							<option value="Ready">Ready — included in the ready set</option>
						</select>
					</div>
				</div>
				<div class="modal-actions">
					<button
						type="button"
						class="btn btn-ghost"
						onclick={() => (showObjectiveModal = false)}>Cancel</button
					>
					<button type="submit" class="btn btn-primary" disabled={objectiveSaving}>
						{objectiveSaving ? 'Creating…' : 'Create objective'}
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}

{#if editingObjective}
	<div
		class="modal-overlay"
		role="presentation"
		onclick={(e) => {
			if (e.target === e.currentTarget) editingObjective = null;
		}}
	>
		<div
			class="modal modal-wide"
			role="dialog"
			aria-modal="true"
			aria-labelledby="edit-objective-inline-title"
			tabindex="-1"
			onclick={(e) => e.stopPropagation()}
			onkeydown={(e) => {
				if (e.key === 'Escape') editingObjective = null;
			}}
		>
			<form onsubmit={saveObjectiveEdit}>
				<div class="modal-header">
					<div>
						<p class="eyebrow">#{editingObjective.alias}</p>
						<h2 id="edit-objective-inline-title">
							{shapingSeed ? 'Shape this seed' : 'Edit objective'}
						</h2>
						{#if shapingSeed}
							<p class="panel-subtitle">
								Saving moves it out of the seed pile and into your plan.
							</p>
						{/if}
					</div>
					<button
						type="button"
						class="modal-close"
						onclick={() => {
							editingObjective = null;
							shapingSeed = false;
						}}
						aria-label="Close">×</button
					>
				</div>
				<div class="modal-body">
					<div class="form-group">
						<label class="form-label" for="inline-objective-title">Title</label>
						<input
							id="inline-objective-title"
							class="form-input"
							type="text"
							bind:value={objectiveEditForm.title}
							required
						/>
					</div>
					<div class="form-group">
						<label class="form-label" for="inline-objective-description"
							>Description <span class="optional">(markdown)</span></label
						>
						<MarkdownEditor
							id="inline-objective-description"
							bind:value={objectiveEditForm.description}
							{campaignId}
							rows={5}
						/>
					</div>
					<div class="form-group">
						<label class="form-label" for="inline-objective-state">Plan state</label>
						<select
							id="inline-objective-state"
							class="form-select"
							bind:value={objectiveEditForm.planState}
						>
							<option value="Backlog">Backlog — parked, not released</option>
							<option value="Ready">Ready — included in the ready set</option>
						</select>
					</div>
				</div>
				<div class="modal-actions">
					<button
						type="button"
						class="btn btn-ghost"
						onclick={() => (editingObjective = null)}>Cancel</button
					>
					<button type="submit" class="btn btn-primary" disabled={objectiveEditSaving}>
						{objectiveEditSaving ? 'Saving…' : 'Save objective'}
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}

{#if previewSeed}
	<SeedPreview
		seed={previewSeed}
		{campaignId}
		onShape={shapeFromPreview}
		onDelete={deleteFromPreview}
		onClose={closeSeedPreview}
	/>
{/if}
