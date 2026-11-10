<script>
	import { actorLabel, plainText } from '../lib/api.js';

	/** @type {{ briefing: any, campaignId: string }} */
	let { briefing, campaignId } = $props();

	// Hard cap per group. The lead line and the +N line carry the rest.
	const ROW_CAP = 2;

	let open = $state(true);

	let flagged = $derived(briefing?.flaggedSteps ?? []);
	let ready = $derived(briefing?.readySteps ?? []);
	let blocked = $derived(briefing?.blockedSteps ?? []);
	let questions = $derived(briefing?.openQuestions ?? []);
	let directives = $derived(briefing?.directives ?? []);
	let decisions = $derived(briefing?.recentDecisions ?? []);
	let preferences = $derived(briefing?.preferences ?? []);

	let needsRows = $derived([
		...flagged.map((step) => ({
			key: `flag-${step.id}`,
			ref: step.ref,
			title: step.title,
			context: step.objectiveTitle || '',
			tone: 'flag',
			href: objectiveHref(step.objectiveAlias)
		})),
		...blocked.map((step) => ({
			key: `block-${step.id}`,
			ref: step.ref,
			title: step.title,
			context: step.objectiveTitle || '',
			tone: 'blocked',
			href: objectiveHref(step.objectiveAlias)
		}))
	]);

	let urgentCount = $derived(flagged.length + blocked.length + questions.length);
	let calm = $derived(urgentCount === 0 && ready.length === 0);

	let lead = $derived(
		flagged.length > 0
			? `${flagged.length} flagged step${flagged.length === 1 ? '' : 's'} need you`
			: blocked.length > 0
				? `${blocked.length} step${blocked.length === 1 ? '' : 's'} blocked`
				: questions.length > 0
					? `${questions.length} open question${questions.length === 1 ? '' : 's'}`
					: ready.length > 0
						? `${ready.length} step${ready.length === 1 ? '' : 's'} ready to pull`
						: 'Nothing is demanding attention'
	);

	// Directives and decisions stay available, but as a single line of reference
	// chips instead of two full blocks.
	let contextChips = $derived([
		...directives.slice(0, 2).map((note) => ({
			key: `directive-${note.id}`,
			label: `#${note.alias} ${note.title}`,
			pinned: !!note.pinned,
			decision: false,
			title: plainText(note.body, 160),
			href: noteHref(note)
		})),
		...decisions.slice(0, 2).map((note) => ({
			key: `decision-${note.id}`,
			label: `#${note.alias} ${note.title}`,
			pinned: false,
			decision: true,
			title: plainText(note.body, 160),
			href: noteHref(note)
		}))
	]);

	let hiddenContext = $derived(
		Math.max(0, directives.length + decisions.length - contextChips.length)
	);

	function objectiveHref(alias) {
		return `#/campaign/${campaignId}/objective/${alias}`;
	}

	function noteHref(note) {
		return `#/campaign/${campaignId}/bunker/${note.alias}`;
	}
</script>

