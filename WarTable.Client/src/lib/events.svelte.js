// Shared server-sent-events store.
//
// One EventSource is shared by the whole app. It connects to /api/events with an
// optional campaignId scope and a `since` cursor. The backend starts at the
// current head when `since` is absent, so the first connection only sees new
// activity. Reconnects always resume from the last event `id` we observed.
//
// Views subscribe with onActivity(fn); the store debounces bursts and calls every
// listener so pages can re-fetch after a cross-device change.

const STORAGE_KEY = 'wartable.event-cursors';

/** Reactive connection state, read directly by the shell. */
export const live = $state({
	status: 'idle', // idle | connecting | live | reconnecting
	campaignId: null,
	cursor: null,
	lastEvent: null,
	lastEventAt: null
});

const listeners = new Set();

let source = null;
let activeCampaign = null;
let reconnectTimer = null;
let notifyTimer = null;
let openGraceTimer = null;
let attempts = 0;

function storageKey(campaignId) {
	return campaignId || 'all';
}

function readCursors() {
	try {
		return JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}');
	} catch {
		return {};
	}
}

function writeCursor(campaignId, cursor) {
	try {
		const all = readCursors();
		all[storageKey(campaignId)] = cursor;
		localStorage.setItem(STORAGE_KEY, JSON.stringify(all));
	} catch {
		// Storage can be unavailable (private mode); cursors remain in memory.
	}
}

function clearTimer(timer) {
	if (timer) clearTimeout(timer);
}

function emit(event) {
	live.lastEvent = event;
	live.lastEventAt = Date.now();
	clearTimer(notifyTimer);
	notifyTimer = setTimeout(() => {
		notifyTimer = null;
		for (const fn of [...listeners]) {
			try {
				fn(event);
			} catch (err) {
				console.error('Live activity listener failed', err);
			}
		}
	}, 300);
}

function teardown() {
	clearTimer(reconnectTimer);
	clearTimer(openGraceTimer);
	reconnectTimer = null;
	openGraceTimer = null;
	if (source) {
		source.close();
		source = null;
	}
}

function connect() {
	if (typeof window === 'undefined' || typeof EventSource === 'undefined') return;

	const params = new URLSearchParams();
	if (activeCampaign) params.set('campaignId', activeCampaign);
	if (live.cursor != null) params.set('since', String(live.cursor));
	const query = params.toString();
	const url = `/api/events${query ? `?${query}` : ''}`;

	live.status = attempts === 0 ? 'connecting' : 'reconnecting';

	let es;
	try {
		es = new EventSource(url);
	} catch {
		scheduleReconnect();
		return;
	}
	source = es;

	// The backend flushes SSE headers with the first event or keepalive (up to
	// 15s). Treat a connection with no immediate error as live so the shell does
	// not sit on "Connecting" during a quiet spell.
	clearTimer(openGraceTimer);
	openGraceTimer = setTimeout(() => {
		openGraceTimer = null;
		if (source === es && live.status === 'connecting') live.status = 'live';
	}, 1200);

	es.addEventListener('open', () => {
		clearTimer(openGraceTimer);
		openGraceTimer = null;
		attempts = 0;
		live.status = 'live';
	});

	es.addEventListener('activity', (message) => {
		clearTimer(openGraceTimer);
		openGraceTimer = null;
		attempts = 0;
		live.status = 'live';

		if (message.lastEventId) {
			const seq = Number(message.lastEventId);
			if (Number.isFinite(seq)) {
				live.cursor = seq;
				writeCursor(activeCampaign, seq);
			}
		}

		let payload = null;
		try {
			payload = JSON.parse(message.data);
		} catch {
			// Malformed event payload; emit null.
		}
		emit(payload);
	});

	// The cursor was ahead of the log, so it came from another database: the
	// server has moved us to the real head. Every subscribed view refetches.
	es.addEventListener('resync', (message) => {
		clearTimer(openGraceTimer);
		openGraceTimer = null;
		attempts = 0;
		live.status = 'live';

		const seq = Number(message.lastEventId);
		if (Number.isFinite(seq)) {
			live.cursor = seq;
			writeCursor(activeCampaign, seq);
		}

		emit(null);
	});

	es.addEventListener('error', () => {
		// EventSource retries by itself, but without our `since` parameter. Close it
		// and reconnect manually so the cursor is preserved.
		clearTimer(openGraceTimer);
		openGraceTimer = null;
		live.status = 'reconnecting';
		es.close();
		if (source === es) source = null;
		scheduleReconnect();
	});
}

