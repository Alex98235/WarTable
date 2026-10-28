<script>
  import { apiGet, apiPost, apiDelete, parseTags, timeAgo } from '../lib/api.js';
  import { addToast, showConfirm } from '../lib/stores.js';

  let intel = $state([]);
  let loading = $state(true);
  let searchQuery = $state('');
  let categoryFilter = $state('');
  let campaignFilter = $state('');
  let campaigns = $state([]);
  let showCreateModal = $state(false);
  let expandedId = $state(null);

  // New intel form fields
  let newTitle = $state('');
  let newBody = $state('');
  let newCategory = $state('Decision');
  let newCampaignId = $state('');
  let newTags = $state([]);
  let tagInput = $state('');

  function addTag() {
    const vals = tagInput.split(',').map(t => t.trim()).filter(t => t);
    for (const v of vals) {
      if (v && !newTags.includes(v)) newTags = [...newTags, v];
    }
    tagInput = '';
  }

  function removeTag(tag) {
    newTags = newTags.filter(t => t !== tag);
  }

  function resetForm() {
    newTitle = '';
    newBody = '';
    newCategory = 'Decision';
    newCampaignId = campaignFilter || '';
    newTags = [];
    tagInput = '';
  }

  async function loadIntel() {
    loading = true;
    try {
      const params = {};
      if (searchQuery) params.search = searchQuery;
      if (categoryFilter) params.category = categoryFilter;
      if (campaignFilter) params.campaignId = campaignFilter;
      const data = await apiGet('/api/intel', params);
      intel = data.intel ?? data;
    } catch (err) {
      addToast(err.message || 'Failed to load intel', 'error');
      intel = [];
    } finally {
      loading = false;
    }
  }

  async function loadCampaigns() {
    try {
      const data = await apiGet('/api/campaigns');
      campaigns = data.campaigns ?? data;
    } catch (err) {
      // Silently fail — campaigns are auxiliary for filtering
      campaigns = [];
    }
  }

  async function createIntel() {
    if (!newTitle || !newTitle.trim()) {
      addToast('Intel title is required', 'warning');
      return;
    }
    if (!newBody || !newBody.trim()) {
      addToast('Intel body is required', 'warning');
      return;
    }
    const effectiveCampaignId = newCampaignId || campaignFilter;
    if (!effectiveCampaignId) {
      addToast('Campaign is required', 'warning');
      return;
    }
    try {
      const body = {
        title: newTitle.trim(),
        body: newBody.trim(),
        category: newCategory,
        campaignId: effectiveCampaignId,
        tags: newTags.join(', '),
      };
      await apiPost('/api/intel', body);
      addToast('Intel logged successfully', 'success');
      showCreateModal = false;
      resetForm();
      await loadIntel();
    } catch (err) {
      addToast(err.message || 'Failed to create intel', 'error');
    }
  }

  function deleteIntel(id, title) {
    showConfirm(`Are you sure you want to delete "${title}"?`, async () => {
      try {
        await apiDelete(`/api/intel/${id}`);
        addToast('Intel deleted', 'success');
        if (expandedId === id) expandedId = null;
        await loadIntel();
      } catch (err) {
        addToast(err.message || 'Failed to delete intel', 'error');
      }
    });
  }

  function toggleExpand(id) {
    expandedId = expandedId === id ? null : id;
  }

  $effect(() => {
    loadCampaigns();
    loadIntel();
  });
</script>

