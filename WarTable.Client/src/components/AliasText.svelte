<script>
   import { apiGet } from "../lib/api.js";

   /** Text that may contain #N alias references */
   let { text = "", campaignId = "" } = $props();

   let popover = $state(null); // { alias, kind, title, lane, deleted, x, anchorTop, anchorBottom } | null
   let popoverEl = $state(null);
   let resolving = $state(false);

   // Keep the popover (which is centred via translateX(-50%)) inside the viewport.
   const HALF_MAX_WIDTH = 140;
   function clampX(x) {
      if (typeof window === "undefined") return x;
      return Math.min(
         Math.max(x, HALF_MAX_WIDTH),
         window.innerWidth - HALF_MAX_WIDTH,
      );
   }

   // Parse #N patterns, skipping # that are part of longer numbers or at word boundaries
   // Matches # followed by digits, not preceded by a word character and not followed by more digits
   const ALIAS_RE = /(?<!\w)#(\d+)\b/g;

   function segments() {
      if (!text) return [{ t: "", alias: false }];
      const parts = [];
      let last = 0;
      let m;
      while ((m = ALIAS_RE.exec(text)) !== null) {
         if (m.index > last) {
            parts.push({ t: text.slice(last, m.index), alias: false });
         }
         parts.push({ t: m[0], alias: parseInt(m[1], 10) });
         last = m.index + m[0].length;
      }
      if (last < text.length) {
         parts.push({ t: text.slice(last), alias: false });
      }
      return parts;
   }

   let segs = $derived(segments());

   // Place the popover after it has been measured: below the reference when it fits,
   // otherwise flipped above it, otherwise pinned to the nearest viewport edge.
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

   async function resolveAlias(alias, event) {
      if (!campaignId) return;
      // Capture the anchor before awaiting — the event is pooled/currentTarget may be gone later.
      const rect = event.currentTarget.getBoundingClientRect();
      const x = clampX(rect.left + rect.width / 2);
      const anchorTop = rect.top;
      const anchorBottom = rect.bottom;
      resolving = true;
      popover = null;
      try {
         const data = await apiGet("/api/objects/resolve", {
            campaignId,
            alias,
         });
         popover = {
            alias,
            kind: data.kind,
            title: data.title,
            lane: data.lane,
            deleted: data.deleted,
            x,
            anchorTop,
            anchorBottom,
         };
      } catch (err) {
         // 404 / unresolved: still anchor to the reference, don't jump to the corner.
         popover = {
            alias,
            kind: "unknown",
            title: "Could not resolve",
            lane: null,
            deleted: false,
            x,
            anchorTop,
            anchorBottom,
            error: true,
         };
      } finally {
         resolving = false;
      }
   }

   function kindBadge(kind) {
      if (kind === "Intel") return "context";
      if (kind === "SitRep") return "reference";
      return "active";
   }

   function closePopover() {
      popover = null;
   }

   function handleKeydown(e, alias) {
      if (e.key === "Enter" || e.key === " ") {
         e.preventDefault();
         resolveAlias(alias, e);
      }
   }
</script>

<span class="alias-text">
   {#each segs as seg}
      {#if seg.alias}
         <span
            class="alias-ref"
            role="button"
            tabindex="0"
            onclick={(e) => resolveAlias(seg.alias, e)}
            onkeydown={(e) => handleKeydown(e, seg.alias)}
            onblur={() => closePopover()}
            aria-label="Resolve reference {seg.t}">{seg.t}</span
         >
      {:else}
         {seg.t}
      {/if}
   {/each}
</span>

{#if popover}
   <!-- svelte-ignore a11y_no_static_element_interactions -->
   <div
      class="alias-popover-backdrop"
      onclick={closePopover}
      onkeydown={(e) => {
         if (e.key === "Escape") closePopover();
      }}
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
      {:else}
         <div class="alias-popover-header">
            <span class="alias-popover-alias">#{popover.alias}</span>
            <span class="badge badge-{kindBadge(popover.kind)} badge-sm"
               >{popover.kind || "Object"}</span
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
   .alias-text {
      display: inline;
   }
   .alias-ref {
      color: var(--color-olive);
      font-weight: 600;
      cursor: pointer;
      border-bottom: 1px dotted var(--color-olive-light);
      transition: background 0.15s ease;
      padding: 0 2px;
      border-radius: 3px;
   }
   .alias-ref:hover {
      background: var(--color-olive-light);
      color: var(--color-olive-dark);
   }
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
