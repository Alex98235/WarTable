<script>
  import { aliasPopoverState, closeAliasPopover } from '../lib/aliasPopover.svelte.js';
  import StatusBadge from './StatusBadge.svelte';

  let popoverEl = $state(null);

  const popover = $derived(aliasPopoverState.popover);

  function kindLabel(kind) {
    if (kind === 'Objective') return 'Objective';
    if (kind === 'Note') return 'Bunker note';
    return kind || 'Object';
  }

  function handleKeydown(e) {
    if (e.key === 'Escape') closeAliasPopover();
  }

  function openTarget() {
    const p = popover;
    if (!p) return;
    if (p.kind === 'Objective') {
      window.location.hash = `/campaign/${p.campaignId}/objective/${p.alias}`;
    } else if (p.kind === 'Note') {
      window.location.hash = `/campaign/${p.campaignId}/bunker/${p.alias}`;
    }
    closeAliasPopover();
  }

  $effect(() => {
    if (!popover) return;
    window.addEventListener('keydown', handleKeydown);
    return () => window.removeEventListener('keydown', handleKeydown);
  });

  // Position the popover after measurement.
  $effect(() => {
    const el = popoverEl;
    const p = popover;
    if (!el || !p) return;

    const MARGIN = 8;
    const GAP = 6;
    const height = el.offsetHeight;
    const viewportBottom = window.innerHeight - MARGIN;

    let top = p.anchorBottom + GAP;
    if (top + height > viewportBottom) {
      const flipped = p.anchorTop - height - GAP;
      top = flipped >= MARGIN ? flipped : Math.max(MARGIN, viewportBottom - height);
    }
    el.style.top = `${top}px`;
  });
</script>

{#if popover}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div
    class="alias-popover-backdrop"
    onclick={closeAliasPopover}
    onkeydown={handleKeydown}
    role="presentation"
  ></div>
  <div
    class="alias-popover"
    bind:this={popoverEl}
    style="left: {popover.x}px; top: {popover.anchorBottom + 6}px;"
    role="dialog"
    aria-label="Reference {popover.alias}"
  >
    {#if popover.loading}
      <span class="text-muted">Resolving…</span>
    {:else if popover.error}
      <span class="text-muted">#{popover.alias} does not resolve</span>
    {:else}
      <div class="alias-popover-header">
        <span class="alias-popover-alias">#{popover.alias}</span>
        <span class="badge badge-neutral badge-sm">{kindLabel(popover.kind)}</span>
        {#if popover.status}<StatusBadge status={popover.status} sm />{/if}
      </div>
      <p class="alias-popover-title">{popover.title}</p>
      {#if popover.deleted}
        <p class="alias-popover-deleted">Soft-deleted; kept resolvable for history.</p>
      {:else if popover.kind === 'Objective' || popover.kind === 'Note'}
        <button type="button" class="alias-popover-open" onclick={openTarget}>
          Open {popover.kind === 'Note' ? 'note' : 'objective'} →
        </button>
      {/if}
    {/if}
  </div>
{/if}
