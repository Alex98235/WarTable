// Simple hash-based SPA router for Svelte 5
// Routes: #/ (dashboard), #/campaigns, #/campaign/:id, #/intel

const routes = [];

export function addRoute(pattern, component) {
  routes.push({ pattern, component });
}

export function getRoute() {
  const hash = window.location.hash.slice(1) || '/';
  for (const { pattern, component } of routes) {
    const params = match(pattern, hash);
    if (params !== null) {
      return { component, params };
    }
  }
  return { component: null, params: {} };
}

function match(pattern, hash) {
  const patternParts = pattern.split('/');
  const hashParts = hash.split('/');
  if (patternParts.length !== hashParts.length) return null;
  const params = {};
  for (let i = 0; i < patternParts.length; i++) {
    if (patternParts[i].startsWith(':')) {
      params[patternParts[i].slice(1)] = hashParts[i];
    } else if (patternParts[i] !== hashParts[i]) {
      return null;
    }
  }
  return params;
}

export function navigate(hash) {
  window.location.hash = hash;
}

export function initRouter() {
  window.addEventListener('hashchange', () => {});
  if (!window.location.hash) {
    window.location.hash = '#/';
  }
}