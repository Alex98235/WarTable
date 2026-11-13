<script>
	import { confirmState, closeConfirm } from '../lib/stores.js';

	// A hold turns the confirm into a deliberate act: the button stays disabled
	// while a short countdown runs, so a double-click or a keyboard repeat cannot
	// land an irreversible action. The deadline lives on the state that opened
	// the dialog, so re-rendering never restarts it.
	let now = $state(Date.now());
	let remaining = $derived.by(() => {
		const state = $confirmState;
		if (!state?.holdMs || !state.openedAt) return 0;
		return Math.max(0, Math.ceil((state.openedAt + state.holdMs - now) / 1000));
	});
	let held = $derived(($confirmState?.holdMs ?? 0) > 0 && remaining > 0);
	let label = $derived($confirmState?.confirmLabel ?? 'Delete');

	$effect(() => {
		if (!$confirmState?.holdMs) return;
		const id = setInterval(() => (now = Date.now()), 250);
		return () => clearInterval(id);
	});

	function confirm() {
		if (held) return;
		if ($confirmState) {
			$confirmState.onConfirm();
		}
		closeConfirm();
	}
</script>

{#if $confirmState}
	<div
		class="modal-overlay"
		onclick={(e) => {
			if (e.target === e.currentTarget) closeConfirm();
		}}
		onkeydown={(e) => {
			if (e.key === 'Escape') closeConfirm();
		}}
		role="presentation"
	>
		<div
			class="modal confirm-modal"
			role="alertdialog"
			aria-modal="true"
			aria-labelledby="confirm-message"
		>
			<div class="modal-body">
				<p class="eyebrow">Confirm</p>
				<p id="confirm-message" class="confirm-message">{$confirmState.message}</p>
			</div>
			<div class="modal-actions">
				<button class="btn btn-ghost" onclick={closeConfirm}>Cancel</button>
				<button
					class="btn {$confirmState.tone === 'primary' ? 'btn-primary' : 'btn-danger'}"
					onclick={confirm}
					disabled={held}
				>
					{held ? `${label} (${remaining})` : label}
				</button>
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