function scheduleReconnect() {
	attempts += 1;
	const delay = Math.min(30000, 1000 * 2 ** Math.min(attempts, 4));
	clearTimer(reconnectTimer);
	reconnectTimer = setTimeout(() => {
		reconnectTimer = null;
		connect();
	}, delay);
}

/**
 * Start (or re-scope) the shared connection.
 * @param {string|null} campaignId optional campaign scope
 */
export function startLive(campaignId = null) {
	const scope = campaignId || null;
	if (source && activeCampaign === scope) return;

	teardown();
	activeCampaign = scope;
	live.campaignId = scope;
	live.cursor = readCursors()[storageKey(scope)] ?? null;
	live.status = 'connecting';
	attempts = 0;
	connect();
}

/** Stop the shared connection (used when the app unmounts). */
export function stopLive() {
	teardown();
	activeCampaign = null;
	live.campaignId = null;
	live.status = 'idle';
}

/**
 * Register a listener for activity events. Listeners are coalesced and debounced.
 * @param {(event: object|null) => void} fn
 * @returns {() => void} unsubscribe
 */
export function onActivity(fn) {
	listeners.add(fn);
	return () => listeners.delete(fn);
}

/** Force a reconnect from the current cursor (e.g. from a status button). */
export function reconnectLive() {
	const scope = activeCampaign;
	teardown();
	activeCampaign = scope;
	live.cursor = readCursors()[storageKey(scope)] ?? live.cursor;
	attempts = 0;
	connect();
}

// Fields that change on every write and say nothing about what the human cares
// about. Reporting them would make every diff look like a change.
const VOLATILE_FIELDS = new Set(['updatedAt', 'rowVersion', 'createdAt']);

/**
 * A plain copy of a row. The caller's object is usually a `$state` proxy, and a
 * baseline that shares the proxy would move with any in-place mutation, making
 * the diff permanently empty. Rows are JSON, so this is lossless.
 * @param {object|null} value
 */
function cloneRow(value) {
	return value == null ? value : JSON.parse(JSON.stringify(value));
}

/**
 * Field-level difference between the object a form opened with and the object
 * the page now holds. Arrays are compared by length only: a step list is the
 * one array that matters, and "2 steps added" is the useful sentence.
 * @param {object|null} base
 * @param {object|null} current
 * @returns {Array<{field: string, from: any, to: any, kind: 'value'|'count'}>}
 */
function diffFields(base, current) {
	if (!base || !current) return [];
	const out = [];
	for (const field of Object.keys(base)) {
		if (VOLATILE_FIELDS.has(field)) continue;
		const before = base[field];
		const after = current[field];
		if (Array.isArray(before) && Array.isArray(after)) {
			if (before.length !== after.length) {
				out.push({ field, from: before.length, to: after.length, kind: 'count' });
			}
			continue;
		}
		if (before === after) continue;
		if (before && after && typeof before === 'object' && typeof after === 'object') {
			if (JSON.stringify(before) === JSON.stringify(after)) continue;
		}
		out.push({ field, from: before, to: after, kind: 'value' });
	}
	return out;
}

/**
 * Follow one entity while a form is open.
 *
 * The value is read from the caller's live state, not fetched here: every page
 * that needs this already refetches on activity, and a second fetch per event
 * would race the first. The subscription exists only to record who claimed the
 * change, which the payload carries and the object does not.
 *
 * Call once during component initialisation.
 *
 * @param {object} options
 * @param {string} options.id entity id, matched against the event's `entity_id`
 * @param {string|null} [options.type] entity type, matched against `entity_type`
 * @param {object} options.snapshot the object as it was when the form opened
 * @param {() => object|null} options.read returns the current object from live state
 * @returns {{current: object|null, changes: Array<object>, lastActor: string|null, lastAction: string|null, rebaseline: () => void}}
 */
export function liveEntity({ id, type = null, snapshot, read }) {
	let base = $state.raw(cloneRow(snapshot));
	let lastActor = $state(null);
	let lastAction = $state(null);

	// Reactive on the caller's state, so a refetch that lands after this
	// listener runs is still seen. A resync (null) needs no special case.
	const current = $derived(read());
	const changes = $derived(diffFields(base, current));

	$effect(() => {
		return onActivity((event) => {
			if (!event) return;
			if (event.entity_id !== id) return;
			if (type && event.entity_type !== type) return;
			lastActor = event.actor ?? null;
			lastAction = event.action ?? null;
		});
	});

	return {
		get current() {
			return current;
		},
		get changes() {
			return changes;
		},
		get lastActor() {
			return lastActor;
		},
		get lastAction() {
			return lastAction;
		},
		/** Adopt the current object as the new baseline, e.g. after a save. */
		rebaseline() {
			base = cloneRow(current);
		}
	};
}
