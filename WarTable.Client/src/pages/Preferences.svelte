<script>
  import { ACTOR, apiGet, apiPost, apiPut, parseTags, timeAgo } from '../lib/api.js';
  import { addToast } from '../lib/stores.js';
  import { onActivity } from '../lib/events.svelte.js';

  let preferences = $state([]);
  let loading = $state(true);
  let refreshing = $state(false);
  let error = $state('');
  let loadToken = 0;

  let form = $state({ text: '', rationale: '', tags: '', source: 'human' });
  let saving = $state(false);

  let editing = $state(null);
  let editForm = $state({ text: '', rationale: '', tags: '' });
  let editSaving = $state(false);

  let active = $derived(preferences.filter((p) => p.active));
  let retired = $derived(preferences.filter((p) => !p.active));

  async function load({ silent = false } = {}) {
    if (silent) refreshing = true;
    else loading = true;
    error = '';
    const token = ++loadToken;
    try {
      const data = await apiGet('/api/preferences', { includeInactive: 'true' });
      if (token !== loadToken) return;
      preferences = Array.isArray(data) ? data : [];
    } catch (err) {
      if (token !== loadToken) return;
      error = err.message || 'Could not load preferences.';
      addToast('Could not load preferences.', 'error');
    } finally {
      if (token === loadToken) {
        loading = false;
        refreshing = false;
      }
    }
  }

  async function capture(event) {
    event.preventDefault();
    if (!form.text.trim()) {
      addToast('Write the preference first.', 'warning');
      return;
    }
    saving = true;
    try {
      await apiPost('/api/preferences', {
        text: form.text.trim(),
        rationale: form.rationale.trim(),
        tags: parseTags(form.tags).join(', '),
        source: form.source,
      });
      form = { text: '', rationale: '', tags: '', source: 'human' };
      addToast('Preference captured. It now applies to every campaign.', 'success');
      await load({ silent: true });
    } catch (err) {
      addToast(err.message || 'Could not capture the preference.', 'error');
    } finally {
      saving = false;
    }
  }

  function startEdit(pref) {
    editing = pref;
    editForm = {
      text: pref.text || '',
      rationale: pref.rationale || '',
      tags: pref.tags || '',
    };
  }

  async function saveEdit(event) {
    event.preventDefault();
    if (!editing) return;
    if (!editForm.text.trim()) {
      addToast('A preference needs text.', 'warning');
      return;
    }
    editSaving = true;
    try {
      await apiPut(`/api/preferences/${editing.id}`, {
        text: editForm.text.trim(),
        rationale: editForm.rationale.trim(),
        tags: parseTags(editForm.tags).join(', '),
      });
      addToast('Preference updated.', 'success');
      editing = null;
      await load({ silent: true });
    } catch (err) {
      addToast(err.message || 'Could not update the preference.', 'error');
    } finally {
      editSaving = false;
    }
  }

  async function setActive(pref, nextActive) {
    try {
      await apiPut(`/api/preferences/${pref.id}`, { active: nextActive });
      addToast(nextActive ? 'Preference reactivated.' : 'Preference retired. Its history is kept.', 'success');
      await load({ silent: true });
    } catch (err) {
      addToast(err.message || 'Could not change the preference.', 'error');
    }
  }

  function sourceLabel(source) {
    if (!source) return 'Unknown';
    if (source === 'human') return 'Human';
    if (source === 'agent') return 'Agent';
    return source;
  }

  load();

  $effect(() => {
    return onActivity(() => load({ silent: true }));
  });
</script>

<svelte:head>
  <title>WarTable — Preferences</title>
</svelte:head>

