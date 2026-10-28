// Shared runes module for alias popover state.
// AliasText.svelte and Markdown.svelte both drive this.
// Mount <AliasPopover /> once in App.svelte.

import { apiGet } from './api.js';

// Exported state object. Components read `aliasPopoverState.popover`;
// drivers mutate its properties (never reassign the object itself).
export const aliasPopoverState = $state({
  popover: null,
});

const HALF_MAX_WIDTH = 140;

function clampX(x) {
  if (typeof window === 'undefined') return x;
  return Math.min(
    Math.max(x, HALF_MAX_WIDTH),
    window.innerWidth - HALF_MAX_WIDTH,
  );
}

/**
 * Open the alias popover at a given anchor position.
 * @param {number} alias The alias number
 * @param {DOMRect} rect Bounding rect of the clicked element
 * @param {string} campaignId
 */
export async function openAliasPopover(alias, rect, campaignId) {
  if (!campaignId) return;

  const x = clampX(rect.left + rect.width / 2);

  aliasPopoverState.popover = {
    alias,
    kind: null,
    title: null,
    lane: null,
    deleted: false,
    x,
    anchorTop: rect.top,
    anchorBottom: rect.bottom,
    loading: true,
  };

  try {
    const data = await apiGet('/api/objects/resolve', { campaignId, alias });
    aliasPopoverState.popover = {
      alias,
      kind: data.kind,
      title: data.title,
      lane: data.lane,
      deleted: data.deleted,
      x,
      anchorTop: rect.top,
      anchorBottom: rect.bottom,
    };
  } catch (err) {
    aliasPopoverState.popover = {
      alias,
      kind: 'unknown',
      title: 'Could not resolve',
      lane: null,
      deleted: false,
      x,
      anchorTop: rect.top,
      anchorBottom: rect.bottom,
      error: true,
    };
  }
}

export function closeAliasPopover() {
  aliasPopoverState.popover = null;
}