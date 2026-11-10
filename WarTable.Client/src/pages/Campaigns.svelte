<script>
  import { ACTOR, apiGet, apiPost, parseTags } from '../lib/api.js';
  import { addToast } from '../lib/stores.js';
  import { onActivity } from '../lib/events.svelte.js';
  import CampaignCard from '../components/CampaignCard.svelte';
  import MarkdownEditor from '../components/MarkdownEditor.svelte';

  let campaigns = $state([]);
  let stats = $state({});
  let loading = $state(true);
  let refreshing = $state(false);
  let error = $state('');
  let search = $state('');
  let includeArchived = $state(false);
  let loadToken = 0;
  let searchTimer = null;

  let showCreate = $state(false);
  let createForm = $state({ title: '', intent: '', tags: '' });
  let creating = $state(false);

  async function load({ silent = false } = {}) {
    if (silent) refreshing = true;
    else loading = true;
    error = '';
    const token = ++loadToken;

    try {
      const params = {};
      if (search.trim()) params.search = search.trim();
      if (includeArchived) params.includeArchived = 'true';

      const data = await apiGet('/api/campaigns', params);
      if (token !== loadToken) return;

      campaigns = Array.isArray(data) ? data : [];
      await loadStats(campaigns, token);
    } catch (err) {
      if (token !== loadToken) return;
      error = err.message || 'Could not load campaigns.';
      addToast('Could not load campaigns.', 'error');
    } finally {
      if (token === loadToken) {
        loading = false;
        refreshing = false;
      }
    }
  }

  async function loadStats(list, token) {
    const entries = await Promise.all(
      list.map(async (campaign) => {
        const [objectives, activity] = await Promise.all([
          apiGet('/api/objectives', { campaignId: campaign.id }).catch(() => []),
          apiGet('/api/activity', { campaignId: campaign.id, limit: 1 }).catch(() => []),
        ]);
        const objectiveList = Array.isArray(objectives) ? objectives : [];
        const planList = objectiveList.filter((o) => o.planState !== 'Seed');
        return [
          campaign.id,
          {
            total: planList.length,
            active: planList.filter((o) => o.planState === 'Active').length,
            draft: planList.filter((o) => o.planState === 'Draft').length,
            completed: planList.filter((o) => o.status === 'completed').length,
            seeds: objectiveList.filter((o) => o.planState === 'Seed').length,
            lastActivity: activity?.[0]?.createdAt || campaign.updatedAt || campaign.createdAt,
          },
        ];
      }),
    );
    if (token !== loadToken) return;
    stats = Object.fromEntries(entries);
  }

  function onSearchInput() {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => load({ silent: true }), 260);
  }

  function openCreate() {
    createForm = { title: '', intent: '', tags: '' };
    showCreate = true;
  }

  async function createCampaign(event) {
    event.preventDefault();
    if (!createForm.title.trim()) {
      addToast('A campaign needs a title.', 'warning');
      return;
    }
    creating = true;
    try {
      await apiPost('/api/campaigns', {
        title: createForm.title.trim(),
        intent: createForm.intent,
        tags: parseTags(createForm.tags).join(', '),
        actor: ACTOR,
      });
      addToast('Campaign created.', 'success');
      showCreate = false;
      await load({ silent: true });
    } catch (err) {
      addToast(err.message || 'Could not create the campaign.', 'error');
    } finally {
      creating = false;
    }
  }

  load();

  $effect(() => {
    return onActivity(() => load({ silent: true }));
  });
</script>

<svelte:head>
  <title>WarTable — Campaigns</title>
</svelte:head>

