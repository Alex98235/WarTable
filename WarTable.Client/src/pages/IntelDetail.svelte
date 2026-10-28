<script>
  import { apiGet, apiPut, apiDelete, parseTags, timeAgo } from '../lib/api.js';
  import { addToast, showConfirm } from '../lib/stores.js';
  import Markdown from '../components/Markdown.svelte';
  import MarkdownEditor from '../components/MarkdownEditor.svelte';

  let { route } = $props();
  let campaignId = $derived(route.params.id);
  let alias = $derived(parseInt(route.params.alias, 10));

  let campaign = $state(null);
  let loading = $state(true);
  let entry = $derived(
    campaign?.intelEntries?.find((e) => e.alias === alias) ?? null,
  );

  // ── Edit mode ──
  let editMode = $state(false);
  let saving = $state(false);
  let form = $state({ title: '', body: '', category: 'Context', tags: '' });

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
      addToast(err.message || 'Failed to load intel entry.', 'error');
      campaign = null;
    } finally {
      loading = false;
    }
  }

  function goTo(path) {
    window.location.hash = path;
  }

  function enterEdit() {
    if (!entry) return;
    form = {
      title: entry.title || '',
      body: entry.body || '',
      category: entry.category || 'Context',
      tags: parseTags(entry.tags).join(', '),
    };
    editMode = true;
  }

  function leaveEdit() {
    editMode = false;
  }

  async function saveIntel() {
    if (!entry) return;
    saving = true;
    try {
      await apiPut(`/api/intel/${entry.id}`, {
        title: form.title,
        body: form.body,
        category: form.category,
        tags: parseTags(form.tags).join(', '),
      });
      addToast('Intel entry updated.', 'success');
      await loadCampaign();
      editMode = false;
    } catch (err) {
      addToast(err.message || 'Failed to update intel entry.', 'error');
    } finally {
      saving = false;
    }
  }

  function deleteIntel() {
    if (!entry) return;
    showConfirm(
      `Delete intel entry #${entry.alias} "${entry.title}"? This cannot be undone.`,
      async () => {
        try {
          await apiDelete(`/api/intel/${entry.id}`);
          addToast('Intel entry deleted.', 'success');
          goTo(`/campaign/${campaignId}`);
        } catch (err) {
          addToast(err.message || 'Failed to delete intel entry.', 'error');
        }
      },
    );
  }

  function categoryBadge(category) {
    if (!category) return 'badge-reference';
    const c = category.toLowerCase();
    if (c === 'decision') return 'badge-decision';
    if (c === 'context') return 'badge-context';
    if (c === 'gotcha') return 'badge-gotcha';
    return 'badge-reference';
  }
</script>

{#if loading}
  <div class="loading">Loading intel entry…</div>
{:else if campaign && entry}
  <div class="detail-page">
    <a
      href="#/campaign/{campaignId}"
      class="btn btn-ghost btn-sm detail-back-link"
      onclick={(e) => { e.preventDefault(); goTo(`/campaign/${campaignId}`); }}
    >← Back to campaign</a>

    <div class="detail-header card card-padding">
      <div class="detail-header-top">
        <div class="detail-title-group">
          {#if entry.alias != null}
            <span class="detail-alias-chip">#{entry.alias}</span>
          {/if}
          {#if editMode}
            <div class="detail-title-input">
              <label class="form-label" for="intel-edit-title">Title</label>
              <input
                id="intel-edit-title"
                class="form-input"
                type="text"
                bind:value={form.title}
              />
            </div>
          {:else}
            <h1 class="detail-title">{entry.title}</h1>
          {/if}
        </div>
        {#if !editMode}
          <div class="detail-header-actions">
            <button type="button" class="btn btn-sm btn-secondary" onclick={enterEdit}>Edit</button>
          </div>
        {/if}
      </div>

      {#if !editMode}
        <div class="detail-badges">
          <span class="tag-badge">Intel</span>
          {#if entry.category}
            <span class="badge {categoryBadge(entry.category)}">{entry.category}</span>
          {/if}
        </div>
        <div class="detail-meta">
          {#if entry.tags}
            {#each parseTags(entry.tags) as tag}
              <span class="tag-badge">{tag}</span>
            {/each}
          {/if}
          {#if entry.createdAt}
            <span class="text-muted text-sm">Created {timeAgo(entry.createdAt)}</span>
          {/if}
        </div>
      {:else}
        <div class="detail-header-edit-fields">
          <div class="detail-edit-inline">
            <div class="detail-edit-field">
              <label class="form-label" for="intel-edit-category">Category</label>
              <select id="intel-edit-category" class="form-select" bind:value={form.category}>
                <option value="Decision">Decision</option>
                <option value="Context">Context</option>
                <option value="Gotcha">Gotcha</option>
                <option value="Reference">Reference</option>
              </select>
            </div>
            <div class="detail-edit-field">
              <label class="form-label" for="intel-edit-tags">Tags</label>
              <input
                id="intel-edit-tags"
                class="form-input"
                type="text"
                bind:value={form.tags}
                placeholder="Comma separated, e.g. logistics, allies"
              />
            </div>
          </div>
        </div>
      {/if}
    </div>

    {#if editMode}
      <div class="detail-section detail-section-grow card card-padding">
        <h2 class="detail-section-title">Body</h2>
        <MarkdownEditor
          id="intel-edit-body"
          bind:value={form.body}
          {campaignId}
          fill={true}
        />
      </div>

      <div class="detail-actions">
        <button type="button" class="btn btn-danger" onclick={deleteIntel}>Delete</button>
        <div class="detail-actions-right">
          <button type="button" class="btn btn-ghost" onclick={leaveEdit}>Cancel</button>
          <button
            type="button"
            class="btn btn-primary"
            onclick={saveIntel}
            disabled={saving}
          >{saving ? 'Saving…' : 'Save'}</button>
        </div>
      </div>
    {:else}
      {#if entry.body}
        <div class="detail-section detail-section-grow card card-padding">
          <Markdown source={entry.body} {campaignId} />
        </div>
      {/if}
    {/if}
  </div>
{:else}
  <div class="empty-state">
    <h3>Intel not found</h3>
    <p>This intel entry doesn't exist or may have been deleted.</p>
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
  .detail-title-input {
    flex: 1 1 320px;
    max-width: 640px;
  }
  .detail-title-input .form-input {
    font-size: 1.125rem;
    font-weight: 600;
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
  .detail-header-actions {
    flex-shrink: 0;
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
    gap: 6px;
    flex-wrap: wrap;
    margin-top: 12px;
    padding-top: 10px;
    border-top: 1px solid var(--border-light);
  }
  .detail-header-edit-fields {
    display: flex;
    flex-direction: column;
    gap: 12px;
    margin-top: 14px;
  }
  .detail-edit-inline {
    display: flex;
    gap: 16px;
    flex-wrap: wrap;
  }
  .detail-edit-field {
    display: flex;
    flex-direction: column;
    gap: 4px;
    flex: 1 1 200px;
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
    display: block;
    font-size: 0.8125rem;
    font-weight: 600;
    color: var(--text-muted);
    text-transform: uppercase;
    letter-spacing: 0.04em;
    margin-bottom: 12px;
  }
  .detail-actions {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    flex-wrap: wrap;
  }
  .detail-actions-right {
    display: flex;
    align-items: center;
    gap: 8px;
  }
</style>