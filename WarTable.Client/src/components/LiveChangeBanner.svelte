<script>
	import { untrack } from 'svelte';
	import { actorLabel } from '../lib/api.js';
	import { liveEntity } from '../lib/events.svelte.js';

	/**
	 * Advisory banner for a form whose row changed elsewhere while it was open.
	 * Mounted only while the form is open, so the baseline is the row the human
	 * first saw. The 409 on save remains the correctness backstop; this only
	 * tells them sooner.
	 *
	 * Props:
	 *   id        entity GUID
	 *   type      entity type ("Objective", "Step", "Note")
	 *   snapshot  the row as it was when the form opened (liveEntity clones it)
	 *   read      () => current object from the caller's live state
	 *   shownFields  fields whose live value the human has already been shown
	 *   onReload  discard the draft and show the current row
	 *   onKeep    optional extra work after rebaseline
	 */
	let {
		id,
		type = null,
		snapshot,
		read,
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

	// Reactive list of human-readable field labels. A change is news unless the
	// live value is the one the human was already shown: that covers a field the
	// form adopted silently, and it keeps covering it after they start typing.
	// Testing "has the human typed here" instead would resurface an old change
	// the moment they touch the field, as if the agent had just written it.
	// Unseen changes (steps, status, archive) always report, because the form
	// cannot show them. When `steps` count is among the changes the derived
	// `status` entry is omitted so the sentence reads "2 steps added" rather
	// than "2 steps added, status".
	let parts = $derived.by(() => {
		const changes = live.changes;
		const hasSteps = changes.some(
			(change) => change.field === 'steps' && change.kind === 'count'
		);
		const out = [];
		for (const change of changes) {
			if (hasSteps && change.field === 'status') continue;
			if (shownFields.includes(change.field)) continue;
			const label = describe(change);
			if (label) out.push(label);
		}
		return out;
	});

	let actor = $derived(live.lastActor ? actorLabel(live.lastActor) : null);

	// Let the owning form disable its primary action while the divergence is
	// unresolved. Speculating over a known change is what the 409 was for; a
	// disabled button makes the choice explicit instead of failing after the fact.
	$effect(() => {
		visible = parts.length > 0;
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

{#if parts.length > 0}
	<div class="live-change-banner">
		<p class="live-change-banner-text" role="status" aria-live="polite">
			{#if actor}Changed elsewhere by <strong>{actor}</strong>{:else}Changed elsewhere{/if}: {parts.join(
				', '
			)}.
		</p>
		<div class="live-change-banner-actions">
			<button type="button" class="btn btn-primary btn-sm" onclick={reload}>Reload</button>
			<button type="button" class="btn btn-ghost btn-sm" onclick={keep}>Keep my text</button>
		</div>
	</div>
{/if}
