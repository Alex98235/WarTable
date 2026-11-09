<script>
  import {
    ACTOR,
    apiDelete,
    apiGet,
    apiPatch,
    apiPost,
    apiPut,
    parseTags,
    plainText,
    stepProgress,
    timeAgo,
    latestUpdate,
    actorLabel,
  } from '../lib/api.js';
  import { addToast, showConfirm } from '../lib/stores.js';
  import { onActivity } from '../lib/events.svelte.js';
  import Markdown from '../components/Markdown.svelte';
  import MarkdownEditor from '../components/MarkdownEditor.svelte';
  import StatusBadge from '../components/StatusBadge.svelte';
  import BriefingPanel from '../components/BriefingPanel.svelte';

  let { route } = $props();
  let campaignId = $derived(route.params.id);

  let campaign = $state(null);
  let objectives = $state([]);
  let objectiveDetails = $state({});
  let briefing = $state(null);
  let notes = $state([]);

  let loading = $state(true);
  let refreshing = $state(false);
  let error = $state('');
  let loadToken = 0;

  let expanded = $state({});
  let expansionInitialized = false;

  let railSearch = $state('');

  let editingCampaign = $state(false);
  let campaignForm = $state({ title: '', intent: '', tags: '' });
  let campaignSaving = $state(false);

  let showObjectiveModal = $state(false);
  let objectiveForm = $state({ title: '', description: '', planState: 'Draft' });
  let objectiveSaving = $state(false);

  let editingObjective = $state(null);
  let objectiveEditForm = $state({ title: '', description: '', planState: 'Draft' });
  let objectiveEditSaving = $state(false);

  let planObjectives = $derived(
    [...objectives]
      .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
      .map((objective) => objectiveDetails[objective.id] || objective),
  );

  let tags = $derived(campaign ? parseTags(campaign.tags) : []);

  let pinnedDirectives = $derived(
    notes.filter(
      (note) => note.kind === 'Directive' && note.status === 'Active' && note.pinned,
    ),
  );

  let railNotes = $derived(
    notes
      .filter((note) => note.status === 'Active')
      .filter((note) => {
        const query = railSearch.trim().toLowerCase();
        if (!query) return true;
        return (
          (note.title || '').toLowerCase().includes(query) ||
          (note.body || '').toLowerCase().includes(query) ||
          (note.tags || '').toLowerCase().includes(query)
        );
      })
      .slice(0, 6),
  );

  let totalSteps = $derived(
    planObjectives.reduce((sum, objective) => sum + (objective.steps?.length || 0), 0),
  );

  let completedSteps = $derived(
    planObjectives.reduce(
      (sum, objective) => sum + (objective.steps || []).filter((step) => step.status === 'done').length,
      0,
    ),
  );

  async function load({ silent = false } = {}) {
    if (silent) refreshing = true;
    else loading = true;
    error = '';
    const token = ++loadToken;

    try {
      const [campaignData, objectiveData, briefingData, noteData] = await Promise.all([
        apiGet(`/api/campaigns/${campaignId}`),
        apiGet('/api/objectives', { campaignId }),
        apiGet('/api/briefing', { campaignId }),
        apiGet('/api/notes', { campaignId }),
      ]);

      if (token !== loadToken) return;

      campaign = campaignData;
      briefing = briefingData;
      notes = Array.isArray(noteData) ? noteData : [];
      objectives = Array.isArray(objectiveData) ? objectiveData : [];

      const details = await Promise.all(
        objectives.map((objective) =>
          apiGet(`/api/objectives/${objective.id}`).catch(() => objective),
        ),
      );
      if (token !== loadToken) return;

      objectiveDetails = Object.fromEntries(details.map((detail) => [detail.id, detail]));

      if (!expansionInitialized) {
        expanded = Object.fromEntries(
          details.map((detail) => [
            detail.id,
            (detail.steps || []).some((step) => step.status === 'flagged' || step.status === 'blocked'),
          ]),
        );
        expansionInitialized = true;
      }
    } catch (err) {
      if (token !== loadToken) return;
      error = err.message || 'Could not load the campaign.';
      addToast('Could not load the campaign.', 'error');
    } finally {
      if (token === loadToken) {
        loading = false;
        refreshing = false;
      }
    }
  }

  function go(path) {
    window.location.hash = path;
  }

  function toggleExpanded(id) {
    expanded = { ...expanded, [id]: !expanded[id] };
  }

  function objectiveHref(objective) {
    return `#/campaign/${campaignId}/objective/${objective.alias}`;
  }

  function noteHref(note) {
    return `#/campaign/${campaignId}/bunker/${note.alias}`;
  }

  function openEditCampaign() {
    campaignForm = {
      title: campaign?.title || '',
      intent: campaign?.intent || '',
      tags: Array.isArray(campaign?.tags) ? campaign.tags.join(', ') : campaign?.tags || '',
    };
    editingCampaign = true;
  }

  async function saveCampaign(event) {
    event.preventDefault();
    campaignSaving = true;
    try {
      await apiPut(`/api/campaigns/${campaignId}`, {
        title: campaignForm.title.trim(),
        intent: campaignForm.intent,
        tags: parseTags(campaignForm.tags).join(', '),
        actor: ACTOR,
      });
      addToast('Campaign updated.', 'success');
      editingCampaign = false;
      await load({ silent: true });
    } catch (err) {
      addToast(err.message || 'Could not update the campaign.', 'error');
    } finally {
      campaignSaving = false;
    }
  }

  function deleteCampaign() {
    showConfirm(
      `Delete the campaign "${campaign?.title}" and all of its objectives, steps, and notes? This cannot be undone.`,
      async () => {
        try {
          await apiDelete(`/api/campaigns/${campaignId}`, { actor: ACTOR });
          addToast('Campaign deleted.', 'success');
          go('/campaigns');
        } catch (err) {
          addToast(err.message || 'Could not delete the campaign.', 'error');
        }
      },
    );
  }

  function openObjectiveModal() {
    objectiveForm = { title: '', description: '', planState: 'Draft' };
    showObjectiveModal = true;
  }

  function openObjectiveEdit(objective) {
    editingObjective = objective;
    objectiveEditForm = {
      title: objective.title || '',
      description: objective.description || '',
      planState: objective.planState || 'Draft',
    };
  }

  async function saveObjectiveEdit(event) {
    event.preventDefault();
    if (!editingObjective) return;
    objectiveEditSaving = true;
    try {
      await apiPut(`/api/objectives/${editingObjective.id}`, {
        title: objectiveEditForm.title.trim(),
        description: objectiveEditForm.description,
        planState: objectiveEditForm.planState,
        actor: ACTOR,
      });
      addToast('Objective updated.', 'success');
      editingObjective = null;
      await load({ silent: true });
    } catch (err) {
      addToast(err.message || 'Could not update the objective.', 'error');
    } finally {
      objectiveEditSaving = false;
    }
  }

  async function createObjective(event) {
    event.preventDefault();
    if (!objectiveForm.title.trim()) {
      addToast('An objective needs a title.', 'warning');
      return;
    }
    objectiveSaving = true;
    try {
      const created = await apiPost('/api/objectives', {
        campaignId,
        title: objectiveForm.title.trim(),
        description: objectiveForm.description,
        planState: objectiveForm.planState,
        actor: ACTOR,
      });
      addToast(`Objective #${created.alias} created.`, 'success');
      showObjectiveModal = false;
      expansionInitialized = false;
      await load({ silent: true });
    } catch (err) {
      addToast(err.message || 'Could not create the objective.', 'error');
    } finally {
      objectiveSaving = false;
    }
  }

  async function setPlanState(objective, planState) {
    try {
      await apiPut(`/api/objectives/${objective.id}`, { planState, actor: ACTOR });
      addToast(planState === 'Active' ? 'Objective activated.' : 'Objective parked as draft.', 'success');
      await load({ silent: true });
    } catch (err) {
      addToast(err.message || 'Could not change the plan state.', 'error');
    }
  }

  async function moveObjective(objective, direction) {
    const list = planObjectives;
    const index = list.findIndex((candidate) => candidate.id === objective.id);
    const swapIndex = index + direction;
    if (index < 0 || swapIndex < 0 || swapIndex >= list.length) return;

    const ids = list.map((candidate) => candidate.id);
    [ids[index], ids[swapIndex]] = [ids[swapIndex], ids[index]];
    const orders = Object.fromEntries(ids.map((id, position) => [id, position]));

    try {
      await apiPut('/api/objectives/reorder', {
        campaignId,
        orders,
        actor: ACTOR,
      });
      await load({ silent: true });
    } catch (err) {
      addToast(err.message || 'Could not reorder objectives.', 'error');
    }
  }

  async function toggleFlag(step) {
    const status = step.status === 'flagged' ? 'open' : 'flagged';
    try {
      await apiPatch(`/api/steps/${step.id}/status`, { status, actor: ACTOR });
      addToast(status === 'flagged' ? 'Step flagged for human attention.' : 'Flag cleared.', 'success');
      await load({ silent: true });
    } catch (err) {
      addToast(err.message || 'Could not update the step.', 'error');
    }
  }

  load();

  $effect(() => {
    return onActivity(() => load({ silent: true }));
  });
