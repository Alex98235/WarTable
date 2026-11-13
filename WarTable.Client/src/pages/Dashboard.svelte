<script>
	import { apiGet, parseDate, plainText, timeAgo } from '../lib/api.js';
	import { addToast } from '../lib/stores.js';
	import { live, onActivity } from '../lib/events.svelte.js';
	import AttentionItem from '../components/AttentionItem.svelte';
	import CampaignCard from '../components/CampaignCard.svelte';

	let campaigns = $state([]);
	let attention = $state(null);
	let campaignStats = $state({});
	let preferences = $state([]);
	let decisions = $state([]);

	let loading = $state(true);
	let refreshing = $state(false);
	let error = $state('');
	let loadToken = 0;

	let groups = $derived([
		{
			key: 'flag',
			label: 'Flagged steps',
			hint: 'The human-attention channel',
			items: attention?.flaggedSteps ?? []
		},
		{
			key: 'blocked',
			label: 'Blocked objectives',
			hint: 'Derived from blocked or waiting work',
			items: attention?.blockedObjectives ?? []
		},
		{
			key: 'stale',
			label: 'Stale in-progress',
			hint: 'No update in over 72 hours',
			items: attention?.staleInProgressSteps ?? []
		}
	]);

	let urgentCount = $derived(groups.reduce((sum, group) => sum + group.items.length, 0));
	let backlog = $derived(attention?.backlogObjectives ?? []);
	let seeds = $derived(attention?.seedObjectives ?? []);

	let recentCampaigns = $derived(
		[...campaigns]
			.sort((a, b) => {
				const aTime = campaignStats[a.id]?.lastActivity || a.updatedAt || a.createdAt;
				const bTime = campaignStats[b.id]?.lastActivity || b.updatedAt || b.createdAt;
				return parseDate(bTime) - parseDate(aTime);
			})
			.slice(0, 6)
	);

	let campaignNames = $derived(
		Object.fromEntries(campaigns.map((campaign) => [campaign.id, campaign.title]))
	);

	let activePreferences = $derived(preferences.filter((pref) => pref.active).slice(0, 4));
	let recentDecisions = $derived(decisions.slice(0, 4));

	const load = async ({ silent = false } = {}) => {
		if (silent) refreshing = true;
		else loading = true;
		error = '';
		const token = ++loadToken;

		try {
			const [attentionData, campaignData, preferenceData, decisionData] = await Promise.all([
				apiGet('/api/attention'),
				apiGet('/api/campaigns'),
				apiGet('/api/preferences').catch(() => []),
				apiGet('/api/notes', {
					kind: 'Decision',
					status: 'Active'
				}).catch(() => [])
			]);

			if (token !== loadToken) return;

			attention = attentionData;
			campaigns = Array.isArray(campaignData) ? campaignData : [];
			preferences = Array.isArray(preferenceData) ? preferenceData : [];
			decisions = Array.isArray(decisionData) ? decisionData : [];

			await loadCampaignStats(campaigns, token);
		} catch (err) {
			if (token !== loadToken) return;
			error = err.message || 'Could not read the table.';
			addToast('Could not load the dashboard.', 'error');
		} finally {
			if (token === loadToken) {
				loading = false;
				refreshing = false;
			}
		}
	};

	async function loadCampaignStats(list, token) {
		const entries = await Promise.all(
			list.map(async (campaign) => {
				const [objectives, activity] = await Promise.all([
					apiGet('/api/objectives', { campaignId: campaign.id }).catch(() => []),
					apiGet('/api/activity', {
						campaignId: campaign.id,
						limit: 1
					}).catch(() => [])
				]);
				const objectiveList = Array.isArray(objectives) ? objectives : [];
				const planList = objectiveList.filter((o) => o.planState !== 'Seed');
				return [
					campaign.id,
					{
						total: planList.length,
						ready: planList.filter((o) => o.planState === 'Ready').length,
						backlog: planList.filter((o) => o.planState === 'Backlog').length,
						completed: planList.filter((o) => o.status === 'completed').length,
						seeds: objectiveList.filter((o) => o.planState === 'Seed').length,
						lastActivity:
							activity?.[0]?.createdAt || campaign.updatedAt || campaign.createdAt
					}
				];
			})
		);

		if (token !== loadToken) return;
		campaignStats = Object.fromEntries(entries);
	}

	load();

	$effect(() => {
		return onActivity(() => load({ silent: true }));
	});
