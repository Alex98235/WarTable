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

export function showConfirm(message, onConfirm) {
	confirmState.set({ message, onConfirm });
}

export function closeConfirm() {
	confirmState.set(null);
}