</script>

<svelte:head>
  <title>{campaign ? `${campaign.title} — WarTable` : 'Campaign — WarTable'}</title>
</svelte:head>

{#if loading}
  <div class="loading-state">
    <div class="loading-spinner"></div>
    <p>Opening the campaign…</p>
  </div>
{:else if error}
  <div class="error-state">
    <p class="eyebrow">Connection trouble</p>
    <h1>The campaign could not be opened.</h1>
    <p class="lede">{error}</p>
    <div class="page-actions">
      <button type="button" class="btn btn-primary" onclick={() => load()}>Try again</button>
      <a class="btn btn-secondary" href="#/campaigns">Back to campaigns</a>
    </div>
  </div>
{:else if !campaign}
  <div class="empty-state">
    <h3>Campaign not found</h3>
    <p>This campaign may have been deleted. The alias history it created is gone with it.</p>
    <a class="btn btn-primary" href="#/campaigns">Back to campaigns</a>
  </div>
{:else}
  <div class="campaign-hero reveal" style="--i: 0">
    <a class="back-link" href="#/campaigns">← All campaigns</a>
    <div class="hero-main">
      <div class="hero-copy">
        <p class="eyebrow">Campaign</p>
        <h1 class="page-title hero-title">{campaign.title}</h1>
        <div class="hero-meta">
          {#each tags as tag}<span class="tag">{tag}</span>{/each}
          <span class="hero-stat">{planObjectives.length} objective{planObjectives.length === 1 ? '' : 's'}</span>
          <span class="hero-stat">{completedSteps}/{totalSteps} steps done</span>
          <span class="hero-stat">updated {timeAgo(campaign.updatedAt)}</span>
        </div>
        {#if campaign.intent}
          <div class="hero-intent">
            <Markdown source={campaign.intent} {campaignId} clamp={6} collapsible />
          </div>
        {:else}
          <p class="hero-intent-empty">No intent recorded yet. Add why this project exists so agents start oriented.</p>
        {/if}
      </div>

      <div class="hero-actions">
        {#if refreshing}<span class="sync-note">Syncing…</span>{/if}
        <button type="button" class="btn btn-secondary" onclick={openEditCampaign}>Edit campaign</button>
        <a class="btn btn-secondary" href="#/campaign/{campaignId}/bunker">Bunker</a>
        <button type="button" class="btn btn-primary" onclick={openObjectiveModal}>+ Objective</button>
      </div>
    </div>
  </div>

  <div class="campaign-layout">
    <div class="campaign-main">
      {#if briefing}
        <BriefingPanel {briefing} {campaignId} />
      {/if}

      <section class="panel plan-panel">
        <header class="panel-head">
          <div class="panel-heading">
            <p class="eyebrow">The plan</p>
            <h2 class="panel-title">Objectives &amp; steps</h2>
            <p class="panel-subtitle">
              A plan tree, not columns. Dependencies decide what is ready. Drafts stay parked.
            </p>
          </div>
          <div class="panel-head-actions">
            <span class="inbox-count">{planObjectives.length}</span>
            <button type="button" class="btn btn-ghost btn-sm" onclick={openObjectiveModal}>+ Objective</button>
          </div>
        </header>

        {#if planObjectives.length === 0}
          <div class="calm-state">
            <span class="calm-mark" aria-hidden="true">◇</span>
            <div>
              <h3>No objectives yet.</h3>
              <p>Add the first durable outcome, then break it into steps agents can pull.</p>
              <button type="button" class="btn btn-primary btn-sm" onclick={openObjectiveModal}>Add an objective</button>
            </div>
          </div>
        {:else}
          <div class="plan-tree">
            {#each planObjectives as objective, index (objective.id)}
              {@const progress = stepProgress(objective.steps)}
              {@const isOpen = !!expanded[objective.id]}
              <article class="objective-node" class:is-draft={objective.planState === 'Draft'}>
                <div class="objective-node-line" aria-hidden="true">
                  <span class="node-dot"></span>
                </div>

                <div class="objective-row">
                  <div class="objective-main">
                    <div class="objective-title-line">
                      <span class="ref-chip large">#{objective.alias}</span>
                      <a class="objective-title" href={objectiveHref(objective)}>{objective.title}</a>
                    </div>
                    <div class="objective-badges">
                      <StatusBadge status={objective.planState} sm />
                      <StatusBadge status={objective.status || 'planned'} sm />
                      {#if progress.flagged > 0}
                        <span class="chip chip-flag">{progress.flagged} flagged</span>
                      {/if}
                      {#if progress.blocked > 0}
                        <span class="chip chip-blocked">{progress.blocked} blocked</span>
                      {/if}
                      {#if objective.planState === 'Draft'}
                        <span class="calm-note">Parked · not in the ready set</span>
                      {/if}
                    </div>
                  </div>

                  <div class="objective-actions">
                    <button
                      type="button"
                      class="btn btn-ghost btn-icon btn-sm"
                      onclick={() => moveObjective(objective, -1)}
                      disabled={index === 0}
                      title="Move up"
                      aria-label="Move objective up"
                    >↑</button>
                    <button
                      type="button"
                      class="btn btn-ghost btn-icon btn-sm"
                      onclick={() => moveObjective(objective, 1)}
                      disabled={index === planObjectives.length - 1}
                      title="Move down"
                      aria-label="Move objective down"
                    >↓</button>
                    <button
                      type="button"
                      class="btn btn-ghost btn-icon btn-sm"
                      onclick={() => openObjectiveEdit(objective)}
                      title="Edit objective"
                      aria-label="Edit objective"
                    >✎</button>
                    {#if objective.planState === 'Draft'}
                      <button type="button" class="btn btn-secondary btn-sm" onclick={() => setPlanState(objective, 'Active')}>
                        Activate
                      </button>
                    {:else}
                      <button type="button" class="btn btn-ghost btn-sm" onclick={() => setPlanState(objective, 'Draft')}>
                        Park
                      </button>
                    {/if}
                  </div>
                </div>

                <div class="objective-progress-row">
                  <div class="progress-track slim" aria-hidden="true">
                    <span class="progress-fill" style="width: {progress.pct}%"></span>
                  </div>
                  <span class="progress-numbers">
                    {progress.done}/{progress.total} steps
                  </span>
                  <button
                    type="button"
                    class="text-toggle"
                    aria-expanded={isOpen}
                    onclick={() => toggleExpanded(objective.id)}
                  >
                    {isOpen ? 'Hide steps' : `Show ${progress.total} step${progress.total === 1 ? '' : 's'}`}
                  </button>
                  <a class="text-link objective-open" href={objectiveHref(objective)}>Open objective →</a>
                </div>

                {#if isOpen}
                  <div class="step-cluster">
                    {#if !objective.steps || objective.steps.length === 0}
                      <p class="micro-empty">No steps yet. Open the objective to add the first atomic unit of work.</p>
                    {:else}
                      {#each objective.steps as step (step.id)}
                        {@const latest = latestUpdate(step)}
                        {@const deps = (step.dependsOn || []).filter((dep) => dep.status !== 'done')}
                        <div class="step-line" class:is-flagged={step.status === 'flagged'}>
                          <div class="step-line-top">
                            <span class="step-ref">#{objective.alias}.{step.stepNumber}</span>
                            <a class="step-line-title" href={objectiveHref(objective)}>{step.title}</a>
                            <StatusBadge status={step.status} sm />
                            <span class="step-line-spacer"></span>
                            <button
                              type="button"
                              class="btn btn-ghost btn-icon btn-xs"
                              class:flag-on={step.status === 'flagged'}
                              onclick={() => toggleFlag(step)}
                              title={step.status === 'flagged' ? 'Clear flag' : 'Flag for human attention'}
                              aria-label={step.status === 'flagged' ? 'Clear flag' : 'Flag step'}
                            >!</button>
                          </div>
                          <div class="step-line-meta">
                            {#if step.assignee}
                              <span class="assignee">@{actorLabel(step.assignee)}</span>
                            {/if}
                            {#if deps.length > 0}
                              <span class="dep-inline">
                                waits on
                                {#each deps as dep (dep.stepId)}
                                  <span class="dep-chip is-pending">#{dep.objectiveAlias}.{dep.stepNumber}</span>
                                {/each}
                              </span>
                            {:else if step.status === 'open' && objective.planState === 'Active'}
                              <span class="chip chip-ready">ready</span>
                            {/if}
                            {#if step.updatedAt}<span title={step.updatedAt}>updated {timeAgo(step.updatedAt)}</span>{/if}
                          </div>
                          {#if latest}
                            <p class="step-line-latest">
                              <StatusBadge status={latest.kind} sm tone={latest.kind === 'Blocker' ? 'blocked' : latest.kind === 'Result' ? 'done' : 'neutral'} />
                              <span>{plainText(latest.body, 150)}</span>
                              <span class="latest-time">{timeAgo(latest.createdAt)}</span>
                            </p>
                          {/if}
                        </div>
                      {/each}
                    {/if}
                  </div>
                {/if}
              </article>
            {/each}
          </div>
        {/if}
      </section>
    </div>

    <aside class="campaign-rail">
      <section class="panel rail-panel">
        <header class="panel-head compact">
          <div class="panel-heading">
            <p class="eyebrow">Knowledge</p>
            <h2 class="panel-title">Bunker</h2>
          </div>
          <a class="text-link" href="#/campaign/{campaignId}/bunker">Open →</a>
        </header>

        <label class="search-field compact">
          <span class="search-glyph" aria-hidden="true">⌕</span>
          <span class="sr-only">Search Bunker notes</span>
          <input type="search" placeholder="Search notes…" bind:value={railSearch} />
        </label>

        {#if pinnedDirectives.length > 0}
          <div class="rail-zone">
            <p class="zone-label">Pinned directives</p>
            <div class="directive-stack">
              {#each pinnedDirectives as note (note.id)}
                <a class="directive-card compact" href={noteHref(note)}>
                  <span class="directive-top">
                    <span class="ref-chip">#{note.alias}</span>
                    <span class="pin-mark" title="Pinned directive">◆</span>
                  </span>
                  <span class="directive-title">{note.title}</span>
                  <span class="directive-body">{plainText(note.body, 95)}</span>
                </a>
              {/each}
            </div>
          </div>
        {/if}

        <div class="rail-zone">
          <p class="zone-label">{railSearch ? 'Matches' : 'Recent notes'}</p>
          {#if railNotes.length === 0}
            <p class="micro-empty">
              {railSearch ? 'No notes match that search.' : 'The Bunker is empty.'}
            </p>
          {:else}
            <ul class="rail-list">
              {#each railNotes as note (note.id)}
                <li>
                  <a class="rail-item" href={noteHref(note)}>
                    <span class="rail-item-top">
                      <span class="ref-chip">#{note.alias}</span>
                      <StatusBadge status={note.kind} sm />
                    </span>
                    <span class="rail-item-title">{note.title}</span>
                    <span class="rail-item-sub">{timeAgo(note.updatedAt)} · {plainText(note.body, 60)}</span>
                  </a>
                </li>
              {/each}
            </ul>
          {/if}
        </div>

        <a class="btn btn-secondary btn-sm w-full" href="#/campaign/{campaignId}/bunker">Search the Bunker</a>
      </section>

      <section class="panel rail-panel quiet-panel">
        <header class="panel-head compact">
          <div class="panel-heading">
            <p class="eyebrow">Steering</p>
            <h2 class="panel-title">Your preferences</h2>
          </div>
          <a class="text-link" href="#/preferences">Manage →</a>
        </header>
        {#if briefing?.preferences?.length}
          <ul class="pref-rail-list">
            {#each briefing.preferences.slice(0, 3) as pref (pref.id)}
              <li class="pref-rail-item">
                <span class="pref-mark" aria-hidden="true">✦</span>
                <span class="pref-rail-text">{pref.text}</span>
              </li>
            {/each}
          </ul>
        {:else}
          <p class="inline-empty">No active preferences.</p>
        {/if}
      </section>
    </aside>
  </div>
{/if}

{#if editingCampaign}
  <div
    class="modal-overlay"
    role="presentation"
    onclick={(e) => { if (e.target === e.currentTarget) editingCampaign = false; }}
  >
    <div
      class="modal modal-wide"
      role="dialog"
      aria-modal="true"
      aria-labelledby="edit-campaign-title"
      tabindex="-1"
      onclick={(e) => e.stopPropagation()}
      onkeydown={(e) => { if (e.key === 'Escape') editingCampaign = false; }}
    >
      <form onsubmit={saveCampaign}>
        <div class="modal-header">
          <div>
            <p class="eyebrow">Campaign</p>
            <h2 id="edit-campaign-title">Edit campaign</h2>
          </div>
          <button type="button" class="modal-close" onclick={() => (editingCampaign = false)} aria-label="Close">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label class="form-label" for="edit-campaign-title-input">Title</label>
            <input id="edit-campaign-title-input" class="form-input" type="text" bind:value={campaignForm.title} required />
          </div>
          <div class="form-group">
            <label class="form-label" for="edit-campaign-intent">Intent <span class="optional">(markdown)</span></label>
            <MarkdownEditor id="edit-campaign-intent" bind:value={campaignForm.intent} {campaignId} rows={5} />
          </div>
          <div class="form-group">
            <label class="form-label" for="edit-campaign-tags">Tags <span class="optional">(comma separated)</span></label>
            <input id="edit-campaign-tags" class="form-input" type="text" bind:value={campaignForm.tags} />
          </div>
        </div>
        <div class="modal-actions spread">
          <button type="button" class="btn btn-danger" onclick={deleteCampaign}>Delete campaign</button>
          <span class="modal-actions-group">
            <button type="button" class="btn btn-ghost" onclick={() => (editingCampaign = false)}>Cancel</button>
            <button type="submit" class="btn btn-primary" disabled={campaignSaving}>
              {campaignSaving ? 'Saving…' : 'Save campaign'}
            </button>
          </span>
        </div>
      </form>
    </div>
  </div>
{/if}

{#if showObjectiveModal}
  <div
    class="modal-overlay"
    role="presentation"
    onclick={(e) => { if (e.target === e.currentTarget) showObjectiveModal = false; }}
  >
    <div
      class="modal modal-wide"
      role="dialog"
      aria-modal="true"
      aria-labelledby="new-objective-title"
      tabindex="-1"
      onclick={(e) => e.stopPropagation()}
      onkeydown={(e) => { if (e.key === 'Escape') showObjectiveModal = false; }}
    >
      <form onsubmit={createObjective}>
        <div class="modal-header">
          <div>
            <p class="eyebrow">New outcome</p>
            <h2 id="new-objective-title">Add an objective</h2>
          </div>
          <button type="button" class="modal-close" onclick={() => (showObjectiveModal = false)} aria-label="Close">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label class="form-label" for="new-objective-title-input">Title</label>
            <input id="new-objective-title-input" class="form-input" type="text" bind:value={objectiveForm.title} required />
          </div>
          <div class="form-group">
            <label class="form-label" for="new-objective-description">Description <span class="optional">(markdown)</span></label>
            <MarkdownEditor id="new-objective-description" bind:value={objectiveForm.description} {campaignId} rows={4} />
          </div>
          <div class="form-group">
            <label class="form-label" for="new-objective-state">Plan state</label>
            <select id="new-objective-state" class="form-select" bind:value={objectiveForm.planState}>
              <option value="Draft">Draft — parked, not actionable</option>
              <option value="Active">Active — included in the ready set</option>
            </select>
          </div>
        </div>
        <div class="modal-actions">
          <button type="button" class="btn btn-ghost" onclick={() => (showObjectiveModal = false)}>Cancel</button>
          <button type="submit" class="btn btn-primary" disabled={objectiveSaving}>
            {objectiveSaving ? 'Creating…' : 'Create objective'}
          </button>
        </div>
      </form>
    </div>
  </div>
{/if}

{#if editingObjective}
  <div
    class="modal-overlay"
    role="presentation"
    onclick={(e) => { if (e.target === e.currentTarget) editingObjective = null; }}
  >
    <div
      class="modal modal-wide"
      role="dialog"
      aria-modal="true"
      aria-labelledby="edit-objective-inline-title"
      tabindex="-1"
      onclick={(e) => e.stopPropagation()}
      onkeydown={(e) => { if (e.key === 'Escape') editingObjective = null; }}
    >
      <form onsubmit={saveObjectiveEdit}>
        <div class="modal-header">
          <div>
            <p class="eyebrow">#{editingObjective.alias}</p>
            <h2 id="edit-objective-inline-title">Edit objective</h2>
          </div>
          <button type="button" class="modal-close" onclick={() => (editingObjective = null)} aria-label="Close">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label class="form-label" for="inline-objective-title">Title</label>
            <input
              id="inline-objective-title"
              class="form-input"
              type="text"
              bind:value={objectiveEditForm.title}
              required
            />
          </div>
          <div class="form-group">
            <label class="form-label" for="inline-objective-description">Description <span class="optional">(markdown)</span></label>
            <MarkdownEditor
              id="inline-objective-description"
              bind:value={objectiveEditForm.description}
              {campaignId}
              rows={5}
            />
          </div>
          <div class="form-group">
            <label class="form-label" for="inline-objective-state">Plan state</label>
            <select id="inline-objective-state" class="form-select" bind:value={objectiveEditForm.planState}>
              <option value="Draft">Draft — parked, not actionable</option>
              <option value="Active">Active — included in the ready set</option>
            </select>
          </div>
        </div>
        <div class="modal-actions">
          <button type="button" class="btn btn-ghost" onclick={() => (editingObjective = null)}>Cancel</button>
          <button type="submit" class="btn btn-primary" disabled={objectiveEditSaving}>
            {objectiveEditSaving ? 'Saving…' : 'Save objective'}
          </button>
        </div>
      </form>
    </div>
  </div>
{/if}
