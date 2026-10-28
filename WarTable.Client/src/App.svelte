<script>
  import Toast from './components/Toast.svelte';
  import Confirm from './components/Confirm.svelte';
  import AliasPopover from './components/AliasPopover.svelte';
  import Dashboard from './pages/Dashboard.svelte';
  import Campaigns from './pages/Campaigns.svelte';
  import CampaignDetail from './pages/CampaignDetail.svelte';
  import Intel from './pages/Intel.svelte';
  import ObjectiveDetail from './pages/ObjectiveDetail.svelte';
  import IntelDetail from './pages/IntelDetail.svelte';
  import SitRepDetail from './pages/SitRepDetail.svelte';

  let currentHash = $state(window.location.hash.slice(1) || '/');

  function onHashChange() {
    currentHash = window.location.hash.slice(1) || '/';
  }

  $effect(() => {
    window.addEventListener('hashchange', onHashChange);
    if (!window.location.hash) {
      window.location.hash = '#/';
    }
    return () => window.removeEventListener('hashchange', onHashChange);
  });

  function navigate(hash) {
    window.location.hash = hash;
  }

  function matchRoute() {
    function match(pattern, hash) {
      const pp = pattern.split('/');
      const hp = hash.split('/');
      if (pp.length !== hp.length) return null;
      const params = {};
      for (let i = 0; i < pp.length; i++) {
        if (pp[i].startsWith(':')) params[pp[i].slice(1)] = hp[i];
        else if (pp[i] !== hp[i]) return null;
      }
      return params;
    }

    const routes = [
      { pattern: '/', component: Dashboard },
      { pattern: '/campaigns', component: Campaigns },
      { pattern: '/campaign/:id', component: CampaignDetail },
      { pattern: '/campaign/:id/objective/:alias', component: ObjectiveDetail },
      { pattern: '/campaign/:id/intel/:alias', component: IntelDetail },
      { pattern: '/campaign/:id/sitrep/:alias', component: SitRepDetail },
      { pattern: '/intel', component: Intel },
    ];

    for (const { pattern, component } of routes) {
      const params = match(pattern, currentHash);
      if (params !== null) return { component, params };
    }
    return { component: null, params: {} };
  }

  let route = $derived(matchRoute());
</script>

<!-- Header -->
<header class="site-header">
  <div class="container">
    <a href="#/" class="site-logo" onclick={(e) => { e.preventDefault(); navigate('/'); }}>
      🗺 WarTable
    </a>
    <nav class="site-nav">
      <a href="#/" class="nav-link" class:active={currentHash === '/'} onclick={(e) => { e.preventDefault(); navigate('/'); }}>
        Dashboard
      </a>
      <a href="#/campaigns" class="nav-link" class:active={currentHash.startsWith('/campaign')} onclick={(e) => { e.preventDefault(); navigate('/campaigns'); }}>
        Campaigns
      </a>
      <a href="#/intel" class="nav-link" class:active={currentHash === '/intel'} onclick={(e) => { e.preventDefault(); navigate('/intel'); }}>
        Intel
      </a>
    </nav>
  </div>
</header>

<main class="main-content container">
  {#if route.component}
    <route.component {route} />
  {:else}
    <div class="empty-state">
      <h2>Page not found</h2>
      <p>The page you're looking for doesn't exist.</p>
      <a href="#/" class="btn btn-primary" onclick={(e) => { e.preventDefault(); navigate('/'); }}>
        Go to Dashboard
      </a>
    </div>
  {/if}
</main>

<Toast />
<Confirm />
<AliasPopover />