<section class="panel briefing-panel">
	<header class="panel-head briefing-head">
		<div class="panel-heading">
			<p class="eyebrow">Briefing</p>
			<h2 class="panel-title">What matters now</h2>
			<p class="panel-subtitle">{lead}</p>
		</div>
		<button
			type="button"
			class="btn btn-ghost btn-sm"
			onclick={() => (open = !open)}
			aria-expanded={open}>{open ? 'Collapse' : 'Expand'}</button
		>
	</header>

	{#if open}
		{#if calm}
			<p class="briefing-calm">
				The plan is quiet. No flagged work, no blockers, and nothing waiting to be pulled.
			</p>
		{:else}
			<div class="brief-grid">
				<div class="brief-group">
					<div class="brief-group-head">
						<p class="zone-label">
							<span class="live-dot tone-flag"></span> Needs you
						</p>
						{#if needsRows.length > 0}
							<span class="brief-group-count">{needsRows.length}</span>
						{/if}
					</div>
					{#if needsRows.length === 0}
						<p class="micro-empty">Nothing flagged or blocked.</p>
					{:else}
						<div class="brief-list">
							{#each needsRows.slice(0, ROW_CAP) as row (row.key)}
								<a
									class="brief-row"
									class:tone-flag={row.tone === 'flag'}
									class:tone-blocked={row.tone === 'blocked'}
									href={row.href}
								>
									<span class="ref-chip">{row.ref}</span>
									<span class="brief-row-title">{row.title}</span>
									{#if row.context}
										<span class="brief-row-context">{row.context}</span>
									{/if}
								</a>
							{/each}
						</div>
						{#if needsRows.length > ROW_CAP}
							<a class="brief-more" href="#/attention"
								>+{needsRows.length - ROW_CAP} more in Attention</a
							>
						{/if}
					{/if}
				</div>

				<div class="brief-group">
					<div class="brief-group-head">
						<p class="zone-label"><span class="live-dot tone-ready"></span> Ready</p>
						{#if ready.length > 0}
							<span class="brief-group-count">{ready.length}</span>
						{/if}
					</div>
					{#if ready.length === 0}
						<p class="micro-empty">No unblocked open steps right now.</p>
					{:else}
						<div class="brief-list">
							{#each ready.slice(0, ROW_CAP) as step (step.id)}
								<a class="brief-row" href={objectiveHref(step.objectiveAlias)}>
									<span class="ref-chip">{step.ref}</span>
									<span class="brief-row-title">{step.title}</span>
									{#if step.assignee}
										<span class="assignee">@{actorLabel(step.assignee)}</span>
									{/if}
								</a>
							{/each}
						</div>
						{#if ready.length > ROW_CAP}
							<span class="brief-more is-quiet"
								>+{ready.length - ROW_CAP} more ready in the plan</span
							>
						{/if}
					{/if}
				</div>

				<div class="brief-group">
					<div class="brief-group-head">
						<p class="zone-label">
							<span class="live-dot tone-question"></span> Questions
						</p>
						{#if questions.length > 0}
							<span class="brief-group-count">{questions.length}</span>
						{/if}
					</div>
					{#if questions.length === 0}
						<p class="micro-empty">No open questions.</p>
					{:else}
						<div class="brief-list">
							{#each questions.slice(0, ROW_CAP) as note (note.id)}
								<a class="brief-row tone-question" href={noteHref(note)}>
									<span class="ref-chip">#{note.alias}</span>
									<span class="brief-row-title">{note.title}</span>
								</a>
							{/each}
						</div>
						{#if questions.length > ROW_CAP}
							<a class="brief-more" href="#/attention"
								>+{questions.length - ROW_CAP} more in Attention</a
							>
						{/if}
					{/if}
				</div>
			</div>
		{/if}

		{#if contextChips.length > 0}
			<div class="brief-context">
				<p class="zone-label">Context</p>
				<div class="brief-chips">
					{#each contextChips as chip (chip.key)}
						<a
							class="brief-chip"
							class:is-decision={chip.decision}
							href={chip.href}
							title={chip.title}
						>
							{#if chip.pinned}<span class="pin-mark" aria-hidden="true">◆</span>{/if}
							<span>{chip.label}</span>
						</a>
					{/each}
					{#if hiddenContext > 0}
						<a class="brief-chip is-more" href="#/campaign/{campaignId}/bunker"
							>+{hiddenContext} more in the Bunker</a
						>
					{/if}
				</div>
			</div>
		{/if}

		{#if preferences.length > 0}
			<div class="briefing-prefs">
				<p class="zone-label">Your preferences, always in play</p>
				<div class="pref-strip">
					{#each preferences.slice(0, 4) as pref (pref.id)}
						<a
							class="pref-chip"
							href="#/preferences"
							title={pref.rationale || pref.text}
						>
							<span class="pref-mark">✦</span>{pref.text}
						</a>
					{/each}
					{#if preferences.length > 4}
						<a class="pref-chip pref-more" href="#/preferences"
							>+{preferences.length - 4} more</a
						>
					{/if}
				</div>
			</div>
		{/if}
	{/if}
</section>