<div class="page-intro reveal" style="--i: 0">
  <div>
    <p class="eyebrow">Your work</p>
    <h1 class="page-title">Campaigns, not statuses</h1>
    <p class="lede">
      A campaign holds objectives, steps, and Bunker knowledge. It has no state of its own — the plan does.
    </p>
  </div>
  <div class="page-actions">
    {#if refreshing}<span class="sync-note">Syncing…</span>{/if}
    <button type="button" class="btn btn-primary" onclick={openCreate}>+ New campaign</button>
  </div>
</div>

<div class="filter-bar reveal" style="--i: 1">
  <label class="search-field">
    <span class="search-glyph" aria-hidden="true">⌕</span>
    <span class="sr-only">Search campaigns</span>
    <input
      type="search"
      placeholder="Search title, intent, or tags…"
      bind:value={search}
      oninput={onSearchInput}
    />
  </label>
  <label class="check-row compact">
    <input type="checkbox" bind:checked={includeArchived} onchange={() => load({ silent: true })} />
    <span>Include archived</span>
  </label>
  <span class="filter-spacer"></span>
  <span class="result-count">
    {campaigns.length} campaign{campaigns.length === 1 ? '' : 's'}
  </span>
</div>

{#if loading}
  <div class="loading-state">
    <div class="loading-spinner"></div>
    <p>Opening campaigns…</p>
  </div>
{:else if error}
  <div class="error-state">
    <p class="eyebrow">Connection trouble</p>
    <h1>Campaigns could not be listed.</h1>
    <p class="lede">{error}</p>
    <button type="button" class="btn btn-primary" onclick={() => load()}>Try again</button>
  </div>
{:else if campaigns.length === 0}
  <div class="empty-state">
    <h3>{search ? 'No campaigns match that search' : 'No campaigns yet'}</h3>
    <p>
      {search
        ? 'Try a shorter search, or clear it to see every campaign.'
        : 'Start with a campaign for the body of work. Objectives and steps carry the plan.'}
    </p>
    {#if search}
      <button
        type="button"
        class="btn btn-secondary"
        onclick={() => { search = ''; load(); }}
      >Clear search</button>
    {:else}
      <button type="button" class="btn btn-primary" onclick={openCreate}>Create the first campaign</button>
    {/if}
  </div>
{:else}
  <div class="folder-grid">
    {#each campaigns as campaign (campaign.id)}
      <div class="folder-wrap">
        <CampaignCard {campaign} stats={stats[campaign.id] || {}} />
        {#if campaign.archivedAt}
          <span class="archived-badge">Archived</span>
        {/if}
      </div>
    {/each}
  </div>
{/if}

{#if showCreate}
  <div
    class="modal-overlay"
    role="presentation"
    onclick={(e) => { if (e.target === e.currentTarget) showCreate = false; }}
  >
    <div
      class="modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="create-campaign-title"
      tabindex="-1"
      onclick={(e) => e.stopPropagation()}
      onkeydown={(e) => { if (e.key === 'Escape') showCreate = false; }}
    >
      <form onsubmit={createCampaign}>
        <div class="modal-header">
          <div>
            <p class="eyebrow">New campaign</p>
            <h2 id="create-campaign-title">Create a campaign</h2>
          </div>
          <button type="button" class="modal-close" onclick={() => (showCreate = false)} aria-label="Close">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label class="form-label" for="campaign-title">Title</label>
            <input
              id="campaign-title"
              class="form-input"
              type="text"
              bind:value={createForm.title}
              placeholder="e.g. Auth overhaul"
              required
            />
          </div>
          <div class="form-group">
            <label class="form-label" for="campaign-intent">Intent <span class="optional">(markdown)</span></label>
            <MarkdownEditor
              id="campaign-intent"
              bind:value={createForm.intent}
              campaignId=""
              placeholder="Why does this project exist? What does done feel like?"
              rows={4}
            />
          </div>
          <div class="form-group">
            <label class="form-label" for="campaign-tags">Tags <span class="optional">(comma separated)</span></label>
            <input
              id="campaign-tags"
              class="form-input"
              type="text"
              bind:value={createForm.tags}
              placeholder="auth, backend, risky"
            />
          </div>
        </div>
        <div class="modal-actions">
          <button type="button" class="btn btn-ghost" onclick={() => (showCreate = false)}>Cancel</button>
          <button type="submit" class="btn btn-primary" disabled={creating}>
            {creating ? 'Creating…' : 'Create campaign'}
          </button>
        </div>
      </form>
    </div>
  </div>
{/if}
