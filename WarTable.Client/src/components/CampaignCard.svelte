<script>
	import { parseTags, plainText, timeAgo } from '../lib/api.js';

	/**
	 * Campaign card used on the dashboard and campaigns list.
	 * stats: { total, ready, backlog, completed, lastActivity }
	 */
	let { campaign, stats = {} } = $props();

	let tags = $derived(parseTags(campaign.tags));
	let excerpt = $derived(plainText(campaign.charterExcerpt, 170));
	let total = $derived(stats.total ?? 0);
	let completed = $derived(stats.completed ?? 0);
	let ready = $derived(stats.ready ?? 0);
	let backlog = $derived(stats.backlog ?? 0);
	let seeds = $derived(stats.seeds ?? 0);
	let pct = $derived(total ? Math.round((completed / total) * 100) : 0);
	let lastActivity = $derived(stats.lastActivity || campaign.updatedAt || campaign.createdAt);
</script>

<a class="folder-card" href="#/campaign/{campaign.id}">
	<span class="folder-tab" aria-hidden="true"></span>
	<div class="folder-body">
		<div class="folder-head">
			<h3 class="folder-title">{campaign.title}</h3>
			<span class="folder-age" title="Last activity">{timeAgo(lastActivity)}</span>
		</div>

		<p class="folder-charter">{excerpt || 'No charter yet.'}</p>

		{#if tags.length > 0}
			<div class="tag-row">
				{#each tags as tag}
					<span class="tag">{tag}</span>
				{/each}
			</div>
		{/if}

		<div class="folder-progress">
			<div class="progress-label">
				<span>
					{#if total === 0}
						No objectives yet
					{:else}
						{completed}/{total} objectives complete
					{/if}
				</span>
				<span class="progress-counts">
					{#if ready > 0}<span>{ready} ready</span>{/if}
					{#if backlog > 0}<span>{backlog} backlog</span>{/if}
					{#if seeds > 0}<span>{seeds} seed{seeds === 1 ? '' : 's'}</span>{/if}
				</span>
			</div>
			<div class="progress-track" aria-hidden="true">
				<span class="progress-fill" style="width: {pct}%"></span>
			</div>
		</div>
	</div>
</a>
