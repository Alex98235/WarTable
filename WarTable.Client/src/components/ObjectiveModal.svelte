<script>
	import { untrack } from 'svelte';
	import { apiGet, timeAgo } from '../lib/api.js';
	import { confirmState } from '../lib/stores.js';
	import Markdown from './Markdown.svelte';
	import MarkdownEditor from './MarkdownEditor.svelte';

	/**
	 * The one surface for the two rows that have no page of their own: a live seed
	 * and an archived objective. Both are objectives, so both get the same body.
	 * Only the seed is editable — an archived row cannot be written to, and an
	 * edit that works only on a row you cannot otherwise see is a trap.
	 *
	 * Props:
	 *   objective  - the row (id, alias, title, description, updatedAt, deletedAt)
	 *   mode       - 'seed' | 'archived'
	 *   onSave     - seed only; receives { title, description }, resolves when done
	 *   onPromote / onArchive / onRestore / onPurge - the parent's existing handlers
	 */
	let {
		objective,
		campaignId = '',
		mode = 'seed',
		onClose,
		onSave = null,
		onPromote = null,
		onArchive = null,
		onRestore = null,
		onPurge = null
	} = $props();

	let dialogEl = $state(null);
	// The form starts from the row as it was when the modal opened; the row is
	// the source of truth for "unchanged", not a live subscription.
	let title = $state(untrack(() => objective.title ?? ''));
	let description = $state(untrack(() => objective.description ?? ''));
	let editingBody = $state(false);
	let saving = $state(false);
	let stepCount = $state(null);

	let isSeed = $derived(mode === 'seed');

	let dirty = $derived(
		isSeed &&
			(title.trim() !== (objective.title || '') ||
				description !== (objective.description || ''))
	);

	let meta = $derived(
		isSeed
			? `#${objective.alias} · Seed · updated ${timeAgo(objective.updatedAt)}`
			: `#${objective.alias} · Archived ${timeAgo(objective.deletedAt)}`
	);

	$effect(() => {
		dialogEl?.focus();
	});

	// The page behind the overlay must not scroll under it.
	$effect(() => {
		const previous = document.body.style.overflow;
		document.body.style.overflow = 'hidden';
		return () => {
			document.body.style.overflow = previous;
		};
	});

	// Steps are out of scope beyond their count, and the count is only worth a
	// line when there is something to count.
	$effect(() => {
		let cancelled = false;
		apiGet('/api/steps', { objectiveId: objective.id, includeArchived: 'true' })
			.then((rows) => {
				if (!cancelled) stepCount = Array.isArray(rows) ? rows.length : 0;
			})
			.catch(() => {
				if (!cancelled) stepCount = null;
			});
		return () => {
			cancelled = true;
		};
	});

	async function save() {
		if (!dirty || saving) return;
		saving = true;
		try {
			await onSave?.({ title: title.trim(), description });
		} finally {
			saving = false;
		}
	}
</script>

<div
	class="modal-overlay"
	role="presentation"
	onclick={(e) => {
		if (e.target === e.currentTarget) onClose();
	}}
>
	<div
		class="modal objective-modal"
		role="dialog"
		aria-modal="true"
		aria-labelledby="objective-modal-title"
		tabindex="-1"
		bind:this={dialogEl}
		onclick={(e) => e.stopPropagation()}
		onkeydown={(e) => {
			// A confirm renders above this dialog at the app root. While one is
			// open, Escape is its key: closing this surface behind it would leave
			// the human looking at a dialog for a row that is already gone.
			if (e.key === 'Escape' && !$confirmState) onClose();
		}}
	>
		<div class="modal-header">
			<div class="objective-modal-heading">
				<p class="eyebrow">{meta}</p>
				{#if isSeed}
					<h2 id="objective-modal-title" class="sr-only">{objective.title}</h2>
					<label class="sr-only" for="objective-modal-title-input">Title</label>
					<input
						id="objective-modal-title-input"
						class="form-input objective-modal-title"
						type="text"
						bind:value={title}
					/>
				{:else}
					<h2 id="objective-modal-title" class="objective-modal-title">
						{objective.title}
					</h2>
				{/if}
			</div>
			<button type="button" class="modal-close" onclick={onClose} aria-label="Close">×</button
			>
		</div>

		<div class="modal-body objective-modal-body">
			{#if isSeed && editingBody}
				<label class="sr-only" for="objective-modal-description">Description</label>
				<MarkdownEditor
					id="objective-modal-description"
					bind:value={description}
					{campaignId}
					rows={6}
				/>
			{:else if description}
				<div class="markdown-wrap">
					<Markdown source={description} {campaignId} />
				</div>
			{:else}
				<p class="inline-empty">No description yet.</p>
			{/if}

			{#if isSeed}
				<button
					type="button"
					class="text-toggle objective-modal-edit"
					onclick={() => (editingBody = !editingBody)}
				>
					{editingBody
						? 'Done editing'
						: description
							? 'Edit description'
							: 'Add a description'}
				</button>
			{/if}

			{#if stepCount > 0}
				<p class="objective-modal-steps">Steps {stepCount}</p>
			{/if}
		</div>

		<div class="modal-actions spread">
			{#if isSeed}
				<button
					type="button"
					class="btn btn-ghost danger-hover"
					onclick={() => onArchive?.()}>Archive</button
				>
				<span class="modal-actions-group">
					<button type="button" class="btn btn-ghost" onclick={onClose}>Cancel</button>
					<button type="button" class="btn btn-secondary" onclick={() => onPromote?.()}
						>Promote</button
					>
					<button
						type="button"
						class="btn btn-primary"
						onclick={save}
						disabled={!dirty || saving}>{saving ? 'Saving…' : 'Save'}</button
					>
				</span>
			{:else}
				<button type="button" class="btn btn-danger" onclick={() => onPurge?.()}
					>Delete permanently</button
				>
				<span class="modal-actions-group">
					<button type="button" class="btn btn-ghost" onclick={onClose}>Close</button>
					<button type="button" class="btn btn-primary" onclick={() => onRestore?.()}
						>Restore</button
					>
				</span>
			{/if}
		</div>
	</div>
</div>
