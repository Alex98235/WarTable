<script>
  import { apiGet, apiPut, apiDelete, timeAgo } from '../lib/api.js';
  import { addToast, showConfirm } from '../lib/stores.js';
  import Markdown from '../components/Markdown.svelte';
  import MarkdownEditor from '../components/MarkdownEditor.svelte';

  let { route } = $props();
  let campaignId = $derived(route.params.id);
  let alias = $derived(parseInt(route.params.alias, 10));

  let campaign = $state(null);
  let loading = $state(true);
  let objective = $derived(
    campaign?.objectives?.find((o) => o.alias === alias) ?? null,
  );

  // ── Edit mode ──
  let editMode = $state(false);
  let saving = $state(false);
  let form = $state({ title: '', description: '', lane: 'backlog' });

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
      addToast(err.message || 'Failed to load objective.', 'error');
      campaign = null;
    } finally {
      loading = false;
    }
  }

  function goTo(path) {
    window.location.hash = path;
  }

  function enterEdit() {
    if (!objective) return;
    form = {
      title: objective.title || '',
      description: objective.description || '',
      lane: objective.lane || 'backlog',
    };
    editMode = true;
  }

  function leaveEdit() {
    editMode = false;
  }

  async function saveObjective() {
    if (!objective) return;
    saving = true;
    try {
      await apiPut(`/api/objectives/${objective.id}`, {
        title: form.title,
        description: form.description,
        lane: form.lane,
      });
      addToast('Objective updated.', 'success');
      await loadCampaign();
      editMode = false;
    } catch (err) {
      addToast(err.message || 'Failed to update objective.', 'error');
    } finally {
      saving = false;
    }
  }

  function deleteObjective() {
    if (!objective) return;
    showConfirm(
      `Delete objective #${objective.alias} "${objective.title}"? This cannot be undone.`,
      async () => {
        try {
          await apiDelete(`/api/objectives/${objective.id}`);
          addToast('Objective deleted.', 'success');
          goTo(`/campaign/${campaignId}`);
        } catch (err) {
          addToast(err.message || 'Failed to delete objective.', 'error');
        }
      },
    );
  }

  // Parse dependencies — may be an array of alias ints or raw text
  function dependencies() {
    if (!objective) return [];
    if (Array.isArray(objective.dependencies)) return objective.dependencies;
    if (typeof objective.dependencies === 'string' && objective.dependencies) {
      const nums = objective.dependencies.match(/#?(\d+)/g);
      return nums ? nums.map((n) => parseInt(n.replace('#', ''), 10)) : [];
    }
    return [];
  }

  function laneBadge(lane) {
    return 'badge-' + (lane || 'backlog').toLowerCase();
  }
</script>

{#if loading}
  <div class="loading">Loading objective…</div>
{:else if campaign && objective}
  <div class="detail-page">
    <a
      href="#/campaign/{campaignId}"
      class="btn btn-ghost btn-sm detail-back-link"
      onclick={(e) => { e.preventDefault(); goTo(`/campaign/${campaignId}`); }}
    >← Back to campaign</a>

    <div class="detail-header card card-padding">
      <div class="detail-header-top">
        <div class="detail-title-group">
          {#if objective.alias != null}
            <span class="detail-alias-chip">#{objective.alias}</span>
          {/if}
          {#if editMode}
            <div class="detail-title-input">
              <label class="form-label" for="obj-edit-title">Title</label>
              <input
                id="obj-edit-title"
                class="form-input"
                type="text"
                bind:value={form.title}
              />
            </div>
          {:else}
            <h1 class="detail-title">{objective.title}</h1>
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
          <span class="tag-badge">Objective</span>
          {#if objective.lane}
            <span class="badge {laneBadge(objective.lane)}">{objective.lane}</span>
          {/if}
        </div>
        <div class="detail-meta">
          {#if objective.createdAt}
            <span class="text-muted text-sm">Created {timeAgo(objective.createdAt)}</span>
          {/if}
          {#if objective.updatedAt}
            <span class="text-muted text-sm"> · Updated {timeAgo(objective.updatedAt)}</span>
          {/if}
        </div>
      {:else}
        <div class="detail-header-edit-fields">
          <div class="detail-edit-field">
            <label class="form-label" for="obj-edit-lane">Lane</label>
            <select id="obj-edit-lane" class="form-select" bind:value={form.lane}>
              <option value="backlog">Backlog</option>
              <option value="ready">Ready</option>
              <option value="in-progress">In Progress</option>
              <option value="review">Review</option>
              <option value="done">Done</option>
            </select>
          </div>
        </div>
      {/if}
    </div>

    {#if editMode}
      <div class="detail-section detail-section-grow card card-padding">
        <h2 class="detail-section-title">Description</h2>
        <MarkdownEditor
          id="obj-edit-description"
          bind:value={form.description}
          {campaignId}
          fill={true}
        />
      </div>

      <div class="detail-actions">
        <button type="button" class="btn btn-danger" onclick={deleteObjective}>Delete</button>
        <div class="detail-actions-right">
          <button type="button" class="btn btn-ghost" onclick={leaveEdit}>Cancel</button>
          <button
            type="button"
            class="btn btn-primary"
            onclick={saveObjective}
            disabled={saving}
          >{saving ? 'Saving…' : 'Save'}</button>
        </div>
      </div>
    {:else}
      {#if objective.description}
        <div class="detail-section detail-section-grow card card-padding">
          <h2 class="detail-section-title">Description</h2>
          <Markdown source={objective.description} {campaignId} />
        </div>
      {/if}

      {#if dependencies().length > 0}
        <div class="detail-section card card-padding">
          <h2 class="detail-section-title">Dependencies</h2>
          <div class="detail-tags">
            {#each dependencies() as depAlias}
              <a
                href="#/campaign/{campaignId}/objective/{depAlias}"
                class="tag-badge detail-tag-link"
                onclick={(e) => { e.preventDefault(); goTo(`/campaign/${campaignId}/objective/${depAlias}`); }}
              >#{depAlias}</a>
            {/each}
          </div>
        </div>
      {/if}
    {/if}
  </div>
{:else}
  <div class="empty-state">
    <h3>Objective not found</h3>
    <p>This objective doesn't exist or may have been deleted.</p>
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
    gap: 4px;
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
  .detail-edit-field {
    display: flex;
    flex-direction: column;
    gap: 4px;
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
  .detail-tags {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
  }
  .detail-tag-link {
    text-decoration: none;
    cursor: pointer;
    transition: background 0.15s ease, color 0.15s ease;
  }
  .detail-tag-link:hover {
    background: var(--color-olive);
    color: var(--text-inverse);
  }
</style>