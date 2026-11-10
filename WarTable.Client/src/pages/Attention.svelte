<script>
  import { apiGet, parseDate } from '../lib/api.js';
  import { addToast } from '../lib/stores.js';
  import { onActivity } from '../lib/events.svelte.js';
  import AttentionItem from '../components/AttentionItem.svelte';

  let attention = $state(null);
  let campaigns = $state([]);
  let loading = $state(true);
  let refreshing = $state(false);
  let error = $state('');
  let kindFilter = $state('all');
  let campaignFilter = $state('');
  let ageFilter = $state('any');
  let loadToken = 0;

  const kindOptions = [
    { value: 'all', label: 'Everything' },
    { value: 'flagged', label: 'Flagged' },
    { value: 'blocked', label: 'Blocked' },
    { value: 'questions', label: 'Questions' },
    { value: 'stale', label: 'Stale' },
    { value: 'drafts', label: 'Drafts' },
    { value: 'seeds', label: 'Seeds' },
  ];

  const ageOptions = [
    { value: 'any', label: 'Any age' },
    { value: 'fresh', label: 'Fresh · under 24h' },
    { value: 'week', label: 'This week · under 7d' },
    { value: 'aging', label: 'Aging · over 3d' },
  ];

  let baseGroups = $derived([
    { key: 'flagged', label: 'Flagged steps', hint: 'The human-attention channel', items: attention?.flaggedSteps ?? [] },
    { key: 'blocked', label: 'Blocked objectives', hint: 'Derived from blocked or waiting work', items: attention?.blockedObjectives ?? [] },
    { key: 'questions', label: 'Open questions', hint: 'Bunker questions still active', items: attention?.openQuestions ?? [] },
    { key: 'stale', label: 'Stale in-progress', hint: 'No update in over 72 hours', items: attention?.staleInProgressSteps ?? [] },
  ]);

  let drafts = $derived(attention?.draftObjectives ?? []);
  let seeds = $derived(attention?.seedObjectives ?? []);

  function ageInDays(since) {
    if (!since) return null;
    return (Date.now() - parseDate(since).getTime()) / 86400000;
  }

  function matchesFilters(item) {
    if (campaignFilter && item.campaignId !== campaignFilter) return false;
    const age = ageInDays(item.since);
    if (ageFilter === 'fresh') return age != null && age < 1;
    if (ageFilter === 'week') return age != null && age < 7;
    if (ageFilter === 'aging') return age != null && age > 3;
    return true;
  }

  let filteredGroups = $derived(
    baseGroups
      .filter((group) => kindFilter === 'all' || kindFilter === group.key)
      .map((group) => ({
        ...group,
        items: group.items.filter(matchesFilters),
      }))
      .filter((group) => group.items.length > 0),
  );

  let filteredDrafts = $derived(
    kindFilter === 'all' || kindFilter === 'drafts'
      ? drafts.filter(matchesFilters)
      : [],
  );

  let filteredSeeds = $derived(
    kindFilter === 'all' || kindFilter === 'seeds'
      ? seeds.filter(matchesFilters)
      : [],
  );

  let urgentCount = $derived(
    baseGroups.reduce((sum, group) => sum + group.items.length, 0),
  );
  let visibleCount = $derived(
    filteredGroups.reduce((sum, group) => sum + group.items.length, 0) +
      filteredDrafts.length +
      filteredSeeds.length,
  );
  let hasFilters = $derived(kindFilter !== 'all' || campaignFilter !== '' || ageFilter !== 'any');

  async function load({ silent = false } = {}) {
    if (silent) refreshing = true;
    else loading = true;
    error = '';
    const token = ++loadToken;
    try {
      const [attentionData, campaignData] = await Promise.all([
        apiGet('/api/attention'),
        apiGet('/api/campaigns'),
      ]);
      if (token !== loadToken) return;
      attention = attentionData;
      campaigns = Array.isArray(campaignData) ? campaignData : [];
    } catch (err) {
      if (token !== loadToken) return;
      error = err.message || 'Could not load the inbox.';
      addToast('Could not load the attention inbox.', 'error');
    } finally {
      if (token === loadToken) {
        loading = false;
        refreshing = false;
      }
    }
  }

  function clearFilters() {
    kindFilter = 'all';
    campaignFilter = '';
    ageFilter = 'any';
  }

  load();

  $effect(() => {
    return onActivity(() => load({ silent: true }));
  });
</script>

<svelte:head>
  <title>WarTable — Attention</title>
</svelte:head>

