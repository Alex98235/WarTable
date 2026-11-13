<script>
	import { parseDate, timeAgo } from '../lib/api.js';
	import Markdown from './Markdown.svelte';

	/**
	 * Read-only preview of a seed. A seed is an objective with no steps, so this
	 * deliberately shows no plan chrome: no status, no progress, no step list.
	 * The note under the body says what a seed is, which is what keeps the modal
	 * from reading as an empty objective.
	 */
	let { seed, campaignId, onShape, onDelete, onClose } = $props();

	let dialogEl = $state(null);

	// Creation stamps CreatedAt and UpdatedAt with the same instant, so a seed
	// that has never been edited carries one timestamp in both fields.
	let edited = $derived.by(() => {
		const created = parseDate(seed?.createdAt);
		const updated = parseDate(seed?.updatedAt);
		if (!created || !updated) return false;
		return Math.abs(updated - created) > 60000;
	});

	$effect(() => {
		dialogEl?.focus();
	});
</script>

<div
	class="modal-overlay"
	role="presentation"
	onclick={(e) => {
		if (e.target === e.currentTarget) onClose();
	}}
>
	<div
		class="modal seed-preview"
		role="dialog"
		aria-modal="true"
		aria-labelledby="seed-preview-title"
		tabindex="-1"
		bind:this={dialogEl}
		onclick={(e) => e.stopPropagation()}
		onkeydown={(e) => {
			if (e.key === 'Escape') onClose();
		}}
	>
		<div class="modal-header">
			<div>
				<p class="eyebrow">Seed #{seed.alias}</p>
				<h2 id="seed-preview-title">{seed.title}</h2>
				<p class="seed-meta">
					Parked {timeAgo(seed.createdAt)}{#if edited}
						· last touched {timeAgo(seed.updatedAt)}{/if}
				</p>
			</div>
			<button type="button" class="modal-close" onclick={onClose} aria-label="Close">×</button
			>
		</div>

		<div class="modal-body seed-preview-body">
			{#if seed.description}
				<Markdown source={seed.description} {campaignId} />
			{:else}
				<p class="seed-empty">
					Nothing written down yet. Shape it when you know what it should become.
				</p>
			{/if}
			<p class="seed-note">
				A seed is a thought, not a plan. It is never worked. Shaping moves it into the plan
				as a backlog objective, where it waits to be released.
			</p>
		</div>

		<div class="modal-actions spread">
			<button type="button" class="btn btn-ghost btn-sm seed-delete" onclick={onDelete}
				>Delete seed</button
			>
			<span class="modal-actions-group">
				<button type="button" class="btn btn-ghost" onclick={onClose}>Close</button>
				<button type="button" class="btn btn-primary" onclick={onShape}
					>Shape into the plan</button
				>
			</span>
		</div>
	</div>
</div>
