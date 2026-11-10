<script>
	import { plainText, actorLabel } from '../lib/api.js';

	/** @type {{ briefing: any, campaignId: string }} */
	let { briefing, campaignId } = $props();

	let open = $state(true);

	let flagged = $derived(briefing?.flaggedSteps ?? []);
	let ready = $derived(briefing?.readySteps ?? []);
	let blocked = $derived(briefing?.blockedSteps ?? []);
	let questions = $derived(briefing?.openQuestions ?? []);
	let directives = $derived(briefing?.directives ?? []);
	let decisions = $derived(briefing?.recentDecisions ?? []);
	let preferences = $derived(briefing?.preferences ?? []);

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

	function objectiveHref(alias) {
		return `#/campaign/${campaignId}/objective/${alias}`;
	}

	function noteHref(alias) {
		return `#/campaign/${campaignId}/bunker/${alias}`;
	}
</script>

<section class="panel briefing-panel">
	<header class="panel-head briefing-head">
		<div class="panel-heading">
			<p class="eyebrow">Briefing</p>
			<h2 class="panel-title">What matters now</h2>
			<p class="panel-subtitle">{lead}</p>
		</div>
		<div class="briefing-counts" aria-label="Briefing counts">
			{#if flagged.length > 0}<span class="count-chip tone-flag"
					>{flagged.length} flagged</span
				>{/if}
			{#if blocked.length > 0}<span class="count-chip tone-blocked"
					>{blocked.length} blocked</span
				>{/if}
			{#if questions.length > 0}<span class="count-chip tone-question"
					>{questions.length} question{questions.length === 1 ? '' : 's'}</span
				>{/if}
			{#if ready.length > 0}<span class="count-chip tone-ready">{ready.length} ready</span
				>{/if}
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
			{#if flagged.length > 0}
				<div class="briefing-flag-zone">
					<p class="zone-label">
						<span class="live-dot tone-flag"></span> Needs a human decision
					</p>
					<div class="brief-list">
						{#each flagged as step (step.id)}
							<a
								class="brief-row tone-flag"
								href={objectiveHref(step.objectiveAlias)}
							>
								<span class="ref-chip">{step.ref}</span>
								<span class="brief-row-title">{step.title}</span>
								<span class="brief-row-context">{step.objectiveTitle}</span>
							</a>
						{/each}
					</div>
				</div>
			{/if}

			<div class="briefing-grid">
				<div class="briefing-block">
					<p class="zone-label">
						<span class="live-dot tone-ready"></span> Ready to pull
					</p>
					{#if ready.length === 0}
						<p class="micro-empty">No unblocked open steps right now.</p>
					{:else}
						<div class="brief-list">
							{#each ready as step (step.id)}
								<a class="brief-row" href={objectiveHref(step.objectiveAlias)}>
									<span class="ref-chip">{step.ref}</span>
									<span class="brief-row-title">{step.title}</span>
									{#if step.assignee}
										<span class="assignee">@{actorLabel(step.assignee)}</span>
									{/if}
								</a>
							{/each}
						</div>
					{/if}
				</div>

				<div class="briefing-block">
					<p class="zone-label">
						<span class="live-dot tone-blocked"></span> Blockers &amp; questions
					</p>
					{#if blocked.length === 0 && questions.length === 0}
						<p class="micro-empty">No blockers or open questions.</p>
					{:else}
						<div class="brief-list">
							{#each blocked as step (step.id)}
								<a
									class="brief-row tone-blocked"
									href={objectiveHref(step.objectiveAlias)}
								>
									<span class="ref-chip">{step.ref}</span>
									<span class="brief-row-title">{step.title}</span>
								</a>
							{/each}
							{#each questions as note (note.id)}
								<a class="brief-row tone-question" href={noteHref(note.alias)}>
									<span class="ref-chip">#{note.alias}</span>
									<span class="brief-row-title">{note.title}</span>
								</a>
							{/each}
						</div>
					{/if}
				</div>

				<div class="briefing-block">
					<p class="zone-label">
						<span class="live-dot tone-directive"></span> Active directives
					</p>
					{#if directives.length === 0}
						<p class="micro-empty">No campaign directives yet.</p>
					{:else}
						<div class="brief-list">
							{#each directives as note (note.id)}
								<a class="brief-note" href={noteHref(note.alias)}>
									<span class="brief-note-head">
										<span class="ref-chip">#{note.alias}</span>
										<span class="brief-note-title">{note.title}</span>
										{#if note.pinned}<span
												class="pin-mark"
												title="Pinned directive">◆</span
											>{/if}
									</span>
									<span class="brief-note-body">{plainText(note.body, 130)}</span>
								</a>
							{/each}
						</div>
					{/if}
				</div>

				<div class="briefing-block">
					<p class="zone-label">
						<span class="live-dot tone-decision"></span> Recent decisions
					</p>
					{#if decisions.length === 0}
						<p class="micro-empty">No active decisions recorded.</p>
					{:else}
						<div class="brief-list">
							{#each decisions as note (note.id)}
								<a class="brief-note" href={noteHref(note.alias)}>
									<span class="brief-note-head">
										<span class="ref-chip">#{note.alias}</span>
										<span class="brief-note-title">{note.title}</span>
									</span>
									<span class="brief-note-body">{plainText(note.body, 110)}</span>
								</a>
							{/each}
						</div>
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