<div class="page-intro reveal" style="--i: 0">
  <div>
    <p class="eyebrow">Across every campaign</p>
    <h1 class="page-title">Preferences</h1>
    <p class="lede">
      Soft rules that follow you into every campaign. Retire rather than delete: retired preferences keep
      their provenance. A campaign directive overrides a conflicting preference, and the agent should say so.
    </p>
  </div>
  <div class="page-actions">
    {#if refreshing}<span class="sync-note">Syncing…</span>{/if}
    <span class="stat-inline"><strong>{active.length}</strong> active</span>
    <span class="stat-inline"><strong>{retired.length}</strong> retired</span>
  </div>
</div>

{#if loading}
  <div class="loading-state">
    <div class="loading-spinner"></div>
    <p>Reading your preferences…</p>
  </div>
{:else if error}
  <div class="error-state">
    <p class="eyebrow">Connection trouble</p>
    <h1>Preferences could not be read.</h1>
    <p class="lede">{error}</p>
    <button type="button" class="btn btn-primary" onclick={() => load()}>Try again</button>
  </div>
{:else}
  <div class="prefs-layout">
    <div class="prefs-main">
      <section class="panel reveal" style="--i: 1">
        <header class="panel-head">
          <div class="panel-heading">
            <p class="eyebrow">Always in play</p>
            <h2 class="panel-title">Active preferences</h2>
            <p class="panel-subtitle">Injected into every briefing, in every campaign.</p>
          </div>
          <span class="inbox-count">{active.length}</span>
        </header>

        {#if active.length === 0}
          <div class="calm-state">
            <span class="calm-mark" aria-hidden="true">✦</span>
            <div>
              <h3>No active preferences.</h3>
              <p>Capture one on the right the next time a durable preference comes up in conversation.</p>
            </div>
          </div>
        {:else}
          <ul class="pref-list">
            {#each active as pref (pref.id)}
              <li class="pref-card">
                <div class="pref-card-main">
                  <p class="pref-text">{pref.text}</p>
                  {#if pref.rationale}<p class="pref-rationale">{pref.rationale}</p>{/if}
                  <div class="pref-meta">
                    {#each parseTags(pref.tags) as tag}
                      <span class="tag">{tag}</span>
                    {/each}
                    <span class="pref-source">From {sourceLabel(pref.source)}</span>
                    <span class="pref-time" title={pref.updatedAt}>updated {timeAgo(pref.updatedAt)}</span>
                  </div>
                </div>
                <div class="pref-actions">
                  <button type="button" class="btn btn-ghost btn-sm" onclick={() => startEdit(pref)}>Edit</button>
                  <button type="button" class="btn btn-ghost btn-sm" onclick={() => setActive(pref, false)}>Retire</button>
                </div>
              </li>
            {/each}
          </ul>
        {/if}
      </section>

      {#if retired.length > 0}
        <section class="panel retired-panel reveal" style="--i: 2">
          <header class="panel-head">
            <div class="panel-heading">
              <p class="eyebrow">History</p>
              <h2 class="panel-title">Retired preferences</h2>
              <p class="panel-subtitle">Kept for provenance. Reactivate if the rule comes back.</p>
            </div>
            <span class="inbox-count">{retired.length}</span>
          </header>
          <ul class="pref-list">
            {#each retired as pref (pref.id)}
              <li class="pref-card is-retired">
                <div class="pref-card-main">
                  <p class="pref-text">{pref.text}</p>
                  {#if pref.rationale}<p class="pref-rationale">{pref.rationale}</p>{/if}
                  <div class="pref-meta">
                    {#each parseTags(pref.tags) as tag}
                      <span class="tag">{tag}</span>
                    {/each}
                    <span class="pref-source">From {sourceLabel(pref.source)}</span>
                    <span class="pref-time">retired</span>
                  </div>
                </div>
                <div class="pref-actions">
                  <button type="button" class="btn btn-ghost btn-sm" onclick={() => setActive(pref, true)}>Reactivate</button>
                </div>
              </li>
            {/each}
          </ul>
        </section>
      {/if}
    </div>

    <aside class="prefs-rail">
      <section class="panel sticky-panel reveal" style="--i: 2">
        <header class="panel-head compact">
          <div class="panel-heading">
            <p class="eyebrow">Capture</p>
            <h2 class="panel-title">A durable preference</h2>
          </div>
        </header>
        <p class="panel-subtitle">
          If a passing remark should shape future work, record it here with the reason behind it.
        </p>
        <form class="pref-form" onsubmit={capture}>
          <div class="form-group">
            <label class="form-label" for="pref-text">Preference</label>
            <textarea
              id="pref-text"
              class="form-textarea"
              rows="3"
              bind:value={form.text}
              placeholder="e.g. Never use newlined braces."
            ></textarea>
          </div>
          <div class="form-group">
            <label class="form-label" for="pref-rationale">Rationale <span class="optional">(why, or where it came from)</span></label>
            <textarea
              id="pref-rationale"
              class="form-textarea"
              rows="2"
              bind:value={form.rationale}
              placeholder="e.g. Faster to scan in diffs."
            ></textarea>
          </div>
          <div class="form-group">
            <label class="form-label" for="pref-tags">Tags</label>
            <input
              id="pref-tags"
              class="form-input"
              type="text"
              bind:value={form.tags}
              placeholder="style, reviews"
            />
          </div>
          <div class="form-group">
            <label class="form-label" for="pref-source">Source</label>
            <select id="pref-source" class="form-select" bind:value={form.source}>
              <option value="human">Human</option>
              <option value="agent">Agent</option>
            </select>
          </div>
          <button type="submit" class="btn btn-primary w-full" disabled={saving}>
            {saving ? 'Capturing…' : 'Capture preference'}
          </button>
        </form>
      </section>

      <p class="rail-note">
        Preferences are advisory, not blocking. No accounts: this instance is for one human, and
        provenance is recorded on every change.
      </p>
    </aside>
  </div>
{/if}

{#if editing}
  <div
    class="modal-overlay"
    role="presentation"
    onclick={(e) => { if (e.target === e.currentTarget) editing = null; }}
  >
    <div
      class="modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="edit-pref-title"
      tabindex="-1"
      onclick={(e) => e.stopPropagation()}
      onkeydown={(e) => { if (e.key === 'Escape') editing = null; }}
    >
      <form onsubmit={saveEdit}>
        <div class="modal-header">
          <div>
            <p class="eyebrow">Edit preference</p>
            <h2 id="edit-pref-title">Refine the rule</h2>
          </div>
          <button type="button" class="modal-close" onclick={() => (editing = null)} aria-label="Close">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label class="form-label" for="edit-pref-text">Preference</label>
            <textarea id="edit-pref-text" class="form-textarea" rows="3" bind:value={editForm.text}></textarea>
          </div>
          <div class="form-group">
            <label class="form-label" for="edit-pref-rationale">Rationale</label>
            <textarea id="edit-pref-rationale" class="form-textarea" rows="2" bind:value={editForm.rationale}></textarea>
          </div>
          <div class="form-group">
            <label class="form-label" for="edit-pref-tags">Tags <span class="optional">(comma separated)</span></label>
            <input id="edit-pref-tags" class="form-input" type="text" bind:value={editForm.tags} />
          </div>
        </div>
        <div class="modal-actions">
          <button type="button" class="btn btn-ghost" onclick={() => (editing = null)}>Cancel</button>
          <button type="submit" class="btn btn-primary" disabled={editSaving}>
            {editSaving ? 'Saving…' : 'Save preference'}
          </button>
        </div>
      </form>
    </div>
  </div>
{/if}
