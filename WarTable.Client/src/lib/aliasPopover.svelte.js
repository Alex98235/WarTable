// Shared runes module for the alias popover. AliasText.svelte, Markdown.svelte,
// and the reference links all drive this. Mount <AliasPopover /> once in App.
//
// Resolves #N through /api/aliases/resolve, which covers objectives and Bunker
// notes (including soft-deleted objects).

import { apiGet } from './api.js';

export const aliasPopoverState = $state({
	popover: null
});

const HALF_MAX_WIDTH = 160;

function clampX(x) {
	if (typeof window === 'undefined') return x;
	return Math.min(Math.max(x, HALF_MAX_WIDTH), window.innerWidth - HALF_MAX_WIDTH);
}

function anchor(rect, extra = {}) {
	return {
		x: clampX(rect.left + rect.width / 2),
		anchorTop: rect.top,
		anchorBottom: rect.bottom,
		...extra
	};
}

/**
 * Open the alias popover at a given anchor position.
 * @param {number} alias campaign-scoped alias number
 * @param {DOMRect} rect bounding rect of the clicked element
 * @param {string} campaignId
 */
export async function openAliasPopover(alias, rect, campaignId) {
	if (!campaignId) return;

	aliasPopoverState.popover = anchor(rect, {
		alias,
		campaignId,
		kind: null,
		title: null,
		status: null,
		deleted: false,
		loading: true
	});

	try {
		const data = await apiGet('/api/aliases/resolve', { campaignId, alias });

		let status = null;
		if (data.kind === 'Objective' && data.id) {
			try {
				const objective = await apiGet(`/api/objectives/${data.id}`);
				status = objective.status || null;
			} catch {
				status = null;
			}
		}

		aliasPopoverState.popover = anchor(rect, {
			alias,
			campaignId,
			kind: data.kind,
			id: data.id,
			title: data.title,
			status,
			deleted: data.deleted
		});
	} catch {
		aliasPopoverState.popover = anchor(rect, {
			alias,
			campaignId,
			kind: 'unknown',
			title: 'Nothing resolves to this reference',
			status: null,
			deleted: false,
			error: true
		});
	}
}

export function closeAliasPopover() {
	aliasPopoverState.popover = null;
}
