// WarTable API client
// All paths must include the /api prefix (e.g. '/api/campaigns').

/** The UI writes as `human`. This instance has no accounts. */
export const ACTOR = 'human';

/**
 * Error carrying HTTP status and parsed body (if any) alongside a message.
 */
export class ApiError extends Error {
	/**
	 * @param {number} status
	 * @param {string} message
	 * @param {object|null} body
	 */
	constructor(status, message, body = null) {
		super(message);
		this.name = 'ApiError';
		this.status = status;
		this.body = body;
	}
}

/** True when the error indicates a stale row. */
export function isConflict(error) {
	return error instanceof ApiError && error.status === 409;
}

/** Extract the `current` row from a 409, or null. */
export function getConflictCurrent(error) {
	if (!isConflict(error) || !error.body) return null;
	return error.body.current ?? null;
}

function buildQuery(params = {}) {
	const query = new URLSearchParams();
	Object.entries(params).forEach(([key, val]) => {
		if (val !== null && val !== undefined && val !== '') {
			query.set(key, val);
		}
	});
	return query.toString();
}

async function parseErrorBody(res) {
	try {
		return await res.json();
	} catch {
		return null;
	}
}

export async function apiGet(path, params = {}) {
	const qs = buildQuery(params);
	const url = qs ? `${path}?${qs}` : path;
	const res = await fetch(url);
	if (!res.ok) {
		const body = await parseErrorBody(res);
		throw new ApiError(res.status, `GET ${url} ${res.status}`, body);
	}
	return res.json();
}

async function send(method, path, body) {
	const res = await fetch(path, {
		method,
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(body ?? {})
	});
	if (!res.ok) {
		const errBody = await parseErrorBody(res);
		throw new ApiError(res.status, `${method} ${path} ${res.status}`, errBody);
	}
	if (res.status === 204) return null;
	const text = await res.text();
	if (!text) return null;
	try {
		return JSON.parse(text);
	} catch {
		return null;
	}
}

export async function apiPost(path, body) {
	return send('POST', path, body);
}

export async function apiPut(path, body) {
	return send('PUT', path, body);
}

export async function apiPatch(path, body) {
	return send('PATCH', path, body);
}

export async function apiDelete(path, params = {}) {
	const qs = buildQuery(params);
	const url = qs ? `${path}?${qs}` : path;
	const res = await fetch(url, { method: 'DELETE' });
	if (!res.ok) {
		const body = await parseErrorBody(res);
		throw new ApiError(res.status, `DELETE ${url} ${res.status}`, body);
	}
	if (res.status === 204) return null;
	const text = await res.text();
	if (!text) return null;
	try {
		return JSON.parse(text);
	} catch {
		return null;
	}
}

/** Parse a comma-separated tag string (or an array) into an array. */
export function parseTags(tags) {
	if (!tags) return [];
	if (Array.isArray(tags)) return tags.filter((t) => t);
	return String(tags)
		.split(',')
		.map((t) => t.trim())
		.filter((t) => t);
}

/**
 * Parse an API timestamp. SQLite-backed DateTime values sometimes arrive without
 * a timezone suffix even though they are UTC; treat those as UTC rather than
 * local time so relative labels stay honest.
 */
export function parseDate(value) {
	if (!value) return null;
	const text = String(value);
	const normalized = /[zZ]|[+-]\d{2}:?\d{2}$/.test(text) ? text : `${text}Z`;
	const date = new Date(normalized);
	return Number.isNaN(date.getTime()) ? new Date(text) : date;
}

/** Format a date string as a short relative time. */
export function timeAgo(dateStr) {
	if (!dateStr) return '';
	const d = parseDate(dateStr);
	const now = new Date();
	const diff = now - d;
	if (diff < 60000) return 'just now';
	if (diff < 3600000) return Math.floor(diff / 60000) + 'm ago';
	if (diff < 86400000) return Math.floor(diff / 3600000) + 'h ago';
	if (diff < 604800000) return Math.floor(diff / 86400000) + 'd ago';
	if (diff < 31536000000) {
		return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
	}
	return d.toLocaleDateString('en-US', {
		month: 'short',
		day: 'numeric',
		year: 'numeric'
	});
}

export function formatDate(dateStr) {
	if (!dateStr) return '';
	const d = parseDate(dateStr);
	return d.toLocaleDateString('en-US', {
		month: 'short',
		day: 'numeric',
		year: 'numeric',
		hour: 'numeric',
		minute: '2-digit'
	});
}

/** Human label for an actor string in the provenance convention. */
export function actorLabel(actor) {
	if (!actor) return 'Unknown';
	if (actor === 'human') return 'Human';
	if (actor.startsWith('human:')) return actor.slice(6);
	if (actor.startsWith('agent:')) return actor.slice(6);
	return actor;
}

export function isAgent(actor) {
	return typeof actor === 'string' && actor.startsWith('agent:');
}

/** `#N.M` reference for a step. */
export function stepRef(objectiveAlias, stepNumber) {
	return `#${objectiveAlias}.${stepNumber}`;
}

/** `#N` reference for an objective or note. */
export function objectRef(alias) {
	return `#${alias}`;
}

/**
 * Progress of a step list. Returns zeroed values for an empty or missing list.
 */
export function stepProgress(steps) {
	const list = Array.isArray(steps) ? steps : [];
	const total = list.length;
	const done = list.filter((s) => s.status === 'done').length;
	const flagged = list.filter((s) => s.status === 'flagged').length;
	const blocked = list.filter((s) => s.status === 'blocked').length;
	const inProgress = list.filter((s) => s.status === 'in-progress').length;
	return {
		total,
		done,
		flagged,
		blocked,
		inProgress,
		pct: total ? Math.round((done / total) * 100) : 0
	};
}

/** Newest update on a step, or null. */
export function latestUpdate(step) {
	const updates = step?.updates;
	if (!Array.isArray(updates) || updates.length === 0) return null;
	return [...updates].sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt))[0];
}

/** Plain-text fallback for markdown excerpts (cards, rails). */
export function plainText(markdown, maxLen = 180) {
	if (!markdown) return '';
	let text = String(markdown)
		.replace(/```[\s\S]*?```/g, ' ')
		.replace(/`([^`]+)`/g, '$1')
		.replace(/!\[[^\]]*\]\([^)]*\)/g, ' ')
		.replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
		.replace(/[#>*_~]/g, '')
		.replace(/\s+/g, ' ')
		.trim();
	if (text.length > maxLen) text = text.slice(0, maxLen).trimEnd() + '…';
	return text;
}

export function truncate(text, maxLen = 120) {
	if (!text) return '';
	return text.length > maxLen ? text.slice(0, maxLen).trimEnd() + '…' : text;
}

/** All valid step statuses, in lifecycle order. */
export const STEP_STATUSES = ['open', 'in-progress', 'done', 'flagged', 'blocked'];

/** All note kinds and statuses, matching the backend enums. */
export const NOTE_KINDS = ['Directive', 'Decision', 'Gotcha', 'Context'];

/** Kinds the generic Bunker capture dialog may create. */
export const NOTE_CREATE_KINDS = NOTE_KINDS;

export const NOTE_STATUSES = ['Active', 'Superseded'];
export const EVIDENCE_KINDS = ['Link', 'Commit'];
export const UPDATE_KINDS = ['Note', 'Blocker', 'Result', 'Handoff'];
