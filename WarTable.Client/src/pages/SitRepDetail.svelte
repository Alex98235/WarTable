<script>
  import { apiGet, timeAgo } from '../lib/api.js';
  import { addToast } from '../lib/stores.js';
  import Markdown from '../components/Markdown.svelte';

  let { route } = $props();
  let campaignId = $derived(route.params.id);
  let alias = $derived(parseInt(route.params.alias, 10));

  let campaign = $state(null);
  let loading = $state(true);
  let sitrep = $derived(
    (campaign?.sitReps ?? campaign?.sitreps ?? []).find((s) => s.alias === alias) ??
      null,
  );

  $effect(() => {
    if (!campaignId) return;
    loadCampaign();
  });

  async function loadCampaign() {
    loading = true;
    try {
      const data = await apiGet(`/api/campaigns/${campaignId}`);
      campaign = data;
    } catch (err) {
      addToast(err.message || 'Failed to load sitrep.', 'error');
      campaign = null;
    } finally {
      loading = false;
    }
  }

  function goTo(path) {
    window.location.hash = path;
  }

  const GROUP_KINDS = ['Completed', 'Blocked', 'Next'];

  function groupRefs(kind) {
    if (!sitrep?.objectives) return [];
    return sitrep.objectives.filter((o) => o.kind === kind);
  }
</script>

{#if loading}
  <div class="loading">Loading sitrep…</div>
{:else if campaign && sitrep}
  <div class="detail-page">
    <a
      href="#/campaign/{campaignId}"
      class="btn btn-ghost btn-sm detail-back-link"
      onclick={(e) => { e.preventDefault(); goTo(`/campaign/${campaignId}`); }}
    >← Back to campaign</a>

    <div class="detail-header card card-padding">
      <div class="detail-header-top">
        <div class="detail-title-group">
          <span class="detail-alias-chip">#{sitrep.alias}</span>
          <h1 class="detail-title">SitRep</h1>
        </div>
      </div>
      <div class="detail-badges">
        <span class="tag-badge">SitRep</span>
        {#if sitrep.source}
          <span class="badge {sitrep.source === 'agent' ? 'badge-reference' : 'badge-active'}">{sitrep.source}</span>
        {/if}
      </div>
      <div class="detail-meta">
        {#if sitrep.createdAt}
          <span class="text-muted text-sm">Created {timeAgo(sitrep.createdAt)}</span>
        {/if}
        {#if sitrep.updatedAt}
          <span class="text-muted text-sm"> · Updated {timeAgo(sitrep.updatedAt)}</span>
        {/if}
      </div>
    </div>

    {#if sitrep.summary}
      <div class="detail-section detail-section-grow card card-padding">
        <h2 class="detail-section-title">Summary</h2>
        <Markdown source={sitrep.summary} {campaignId} />
      </div>
    {/if}

    {#if sitrep.objectives?.length}
      <div class="detail-section card card-padding">
        <h2 class="detail-section-title">Objective Movements</h2>
        <div class="detail-ref-groups">
          {#each GROUP_KINDS as kind}
            {@const refs = groupRefs(kind)}
            {#if refs.length > 0}
              <div class="detail-ref-group detail-ref-{kind.toLowerCase()}">
                <span class="detail-ref-group-label">{kind}</span>
                <div class="detail-ref-chips">
                  {#each refs as ref}
                    <a
                      href="#/campaign/{campaignId}/objective/{ref.objectiveAlias}"
                      class="obj-ref-chip detail-obj-link"
                      onclick={(e) => { e.preventDefault(); goTo(`/campaign/${campaignId}/objective/${ref.objectiveAlias}`); }}
                    >#{ref.objectiveAlias} {ref.titleSnapshot}{#if ref.laneAtWrite} <span class="text-muted">({ref.laneAtWrite})</span>{/if}</a>
                  {/each}
                </div>
              </div>
            {/if}
          {/each}
        </div>
      </div>
    {/if}
  </div>
{:else}
  <div class="empty-state">
    <h3>SitRep not found</h3>
    <p>This sitrep doesn't exist or may have been deleted.</p>
    <a
      href="#/campaign/{campaignId}"
      class="btn btn-primary"
      onclick={(e) => { e.preventDefault(); goTo(`/campaign/${campaignId}`); }}
    >Back to campaign</a>
  </div>
{/if}

<style>
  .detail-page {
    display: flex;
    flex-direction: column;
    gap: 24px;
    min-height: calc(100vh - 160px);
  }
  .detail-back-link {
    align-self: flex-start;
    margin-bottom: 4px;
  }
  .detail-header-top {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 16px;
    flex-wrap: wrap;
  }
  .detail-title-group {
    display: flex;
    align-items: center;
    gap: 10px;
    flex-wrap: wrap;
    flex: 1 1 auto;
    min-width: 0;
  }
  .detail-alias-chip {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    padding: 3px 10px;
    border-radius: 100px;
    font-size: 0.75rem;
    font-weight: 700;
    background: var(--color-olive-dark);
    color: var(--color-olive-light);
    white-space: nowrap;
  }
  .detail-title {
    font-size: 1.625rem;
    font-weight: 700;
    letter-spacing: -0.02em;
    margin: 0;
    line-height: 1.25;
  }
  .detail-badges {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 8px;
    margin-top: 14px;
  }
  .detail-badges .badge,
  .detail-badges .tag-badge {
    font-size: 0.6875rem;
    padding: 2px 10px;
    font-weight: 500;
    letter-spacing: 0.02em;
  }
  .detail-meta {
    display: flex;
    align-items: center;
    gap: 4px;
    flex-wrap: wrap;
    margin-top: 12px;
    padding-top: 10px;
    border-top: 1px solid var(--border-light);
  }
  .detail-section {
    margin: 0;
  }
  .detail-section-grow {
    flex: 1;
    display: flex;
    flex-direction: column;
  }
  .detail-section-title {
    font-size: 0.8125rem;
    font-weight: 600;
    color: var(--text-muted);
    text-transform: uppercase;
    letter-spacing: 0.04em;
    margin-bottom: 12px;
  }
  .detail-ref-groups {
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .detail-ref-group-label {
    font-size: 0.68rem;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: var(--text-muted);
    margin-bottom: 6px;
    display: block;
  }
  .detail-ref-chips {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
  }
  .detail-obj-link {
    cursor: pointer;
    transition: border-color 0.15s ease, box-shadow 0.15s ease;
  }
  .detail-obj-link:hover {
    border-color: var(--color-olive);
    box-shadow: var(--shadow-sm);
  }
</style>