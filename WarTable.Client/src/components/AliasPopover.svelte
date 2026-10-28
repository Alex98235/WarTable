<script>
  import { aliasPopoverState, closeAliasPopover } from '../lib/aliasPopover.svelte.js';

  let popoverEl = $state(null);

  const popover = $derived(aliasPopoverState.popover);

  function kindBadge(kind) {
    if (kind === 'Intel') return 'context';
    if (kind === 'SitRep') return 'reference';
    return 'active';
  }

  function handleKeydown(e) {
    if (e.key === 'Escape') closeAliasPopover();
  }

  // The backdrop is not focusable, so listen on window while the popover is open.
  $effect(() => {
    if (!popover) return;
    window.addEventListener('keydown', handleKeydown);
    return () => window.removeEventListener('keydown', handleKeydown);
  });

  // Position the popover after measurement
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
    role="tooltip"
  >
    {#if popover.error}
      <span class="text-muted">#{popover.alias} not found</span>
    {:else if popover.loading}
      <span class="text-muted">Resolving…</span>
    {:else}
      <div class="alias-popover-header">
        <span class="alias-popover-alias">#{popover.alias}</span>
        <span class="badge badge-{kindBadge(popover.kind)} badge-sm"
          >{popover.kind || 'Object'}</span
        >
      </div>
      <div class="alias-popover-title">{popover.title}</div>
      {#if popover.lane}
        <div class="alias-popover-lane">
          <span class="badge badge-{popover.lane.toLowerCase()} badge-sm"
            >{popover.lane}</span
          >
        </div>
      {/if}
      {#if popover.deleted}
        <div class="alias-popover-deleted">(deleted)</div>
      {/if}
    {/if}
  </div>
{/if}

<style>
  .alias-popover-backdrop {
    position: fixed;
    inset: 0;
    z-index: 999;
  }
  .alias-popover {
    position: fixed;
    z-index: 1000;
    transform: translateX(-50%);
    background: var(--bg-card);
    border: 1px solid var(--border-color);
    border-radius: var(--radius-md);
    box-shadow: var(--shadow-lg);
    padding: 10px 14px;
    min-width: 140px;
    max-width: 260px;
    pointer-events: auto;
  }
  .alias-popover-header {
    display: flex;
    align-items: center;
    gap: 6px;
    margin-bottom: 4px;
  }
  .alias-popover-alias {
    font-weight: 700;
    font-size: 0.8rem;
    color: var(--text-secondary);
  }
  .alias-popover-title {
    font-size: 0.85rem;
    color: var(--text-primary);
    line-height: 1.3;
    word-break: break-word;
  }
  .alias-popover-lane {
    margin-top: 4px;
  }
  .alias-popover-deleted {
    font-size: 0.75rem;
    color: var(--color-danger);
    font-style: italic;
    margin-top: 2px;
  }
  .badge-sm {
    font-size: 0.65rem;
    padding: 2px 8px;
  }
</style>