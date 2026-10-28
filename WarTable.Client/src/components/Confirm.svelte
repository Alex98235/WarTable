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
  <div class="modal-overlay" onclick={(e) => { if (e.target === e.currentTarget) closeConfirm(); }} onkeydown={(e) => { if (e.key === "Escape") closeConfirm(); }} role="dialog" tabindex="-1">
    <div class="modal" role="document">
      <p>{$confirmState.message}</p>
      <div class="modal-actions">
        <button class="btn btn-ghost" onclick={closeConfirm}>Cancel</button>
        <button class="btn btn-danger" onclick={confirm}>Delete</button>
      </div>
    </div>
  </div>
{/if}