<script>
  import { timeAgo, formatDate } from '../lib/api.js';

  /** @type {{ item: any }} */
  let { item } = $props();

  const metaByKind = {
    FlaggedStep: { label: 'Flagged step', mark: '!', tone: 'flag' },
    BlockedObjective: { label: 'Blocked objective', mark: '×', tone: 'blocked' },
    OpenQuestion: { label: 'Open question', mark: '?', tone: 'question' },
    StaleStep: { label: 'Stale work', mark: '◷', tone: 'stale' },
    DraftObjective: { label: 'Parked draft', mark: '◇', tone: 'draft' },
    SeedObjective: { label: 'Seed · needs shaping', mark: '◌', tone: 'seed' },
  };

  let meta = $derived(metaByKind[item.kind] || { label: item.kind, mark: '·', tone: 'neutral' });

  let ref = $derived(
    item.stepNumber != null && item.objectiveAlias != null
      ? `#${item.objectiveAlias}.${item.stepNumber}`
      : item.objectiveAlias != null
        ? `#${item.objectiveAlias}`
        : item.noteAlias != null
          ? `#${item.noteAlias}`
          : '',
  );

  let href = $derived(
    item.kind === 'OpenQuestion'
      ? `#/campaign/${item.campaignId}/bunker/${item.noteAlias}`
      : item.objectiveAlias != null
        ? `#/campaign/${item.campaignId}/objective/${item.objectiveAlias}`
        : `#/campaign/${item.campaignId}`,
  );
</script>

<a class="attention-item tone-{meta.tone}" {href} title={item.detail}>
  <span class="attention-mark" aria-hidden="true">{meta.mark}</span>
  <span class="attention-content">
    <span class="attention-top">
      <span class="attention-kind">{meta.label}</span>
      {#if ref}<span class="ref-chip">{ref}</span>{/if}
      <span class="attention-age" title={formatDate(item.since)}>{timeAgo(item.since)}</span>
    </span>
    <span class="attention-title">{item.title}</span>
    <span class="attention-context">
      <span class="attention-campaign">{item.campaignTitle}</span>
      {#if item.detail}<span class="attention-detail"> · {item.detail}</span>{/if}
    </span>
  </span>
</a>
