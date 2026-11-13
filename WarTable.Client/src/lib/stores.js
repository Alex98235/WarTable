// Shared Svelte 5 reactive state for toast notifications and confirm dialogs
import { writable } from 'svelte/store';

// Toast notifications
export const toasts = writable([]);

let toastId = 0;

export function addToast(message, type = 'info') {
	const id = ++toastId;
	toasts.update((t) => [...t, { id, message, type }]);
	setTimeout(() => {
		toasts.update((t) => t.filter((item) => item.id !== id));
	}, 4000);
}

export function removeToast(id) {
	toasts.update((t) => t.filter((item) => item.id !== id));
}

// Confirm dialog
export const confirmState = writable(null);

// Confirm dialog. `options` adds: confirmLabel (button text), tone ('danger' |
// 'primary'), and holdMs (a countdown that keeps the button disabled, for
// actions whose whole cost is that they cannot be undone).
export function showConfirm(message, onConfirm, options = {}) {
	confirmState.set({ message, onConfirm, openedAt: Date.now(), ...options });
}

export function closeConfirm() {
	confirmState.set(null);
}
