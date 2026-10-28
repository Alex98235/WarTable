// WarTable API client
// All paths must include the /api prefix (e.g. '/api/campaigns')

export async function apiGet(path, params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, val]) => {
    if (val !== null && val !== undefined && val !== '') {
      query.set(key, val);
    }
  });
  const qs = query.toString();
  const url = qs ? `${path}?${qs}` : path;
  const res = await fetch(url);
  if (!res.ok) {
    const err = await res.text();
    throw new Error(`GET ${url} ${res.status}: ${err}`);
  }
  return res.json();
}

export async function apiPost(path, body) {
  const res = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    const err = await res.text();
    throw new Error(`POST ${path} ${res.status}: ${err}`);
  }
  return res.json();
}

export async function apiPut(path, body) {
  const res = await fetch(path, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    const err = await res.text();
    throw new Error(`PUT ${path} ${res.status}: ${err}`);
  }
  return res.json();
}

export async function apiDelete(path) {
  const res = await fetch(path, { method: 'DELETE' });
  if (!res.ok) {
    const err = await res.text();
    throw new Error(`DELETE ${path} ${res.status}: ${err}`);
  }
  if (res.status === 204) return null;
  return res.json();
}

/** Parse tags string to array (e.g. "a, b" → ["a", "b"]) */
export function parseTags(tags) {
  if (!tags) return [];
  if (Array.isArray(tags)) return tags;
  return tags
    .split(',')
    .map((t) => t.trim())
    .filter((t) => t);
}

/** Format a date string for display */
export function timeAgo(dateStr) {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  const now = new Date();
  const diff = now - d;
  if (diff < 60000) return 'just now';
  if (diff < 3600000) return Math.floor(diff / 60000) + 'm ago';
  if (diff < 86400000) return Math.floor(diff / 3600000) + 'h ago';
  if (diff < 604800000) return Math.floor(diff / 86400000) + 'd ago';
  return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
}

export function formatDate(dateStr) {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  return d.toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  });
}