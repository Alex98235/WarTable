<script>
  import Markdown from './Markdown.svelte';

  let {
    value = $bindable(''),
    campaignId = '',
    placeholder = '',
    id = '',
    rows = 5,
    minHeight = '80px',
    fill = false,
  } = $props();

  let mode = $state('write');
</script>

<div class="md-editor" class:md-editor-fill={fill}>
  <div class="md-editor-toggle" role="radiogroup" aria-label="Editor mode">
    <button
      type="button"
      class="md-seg {mode === 'write' ? 'md-seg-active' : ''}"
      role="radio"
      aria-checked={mode === 'write'}
      onclick={() => (mode = 'write')}
    >Write</button>
    <button
      type="button"
      class="md-seg {mode === 'preview' ? 'md-seg-active' : ''}"
      role="radio"
      aria-checked={mode === 'preview'}
      onclick={() => (mode = 'preview')}
    >Preview</button>
  </div>

  {#if mode === 'write'}
    <textarea
      id={id}
      class="form-textarea"
      {placeholder}
      rows={fill ? 2 : rows}
      style="min-height: {fill ? '0' : minHeight};"
      bind:value
    ></textarea>
  {:else}
    <div class="md-editor-preview" style="min-height: {fill ? '0' : minHeight};">
      {#if value && value.trim()}
        <Markdown source={value} {campaignId} />
      {:else}
        <p class="text-muted">Nothing to preview yet.</p>
      {/if}
    </div>
  {/if}
</div>

<style>
  .md-editor {
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .md-editor-toggle {
    display: inline-flex;
    align-self: flex-start;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    overflow: hidden;
  }
  .md-seg {
    padding: 4px 14px;
    font-size: 0.78rem;
    font-weight: 500;
    font-family: var(--font-stack);
    border: none;
    background: transparent;
    color: var(--text-muted);
    cursor: pointer;
    transition: all 0.12s ease;
    line-height: 1.5;
  }
  .md-seg:hover {
    background: var(--bg-hover);
  }
  .md-seg + .md-seg {
    border-left: 1px solid var(--border-color);
  }
  .md-seg-active {
    background: var(--color-olive);
    color: var(--text-inverse);
    font-weight: 600;
  }
  .md-seg-active:hover {
    background: var(--color-olive);
    color: var(--text-inverse);
  }
  .md-editor-preview {
    padding: 14px 16px;
    border: 1px solid var(--border-color);
    border-radius: var(--radius-sm);
    background: var(--bg-surface);
  }
  /* ── Fill/grow mode ── */
  .md-editor-fill {
    flex: 1;
    min-height: 0;
  }
  .md-editor-fill .form-textarea,
  .md-editor-fill .md-editor-preview {
    flex: 1;
    min-height: 0;
  }
  .md-editor-fill .form-textarea {
    resize: none;
  }
</style>