<script>
  /**
   * Status, plan-state, note-kind, and update-kind badges.
   * Props:
   *   status  - raw enum/status value from the API
   *   label   - optional display override
   *   sm      - small variant
   *   tone    - optional explicit badge tone class suffix
   */
  let { status = '', label = undefined, sm = false, tone = null } = $props();

  const toneByKey = {
    planned: 'planned',
    'in-progress': 'progress',
    inprogress: 'progress',
    'needs-review': 'review',
    blocked: 'blocked',
    completed: 'done',
    open: 'open',
    done: 'done',
    flagged: 'flagged',
    draft: 'draft',
    active: 'active',
    superseded: 'muted',
    resolved: 'done',
    // Bunker note kinds
    directive: 'directive',
    decision: 'decision',
    gotcha: 'gotcha',
    reference: 'reference',
    context: 'context',
    question: 'question',
    // Step update kinds
    note: 'muted',
    blocker: 'blocked',
    result: 'done',
    handoff: 'review',
    // Evidence kinds
    link: 'reference',
    file: 'context',
    commit: 'decision',
    test: 'done',
    screenshot: 'directive',
    other: 'muted',
  };

  const labelByKey = {
    'in-progress': 'In progress',
    'needs-review': 'Needs review',
    inprogress: 'In progress',
  };

  function keyOf(value) {
    return String(value ?? '').toLowerCase();
  }

  let key = $derived(keyOf(status));
  let badgeClass = $derived(
    tone ? `badge-${tone}` : `badge-${toneByKey[key] || 'neutral'}`,
  );
  let display = $derived(
    label ??
      labelByKey[key] ??
      (status
        ? String(status).charAt(0).toUpperCase() + String(status).slice(1)
        : '—'),
  );
</script>

<span class="badge {badgeClass}" class:badge-sm={sm}>{display}</span>
