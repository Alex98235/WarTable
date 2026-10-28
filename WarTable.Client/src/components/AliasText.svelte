<script>
   import { openAliasPopover } from "../lib/aliasPopover.svelte.js";

   /** Text that may contain #N alias references */
   let { text = "", campaignId = "" } = $props();

   // Parse #N patterns, skipping # that are part of longer numbers or at word boundaries
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

   function handleAliasClick(e, alias) {
      const rect = e.currentTarget.getBoundingClientRect();
      openAliasPopover(alias, rect, campaignId);
   }

   function handleKeydown(e, alias) {
      if (e.key === "Enter" || e.key === " ") {
         e.preventDefault();
         const rect = e.currentTarget.getBoundingClientRect();
         openAliasPopover(alias, rect, campaignId);
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
            onclick={(e) => handleAliasClick(e, seg.alias)}
            onkeydown={(e) => handleKeydown(e, seg.alias)}
            aria-label="Resolve reference {seg.t}">{seg.t}</span
         >
      {:else}
         {seg.t}
      {/if}
   {/each}
</span>

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
</style>
