<script>
  import { toasts, removeToast } from '../lib/stores.js';
</script>

<div class="toast-container">
  {#each $toasts as toast (toast.id)}
    <div class="toast toast-{toast.type}" role="alert">
      <span>{toast.message}</span>
      <button class="toast-close" onclick={() => removeToast(toast.id)}>×</button>
    </div>
  {/each}
</div>

<style>
  .toast-container {
    position: fixed;
    bottom: 20px;
    right: 20px;
    z-index: 10000;
    display: flex;
    flex-direction: column;
    gap: 8px;
    max-width: 380px;
  }
  .toast {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
    padding: 12px 16px;
    border-radius: 6px;
    font-size: 14px;
    line-height: 1.4;
    color: #fff;
    box-shadow: 0 4px 12px rgba(0,0,0,0.25);
    animation: toast-in 0.25s ease-out;
  }
  .toast-info { background: #5b7a5a; }
  .toast-success { background: #3a7a4a; }
  .toast-warning { background: #b8860b; }
  .toast-error { background: #a33; }
  .toast-close {
    background: none;
    border: none;
    color: inherit;
    font-size: 18px;
    line-height: 1;
    cursor: pointer;
    opacity: 0.8;
    padding: 0 2px;
  }
  .toast-close:hover { opacity: 1; }
  @keyframes toast-in {
    from { transform: translateX(100%); opacity: 0; }
    to { transform: translateX(0); opacity: 1; }
  }
</style>