<div class="filters-row">
  <input
    class="search-input"
    type="text"
    placeholder="Search intel…"
    bind:value={searchQuery}
    oninput={() => loadIntel()}
  />

  <select class="form-select" bind:value={categoryFilter} onchange={() => loadIntel()}>
    <option value="">All Categories</option>
    <option value="Decision">Decision</option>
    <option value="Context">Context</option>
    <option value="Gotcha">Gotcha</option>
    <option value="Reference">Reference</option>
  </select>

  <select class="form-select" bind:value={campaignFilter} onchange={() => loadIntel()}>
    <option value="">All Campaigns</option>
    {#each campaigns as campaign (campaign.id)}
      <option value={campaign.id}>{campaign.title}</option>
    {/each}
  </select>

  <button class="btn btn-primary" onclick={() => { resetForm(); showCreateModal = true; }}>
    + Log Intel
  </button>
</div>

{#if loading}
  <div class="loading">
    <p>Loading intel…</p>
  </div>
{:else if intel.length === 0}
  <div class="empty-state">
    <h3>No intel found</h3>
    <p>
      {#if searchQuery || categoryFilter || campaignFilter}
        Try adjusting your search or filters.
      {:else}
        No intel entries yet. Log your first piece of intel to get started.
      {/if}
    </p>
  </div>
{:else}
  <div class="intel-feed">
    {#each intel as entry (entry.id)}
      <div class="intel-tab-entry">
        <div class="intel-tab-entry-header" onclick={() => toggleExpand(entry.id)} onkeydown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); toggleExpand(entry.id); } }} role="button" tabindex="0">
          <strong class="intel-tab-entry-title">
            {#if entry.alias != null}
              <span class="alias-chip-sm">#{entry.alias}</span>
            {/if}
            {entry.title}
          </strong>
          <span class="badge badge-{entry.category?.toLowerCase() || 'reference'}">{entry.category || 'reference'}</span>
        </div>
        {#if expandedId === entry.id}
          <div class="intel-tab-entry-body">
            <p>{entry.body}</p>
          </div>
        {:else}
          <div class="intel-tab-entry-body">
            <p>{entry.body?.length > 200 ? entry.body.slice(0, 200) + '...' : entry.body}</p>
          </div>
        {/if}
        <div class="intel-tab-entry-meta">
          {#if entry.tags}
            {#each parseTags(entry.tags) as tag}
              <span class="tag-badge">{tag}</span>
            {/each}
          {/if}
          <small class="text-muted">{timeAgo(entry.createdAt || entry.created_at)}</small>
          <button class="btn btn-danger btn-sm" onclick={(e) => { e.stopPropagation(); deleteIntel(entry.id, entry.title); }}>Delete</button>
        </div>
      </div>
    {/each}
  </div>
{/if}

{#if showCreateModal}
  <div class="modal-overlay" onclick={(e) => { if (e.target === e.currentTarget) showCreateModal = false; }} onkeydown={(e) => { if (e.key === "Escape") showCreateModal = false; }} role="dialog" tabindex="-1">
    <div class="modal" role="document">
      <div class="modal-header">
        <h3>Log Intel</h3>
        <button class="btn btn-ghost btn-sm" onclick={() => showCreateModal = false}>×</button>
      </div>
      <form class="modal-body" onsubmit={(e) => { e.preventDefault(); createIntel(); }}>
        <div class="form-group">
          <label class="form-label" for="intel-title">Title</label>
          <input
            id="intel-title"
            class="form-input"
            type="text"
            bind:value={newTitle}
            placeholder="Intel title"
            required
          />
        </div>

        <div class="form-group">
          <label class="form-label" for="intel-body">Body</label>
          <textarea
            id="intel-body"
            class="form-textarea"
            bind:value={newBody}
            placeholder="Intel details…"
            rows="5"
            required
          ></textarea>
        </div>

        <div class="form-group">
          <label class="form-label" for="intel-category">Category</label>
          <select id="intel-category" class="form-select" bind:value={newCategory}>
            <option value="Decision">Decision</option>
            <option value="Context">Context</option>
            <option value="Gotcha">Gotcha</option>
            <option value="Reference">Reference</option>
          </select>
        </div>

        <div class="form-group">
          <label class="form-label" for="intel-campaign">Campaign <span class="text-muted">(required)</span></label>
          <select id="intel-campaign" class="form-select" bind:value={newCampaignId} disabled={campaignFilter !== ''}>
            {#if !campaignFilter}
              <option value="">Select a campaign…</option>
            {/if}
            {#each campaigns as campaign (campaign.id)}
              <option value={campaign.id} selected={campaign.id === (campaignFilter || newCampaignId)}>{campaign.title}</option>
            {/each}
          </select>
        </div>

        <div class="form-group">
          <label class="form-label" for="intel-tags">Tags</label>
          <div class="tag-input-group">
            <input
              id="intel-tags"
              class="form-input"
              type="text"
              bind:value={tagInput}
              placeholder="Type a tag and press comma or Enter…"
              onkeydown={(e) => { if (e.key === ',' || e.key === 'Enter') { e.preventDefault(); addTag(); } }}
            />
            {#if newTags.length > 0}
              <div class="tag-tokens">
                {#each newTags as tag}
                  <span class="tag-token">
                    {tag}
                    <button onclick={() => removeTag(tag)} aria-label="Remove tag {tag}">×</button>
                  </span>
                {/each}
              </div>
            {/if}
          </div>
        </div>

        <div class="modal-actions">
          <button type="button" class="btn btn-ghost" onclick={() => { showCreateModal = false; resetForm(); }}>Cancel</button>
          <button type="submit" class="btn btn-primary" disabled={!newTitle || !newBody}>Log Intel</button>
        </div>
      </form>
    </div>
  </div>
{/if}

<style>
   .alias-chip-sm {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      padding: 0 5px;
      border-radius: 100px;
      font-size: 0.6rem;
      font-weight: 700;
      line-height: 1.5;
      background: var(--color-olive-dark);
      color: var(--color-olive-light);
      white-space: nowrap;
      margin-right: 4px;
      vertical-align: middle;
   }
</style>