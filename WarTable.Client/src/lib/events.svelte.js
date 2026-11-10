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