<div class="page-intro reveal" style="--i: 0">
  <div>
    <p class="eyebrow">Cross-campaign inbox</p>
    <h1 class="page-title">What needs a human</h1>
    <p class="lede">
      Flagged steps come first, then blockers, open questions, and work that has gone quiet. Draft plans
      and seeds are parked separately so they never masquerade as urgent.
    </p>
  </div>
  <div class="page-actions">
    {#if refreshing}<span class="sync-note">Syncing…</span>{/if}
    <span class="stat-inline"><strong>{urgentCount}</strong> urgent</span>
    <span class="stat-inline"><strong>{drafts.length}</strong> parked</span>
    <span class="stat-inline"><strong>{seeds.length}</strong> seeds</span>
  </div>
</div>

<div class="filter-bar reveal" style="--i: 1">
  <div class="segmented" role="group" aria-label="Filter by kind">
    {#each kindOptions as option}
      <button
        type="button"
        class="seg-btn"
        class:is-active={kindFilter === option.value}
        onclick={() => (kindFilter = option.value)}
      >{option.label}</button>
    {/each}
  </div>

  <label class="select-field">
    <span class="sr-only">Filter by campaign</span>
    <select class="form-select" bind:value={campaignFilter}>
      <option value="">All campaigns</option>
      {#each campaigns as campaign (campaign.id)}
        <option value={campaign.id}>{campaign.title}</option>
      {/each}
    </select>
  </label>

  <label class="select-field">
    <span class="sr-only">Filter by age</span>
    <select class="form-select" bind:value={ageFilter}>
      {#each ageOptions as option}
        <option value={option.value}>{option.label}</option>
      {/each}
    </select>
  </label>

  {#if hasFilters}
    <button type="button" class="btn btn-ghost btn-sm" onclick={clearFilters}>Clear filters</button>
  {/if}

  <span class="filter-spacer"></span>
  <span class="result-count">{visibleCount} shown</span>
</div>

{#if loading}
  <div class="loading-state">
    <div class="loading-spinner"></div>
    <p>Reading the inbox…</p>
  </div>
{:else if error}
  <div class="error-state">
    <p class="eyebrow">Connection trouble</p>
    <h1>The inbox could not be read.</h1>
    <p class="lede">{error}</p>
    <button type="button" class="btn btn-primary" onclick={() => load()}>Try again</button>
  </div>
{:else if filteredGroups.length === 0 && filteredDrafts.length === 0 && filteredSeeds.length === 0}
  <div class="empty-state">
    <h3>{hasFilters ? 'Nothing matches those filters' : 'The inbox is clear'}</h3>
    <p>
      {hasFilters
        ? 'Widen the filters to see the rest of the inbox.'
        : 'No flagged steps, blocked objectives, open questions, or stale work across any campaign.'}
    </p>
    {#if hasFilters}
      <button type="button" class="btn btn-secondary" onclick={clearFilters}>Clear filters</button>
    {/if}
  </div>
{:else}
  <div class="attention-page">
    {#each filteredGroups as group (group.key)}
      <section class="panel attention-group-panel" class:tone-flag-panel={group.key === 'flagged'}>
        <header class="attention-group-head">
          <div>
            <p class="eyebrow">{group.hint}</p>
            <h2 class="panel-title">{group.label}</h2>
          </div>
          <span class="inbox-count big">{group.items.length}</span>
        </header>
        <div class="attention-list">
          {#each group.items as item (item.entityId + item.kind)}
            <AttentionItem {item} />
          {/each}
        </div>
      </section>
    {/each}

    {#if filteredDrafts.length > 0}
      <section class="panel parked-panel">
        <header class="attention-group-head">
          <div>
            <p class="eyebrow">Parked · calm by design</p>
            <h2 class="panel-title">Draft plans</h2>
            <p class="panel-subtitle">Visible but not actionable until someone activates the objective.</p>
          </div>
          <span class="inbox-count big">{filteredDrafts.length}</span>
        </header>
        <div class="attention-list">
          {#each filteredDrafts as item (item.entityId)}
            <AttentionItem {item} />
          {/each}
        </div>
      </section>
    {/if}

    {#if filteredSeeds.length > 0}
      <section class="panel parked-panel seed-panel">
        <header class="attention-group-head">
          <div>
            <p class="eyebrow">Unfinished thoughts · calm by design</p>
            <h2 class="panel-title">Seeds to shape</h2>
            <p class="panel-subtitle">
              Seeds are visible to agents in the briefing. Shape one into a draft when it is ready.
            </p>
          </div>
          <span class="inbox-count big">{filteredSeeds.length}</span>
        </header>
        <div class="attention-list">
          {#each filteredSeeds as item (item.entityId)}
            <AttentionItem {item} />
          {/each}
        </div>
      </section>
    {/if}
  </div>
{/if}
