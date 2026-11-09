<script>
  import { confirmState, closeConfirm } from '../lib/stores.js';

  function confirm() {
    if ($confirmState) {
      $confirmState.onConfirm();
    }
    closeConfirm();
  }
</script>

{#if $confirmState}
  <div
    class="modal-overlay"
    onclick={(e) => { if (e.target === e.currentTarget) closeConfirm(); }}
    onkeydown={(e) => { if (e.key === 'Escape') closeConfirm(); }}
    role="presentation"
  >
    <div class="modal confirm-modal" role="alertdialog" aria-modal="true" aria-labelledby="confirm-message">
      <div class="modal-body">
        <p class="eyebrow">Confirm</p>
        <p id="confirm-message" class="confirm-message">{$confirmState.message}</p>
      </div>
      <div class="modal-actions">
        <button class="btn btn-ghost" onclick={closeConfirm}>Cancel</button>
        <button class="btn btn-danger" onclick={confirm}>Delete</button>
      </div>
    </div>
  </div>
{/if}

<style>
  .confirm-modal {
    max-width: 460px;
  }
  .confirm-message {
    font-size: 0.95rem;
    line-height: 1.55;
    color: var(--ink);
  }
</style>
