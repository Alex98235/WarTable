<script>
	import { untrack } from 'svelte';
	import { actorLabel } from '../lib/api.js';
	import { liveEntity } from '../lib/events.svelte.js';
	import LiveChangeDiff from './LiveChangeDiff.svelte';

	/**
	 * Advisory banner for a form whose row changed elsewhere while it was open.
	 * Mounted only while the form is open, so the baseline is the row the human
	 * first saw. The 409 on save remains the correctness backstop; this only
	 * tells them sooner.
	 *
	 * The left side of each detail row shows the draft value (or the snapshot if
	 * the human has not touched that field), so the human sees "mine vs theirs"
	 * and can decide whether to Reload or Keep. All rows start collapsed behind
	 * one toggle, so a title change and a description change read the same until
	 * opened.
	 *
	 * Props:
	 *   id        entity GUID
	 *   type      entity type ("Objective", "Step", "Note")
	 *   snapshot  the row as it was when the form opened (liveEntity clones it)
	 *   read      () => current object from the caller's live state
	 *   readDraft (field) => current draft value for that field, or undefined
	 *   shownFields  fields whose live value the human has already been shown
	 *   onReload  discard the draft and show the current row
	 *   onKeep    optional extra work after rebaseline
	 */
	let {
		id,
		type = null,
		snapshot,
		read,
		readDraft = () => undefined,
		shownFields = [],
		onReload = () => {},
		onKeep = () => {},
		visible = $bindable(false)
	} = $props();

	// These props are constant for the component's lifetime: the banner is
	// mounted when the form opens and destroyed when it closes. The reactive
	// binding comes from `read`, which liveEntity calls inside a $derived.
	const live = untrack(() => liveEntity({ id, type, snapshot, read }));

	// Only fields a human can see in the form are worth naming. Everything else
	// (updates, evidence, linked work, provenance) is noise in a sentence about
	// this row.
	const FIELD_LABELS = {
		title: 'title',
		description: 'description',
		planState: 'plan state',
		context: 'context',
		assignee: 'assignee',
		assigner: 'assigner',
		dependsOn: 'dependencies',
		body: 'body',
		tags: 'tags',
		pinned: 'pinned',
		supersedesId: 'supersession',
		status: 'status'
	};

	function describe(change) {
		if (change.field === 'deletedAt') {
			return change.to ? 'archived' : 'restored';
		}
		if (change.field === 'steps' && change.kind === 'count') {
			const delta = change.to - change.from;
			const count = Math.abs(delta);
			return `${count} step${count === 1 ? '' : 's'} ${delta > 0 ? 'added' : 'removed'}`;
		}
		return FIELD_LABELS[change.field] ?? null;
	}

	// Reactive list of reported changes. Each entry carries the human-readable
	// label for the row eyebrow and the change object for the detail row.
	// A change is news unless the live value is the one the human was already
	// shown: that covers a field the form adopted silently, and it keeps covering
	// it after they start typing. Testing "has the human typed here" instead would
	// resurface an old change the moment they touch the field, as if the agent had
	// just written it.
	// Unseen changes (steps, status, archive) always report, because the form
	// cannot show them. When `steps` count is among the changes the derived
	// `status` entry is omitted so the sentence reads "2 steps added" rather
	// than "2 steps added, status".
	let reported = $derived.by(() => {
		const changes = live.changes;
		const hasSteps = changes.some(
			(change) => change.field === 'steps' && change.kind === 'count'
		);
		const out = [];
		for (const change of changes) {
			if (hasSteps && change.field === 'status') continue;
			if (shownFields.includes(change.field)) continue;
			const label = describe(change);
			if (label) out.push({ label, change });
		}
		return out;
	});

	// Archive and step-count changes have no row to open, so their wording stays
	// in the sentence after a colon. Every other change gets its own row.
	let sentenceNotes = $derived(
		reported
			.filter(({ change }) => change.field === 'deletedAt' || change.field === 'steps')
			.map(({ label }) => label)
	);

	let sentenceSuffix = $derived(
		sentenceNotes.length > 0 ? `: ${sentenceNotes.join(', ')}.` : '.'
	);

	// Fields that render a word-diff toggle instead of an inline value
	const LONG_FIELDS = new Set(['description', 'body', 'context']);

	// One toggle for the whole banner: every row opens together, so a title
	// change and a description change read the same until the human asks.
	let open = $state(false);

	// Sentence-only changes (archive, step count) have no row, so the toggle is
	// pointless when they are all that was reported.
	let hasRows = $derived(
		reported.some(({ change }) => change.field !== 'deletedAt' && change.field !== 'steps')
	);

	let actor = $derived(live.lastActor ? actorLabel(live.lastActor) : null);

	// Let the owning form disable its primary action while the divergence is
	// unresolved. Speculating over a known change is what the 409 was for; a
	// disabled button makes the choice explicit instead of failing after the fact.
	$effect(() => {
		visible = reported.length > 0;
	});

	function keep() {
		// Adopt the current row as the baseline: the banner goes away, the draft
		// stays, and the save that follows is a deliberate overwrite.
		live.rebaseline();
		onKeep();
	}

	function reload() {
		// Rebaseline before the parent replaces the draft, so the banner clears in
		// the same tick the text changes. Reload is not a close: the human asked to
		// see the other version, not to leave the form.
		live.rebaseline();
		onReload();
	}
</script>

{#if reported.length > 0}
	<div class="live-change-banner">
		<p class="live-change-banner-text" role="status" aria-live="polite">
			{#if actor}Changed elsewhere by <strong>{actor}</strong>{:else}Changed elsewhere{/if}{sentenceSuffix}
		</p>
		{#if hasRows}
			<button
				type="button"
				class="live-change-toggle"
				aria-expanded={open}
				onclick={() => (open = !open)}
			>
				{open ? 'Hide changes' : 'Show changes'}
			</button>
		{/if}
		{#if open}
			{#each reported as { label, change } (change.field)}
				{@const isSentenceOnly = change.field === 'deletedAt' || change.field === 'steps'}
				{@const mine = readDraft(change.field) ?? change.from}
				{@const theirs = change.to}
				{#if !isSentenceOnly}
					<div class="live-change-row">
						<span class="live-change-eyebrow">{label}</span>
						{#if LONG_FIELDS.has(change.field)}
							<LiveChangeDiff
								before={mine}
								after={theirs}
								{label}
								otherLabel={actor ?? 'Theirs'}
							/>
						{:else if change.kind === 'count'}
							{@const countLabel = change.field === 'tags' ? 'tags' : label}
							{@const countFrom = Array.isArray(mine) ? mine.length : change.from}
							<span
								class="live-change-inline"
								title="{countFrom} {countLabel} -> {theirs} {countLabel}"
							>
								{countFrom}
								{countLabel} &rarr; {theirs}
								{countLabel}
							</span>
						{:else}
							<span class="live-change-inline" title="{mine ?? ''} -> {theirs ?? ''}">
								<span class="live-change-old">{mine ?? ''}</span>
								<span class="live-change-arrow">&rarr;</span>
								<span class="live-change-new">{theirs ?? ''}</span>
							</span>
						{/if}
					</div>
				{/if}
			{/each}
		{/if}
		<div class="live-change-banner-actions">
			<button type="button" class="btn btn-primary btn-sm" onclick={reload}>Reload</button>
			<button type="button" class="btn btn-ghost btn-sm" onclick={keep}>Keep my text</button>
		</div>
	</div>
{/if}
