<script>
	import Toast from './components/Toast.svelte';
	import Confirm from './components/Confirm.svelte';
	import AliasPopover from './components/AliasPopover.svelte';
	import Dashboard from './pages/Dashboard.svelte';
	import Attention from './pages/Attention.svelte';
	import Campaigns from './pages/Campaigns.svelte';
	import CampaignDetail from './pages/CampaignDetail.svelte';
	import ObjectiveDetail from './pages/ObjectiveDetail.svelte';
	import Bunker from './pages/Bunker.svelte';
	import Preferences from './pages/Preferences.svelte';
	import { currentPath, matchPath } from './lib/router.js';
	import { live, reconnectLive, startLive, stopLive } from './lib/events.svelte.js';

	const routes = [
		{ pattern: '/', component: Dashboard, nav: 'dashboard' },
		{ pattern: '/attention', component: Attention, nav: 'attention' },
		{ pattern: '/campaigns', component: Campaigns, nav: 'campaigns' },
		{ pattern: '/preferences', component: Preferences, nav: 'preferences' },
		{ pattern: '/campaign/:id', component: CampaignDetail, nav: 'campaigns' },
		{ pattern: '/campaign/:id/objective/:alias', component: ObjectiveDetail, nav: 'campaigns' },
		{ pattern: '/campaign/:id/bunker', component: Bunker, nav: 'campaigns' },
		{ pattern: '/campaign/:id/bunker/:alias', component: Bunker, nav: 'campaigns' }
	];

	let currentHash = $state(currentPath());
	let lastPath = currentPath();

	let route = $derived.by(() => {
		for (const entry of routes) {
			const params = matchPath(entry.pattern, currentHash);
			if (params !== null) return { ...entry, params };
		}
		return { component: null, params: {}, nav: null };
	});

	// Remount when the campaign changes, and when the objective alias changes
	// within the same campaign. Bunker note selection keeps the same key so
	// filters and scroll position survive.
	let routeKey = $derived.by(() => {
		if (route.pattern === '/campaign/:id/objective/:alias') {
			return `${route.pattern}:${route.params.id}:${route.params.alias}`;
		}
		return route.params?.id || route.pattern || 'not-found';
	});

	// A single global feed keeps every view current, including global preference
	// events (which have no campaignId). The store still supports campaign-scoped
	// connections if a future view needs one.
	$effect(() => {
		startLive();
	});

	$effect(() => {
		return () => stopLive();
	});

	$effect(() => {
		function onHashChange() {
			const next = currentPath();
			const prevParts = lastPath.split('/');
			const nextParts = next.split('/');
			// Selecting a note in the Bunker should not throw the reader back to the
			// top of the page; every other route change starts at the top.
			const stayingInBunker =
				prevParts[1] === 'campaign' &&
				nextParts[1] === 'campaign' &&
				prevParts[2] === nextParts[2] &&
				prevParts[3] === 'bunker' &&
				nextParts[3] === 'bunker';

			currentHash = next;
			if (!stayingInBunker) {
				window.scrollTo({ top: 0, behavior: 'instant' });
			}
			lastPath = next;
		}
		window.addEventListener('hashchange', onHashChange);
		if (!window.location.hash) {
			window.location.hash = '#/';
		}
		return () => window.removeEventListener('hashchange', onHashChange);
	});

	function navClass(key) {
		return route.nav === key ? 'nav-link is-active' : 'nav-link';
	}

	let liveLabel = $derived(
		live.status === 'live'
			? 'Live'
			: live.status === 'connecting'
				? 'Connecting'
				: live.status === 'reconnecting'
					? 'Reconnecting'
					: 'Offline'
	);

	let liveTitle = $derived(
		live.lastEvent?.summary
			? `Live updates connected · last change: ${live.lastEvent.summary}`
			: live.status === 'live'
				? 'Live updates connected. Changes from other devices appear automatically.'
				: 'Live updates are not connected. Click to retry.'
	);
</script>

<header class="topbar">
	<div class="topbar-inner container">
		<a class="brand" href="#/">
			<span class="brand-mark" aria-hidden="true">W</span>
			<span class="brand-text">
				<span class="brand-name">WarTable</span>
				<span class="brand-tag">Planning state that outlives the conversation</span>
			</span>
		</a>

		<nav class="topnav" aria-label="Primary">
			<a class={navClass('dashboard')} href="#/">
				<span class="nav-glyph" aria-hidden="true">⌂</span> Dashboard
			</a>
			<a class={navClass('attention')} href="#/attention">
				<span class="nav-glyph" aria-hidden="true">◎</span> Attention
			</a>
			<a class={navClass('campaigns')} href="#/campaigns">
				<span class="nav-glyph" aria-hidden="true">▤</span> Campaigns
			</a>
			<a class={navClass('preferences')} href="#/preferences">
				<span class="nav-glyph" aria-hidden="true">✦</span> Preferences
			</a>
		</nav>

		<div class="topbar-right">
			<button
				type="button"
				class="live-pill status-{live.status}"
				onclick={() => {
					if (live.status !== 'live') reconnectLive();
				}}
				title={liveTitle}
				aria-label={liveTitle}
			>
				<span class="live-dot" aria-hidden="true"></span>
				<span>{liveLabel}</span>
			</button>
		</div>
	</div>
</header>

<main class="page container">
	{#if route.component}
		{#key routeKey}
			<route.component {route} />
		{/key}
	{:else}
		<div class="not-found">
			<p class="eyebrow">Unknown page</p>
			<h1 class="not-found-title">This route is not on the map.</h1>
			<p class="lede">
				The page may have moved when WarTable reorganized work into campaigns, plan trees,
				and the Bunker.
			</p>
			<div class="page-actions">
				<a class="btn btn-primary" href="#/">Return to the dashboard</a>
				<a class="btn btn-secondary" href="#/campaigns">Browse campaigns</a>
			</div>
		</div>
	{/if}
</main>

<footer class="site-footer container">
	<span class="footer-dim">WarTable · campaigns, plan trees, Bunker knowledge, attention.</span>
</footer>

<Toast />
<Confirm />
<AliasPopover />
