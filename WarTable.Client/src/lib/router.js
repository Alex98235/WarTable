// Hash-based SPA router helpers.
//
// Routes are declared in App.svelte:
//   #/                                Dashboard
//   #/attention                       Attention inbox
//   #/campaigns                       Campaigns
//   #/preferences                     Preferences
//   #/campaign/:id                    Campaign detail (plan tree + Bunker rail)
//   #/campaign/:id/objective/:alias   Objective detail
//   #/campaign/:id/bunker             Bunker knowledgebase
//   #/campaign/:id/bunker/:alias      Bunker note detail

/** Match a hash path against a `:param` pattern. Returns params or null. */
export function matchPath(pattern, hash) {
	const patternParts = pattern.split('/');
	const hashParts = hash.split('/');
	if (patternParts.length !== hashParts.length) return null;

	const params = {};
	for (let i = 0; i < patternParts.length; i++) {
		if (patternParts[i].startsWith(':')) {
			params[patternParts[i].slice(1)] = decodeURIComponent(hashParts[i]);
		} else if (patternParts[i] !== hashParts[i]) {
			return null;
		}
	}
	return params;
}

/** Current hash path without the leading `#`. */
export function currentPath() {
	return window.location.hash.slice(1) || '/';
}

export function navigate(path) {
	window.location.hash = path;
}