</script>

<svelte:head>
	<title>WarTable — Dashboard</title>
</svelte:head>

{#if loading}
	<div class="loading-state">
		<div class="loading-spinner"></div>
		<p>Reading the table…</p>
	</div>
{:else if error}
	<div class="error-state">
		<p class="eyebrow">Connection trouble</p>
		<h1>The table could not be read.</h1>
		<p class="lede">{error}</p>
		<button type="button" class="btn btn-primary" onclick={() => load()}>Try again</button>
	</div>
{:else}
	<div class="page-intro reveal" style="--i: 0">
		<div>
			<p class="eyebrow">Your table</p>
			<h1 class="page-title">The table, at a glance</h1>
			<p class="lede">
				What needs you, what is moving, and which campaigns are still alive. Live updates
				are
				{#if live.status === 'live'}
					<span class="inline-live">on</span>
				{:else if live.status === 'connecting'}
					<span class="inline-live is-off">connecting</span>
				{:else}
					<span class="inline-live is-off">reconnecting</span>
				{/if}.
			</p>
		</div>
		<div class="page-actions">
			{#if refreshing}<span class="sync-note">Syncing…</span>{/if}
			<a class="btn btn-secondary" href="#/attention">All attention</a>
			<a class="btn btn-primary" href="#/campaigns">View campaigns</a>
		</div>
	</div>

	<div class="dash-grid">
		<section class="panel inbox-panel reveal" style="--i: 1">
			<header class="panel-head">
				<div class="panel-heading">
					<p class="eyebrow">Inbox</p>
					<h2 class="panel-title">Attention</h2>
					<p class="panel-subtitle">
						{urgentCount === 0
							? 'Nothing is waiting on you right now.'
							: `${urgentCount} item${urgentCount === 1 ? '' : 's'} need a look. Flagged work comes first.`}
					</p>
				</div>
				<a class="text-link" href="#/attention">Open inbox →</a>
			</header>

			{#if urgentCount === 0}
				<div class="calm-state">
					<span class="calm-mark" aria-hidden="true">◌</span>
					<div>
						<h3>The inbox is clear.</h3>
						<p>No flagged steps, blocked objectives, or stale work.</p>
					</div>
				</div>
			{:else}
				<div class="inbox-groups">
					{#each groups as group (group.key)}
						{#if group.items.length > 0}
							<div class="inbox-group">
								<div class="inbox-group-head">
									<h3 class="inbox-group-title">{group.label}</h3>
									<span class="inbox-count">{group.items.length}</span>
									<span class="inbox-hint">{group.hint}</span>
								</div>
								<div class="attention-list">
									{#each group.items.slice(0, 4) as item (item.entityId + item.kind)}
										<AttentionItem {item} />
									{/each}
								</div>
								{#if group.items.length > 4}
									<a class="more-link" href="#/attention"
										>+{group.items.length - 4} more in the inbox</a
									>
								{/if}
							</div>
						{/if}
					{/each}
				</div>
			{/if}

			{#if backlog.length > 0}
				<div class="parked-zone">
					<div class="parked-head">
						<div>
							<p class="eyebrow">Parked</p>
							<h3>Backlog</h3>
						</div>
						<span class="parked-count">{backlog.length}</span>
					</div>
					<p class="parked-hint">
						Backlog plans are visible but not actionable. Release one when it is time to
						work.
					</p>
					<div class="parked-list">
						{#each backlog.slice(0, 3) as item (item.entityId)}
							<AttentionItem {item} />
						{/each}
					</div>
					{#if backlog.length > 3}
						<a class="more-link" href="#/attention"
							>+{backlog.length - 3} more parked plans</a
						>
					{/if}
				</div>
			{/if}

			{#if seeds.length > 0}
				<div class="parked-zone seed-zone">
					<div class="parked-head">
						<div>
							<p class="eyebrow">Seeds</p>
							<h3>Seeds to shape</h3>
						</div>
						<span class="parked-count">{seeds.length}</span>
					</div>
					<p class="parked-hint">
						Unfinished thoughts. Shape one into a plan; it lands in the backlog.
					</p>
					<div class="parked-list">
						{#each seeds.slice(0, 3) as item (item.entityId)}
							<AttentionItem {item} />
						{/each}
					</div>
					{#if seeds.length > 3}
						<a class="more-link" href="#/attention">+{seeds.length - 3} more seeds</a>
					{/if}
				</div>
			{/if}
		</section>

		<aside class="dash-rail">
			<section class="panel rail-panel reveal" style="--i: 2">
				<header class="panel-head compact">
					<div class="panel-heading">
						<p class="eyebrow">Memory</p>
						<h2 class="panel-title">Recent decisions</h2>
					</div>
					<a class="text-link" href="#/campaigns">Browse →</a>
				</header>
				{#if recentDecisions.length === 0}
					<p class="inline-empty">No active decisions recorded yet.</p>
				{:else}
					<ul class="rail-list">
						{#each recentDecisions as note (note.id)}
							<li>
								<a
									class="rail-item"
									href="#/campaign/{note.campaignId}/bunker/{note.alias}"
								>
									<span class="rail-item-top">
										<span class="ref-chip">#{note.alias}</span>
										<span class="rail-item-title">{note.title}</span>
									</span>
									<span class="rail-item-sub">
										{campaignNames[note.campaignId] || 'Campaign'} · {timeAgo(
											note.createdAt
										)}
									</span>
									<span class="rail-item-body">{plainText(note.body, 95)}</span>
								</a>
							</li>
						{/each}
					</ul>
				{/if}
			</section>

			<section class="panel rail-panel reveal" style="--i: 3">
				<header class="panel-head compact">
					<div class="panel-heading">
						<p class="eyebrow">Always applied</p>
						<h2 class="panel-title">Your preferences</h2>
					</div>
					<a class="text-link" href="#/preferences">Manage →</a>
				</header>
				{#if activePreferences.length === 0}
					<p class="inline-empty">No active preferences captured yet.</p>
				{:else}
					<ul class="pref-rail-list">
						{#each activePreferences as pref (pref.id)}
							<li class="pref-rail-item">
								<span class="pref-mark" aria-hidden="true">✦</span>
								<span>
									<span class="pref-rail-text">{pref.text}</span>
									{#if pref.rationale}
										<span class="pref-rail-rationale">{pref.rationale}</span>
									{/if}
								</span>
							</li>
						{/each}
					</ul>
				{/if}
			</section>
		</aside>
	</div>

	<section class="dash-campaigns reveal" style="--i: 4">
		<div class="section-head">
			<div>
				<p class="eyebrow">Your work</p>
				<h2 class="section-title">Campaigns</h2>
				<p class="section-subtitle">
					{campaigns.length === 0
						? 'No campaigns yet. A campaign is a project-wide container; its objectives carry the state.'
						: `${campaigns.length} campaign${campaigns.length === 1 ? '' : 's'}, most recently active first.`}
				</p>
			</div>
			<a class="btn btn-secondary" href="#/campaigns">All campaigns</a>
		</div>

		{#if campaigns.length === 0}
			<div class="empty-state">
				<h3>No campaigns yet</h3>
				<p>
					Create a campaign for the next body of work. Objectives carry the state that
					matters.
				</p>
				<a class="btn btn-primary" href="#/campaigns">Create a campaign</a>
			</div>
		{:else}
			<div class="folder-grid">
				{#each recentCampaigns as campaign (campaign.id)}
					<CampaignCard {campaign} stats={campaignStats[campaign.id] || {}} />
				{/each}
			</div>
		{/if}
	</section>
{/if}
