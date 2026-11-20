<script>
	import { diffWordsWithSpace } from 'diff';

	let { before = '', after = '', label = '', otherLabel = 'Theirs' } = $props();

	// Skip the diff algorithm for very long strings so a pasted log cannot
	// stall the tab. Fall back to plain text display.
	let diffParts = $derived.by(() => {
		const b = before ?? '';
		const a = after ?? '';
		if (b.length + a.length > 20000) return null;
		try {
			return diffWordsWithSpace(b, a);
		} catch {
			return null;
		}
	});

	let leftPane = $state(null);
	let rightPane = $state(null);
	let syncing = $state(false);

	function syncScroll(e, side) {
		if (syncing) return;
		syncing = true;
		const other = side === 'left' ? rightPane : leftPane;
		if (other) {
			other.scrollTop = e.target.scrollTop;
		}
		requestAnimationFrame(() => {
			syncing = false;
		});
	}
</script>

<div class="live-change-diff" role="region" aria-label={label ? `${label} changes` : 'Changes'}>
	<div class="live-change-diff-panes">
		<div
			class="live-change-diff-pane"
			aria-label="Yours"
			bind:this={leftPane}
			onscroll={(e) => syncScroll(e, 'left')}
		>
			<span class="live-change-diff-pane-label">Yours</span>
			<div class="live-change-diff-content">
				{#if diffParts}
					{#each diffParts as part}
						{#if !part.added}
							{#if part.removed}
								<del>{part.value}</del>
							{:else}
								{part.value}
							{/if}
						{/if}
					{/each}
				{:else}
					{before ?? ''}
				{/if}
			</div>
		</div>
		<div
			class="live-change-diff-pane"
			aria-label={otherLabel}
			bind:this={rightPane}
			onscroll={(e) => syncScroll(e, 'right')}
		>
			<span class="live-change-diff-pane-label">{otherLabel}</span>
			<div class="live-change-diff-content">
				{#if diffParts}
					{#each diffParts as part}
						{#if !part.removed}
							{#if part.added}
								<ins>{part.value}</ins>
							{:else}
								{part.value}
							{/if}
						{/if}
					{/each}
				{:else}
					{after ?? ''}
				{/if}
			</div>
		</div>
	</div>
</div>
