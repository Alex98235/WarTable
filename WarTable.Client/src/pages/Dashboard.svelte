<script>
   import { apiGet, parseTags, timeAgo } from "../lib/api.js";
   import { addToast } from "../lib/stores.js";

   let campaigns = $state([]);
   let intelEntries = $state([]);
   let loading = $state(true);
   let expandedIntel = $state([]);

   let activeCampaigns = $derived(
      campaigns.filter(
         (c) => c.status !== "Completed" && c.status !== "Abandoned",
      ),
   );
   let activeCount = $derived(activeCampaigns.length);
   let completedCount = $derived(
      campaigns.filter((c) => c.status === "Completed").length,
   );
   let totalCount = $derived(campaigns.length);
   let intelCount = $derived(intelEntries.length);
   let recentIntel = $derived(intelEntries.slice(0, 5));

   $effect(() => {
      loadData();
   });

   async function loadData() {
      loading = true;
      try {
         const campaignData = await apiGet("/api/campaigns");
         campaigns = campaignData.campaigns ?? campaignData;

         const intelData = await apiGet("/api/intel");
         intelEntries = intelData.intel ?? intelData;
      } catch (err) {
         addToast(err.message || "Failed to load dashboard data", "error");
         campaigns = [];
         intelEntries = [];
      } finally {
         loading = false;
      }
   }

   function goTo(path) {
      window.location.hash = path;
   }

   function toggleIntel(id) {
      if (expandedIntel.includes(id)) {
         expandedIntel = expandedIntel.filter((i) => i !== id);
      } else {
         expandedIntel = [...expandedIntel, id];
      }
   }

   function truncate(text, maxLen = 120) {
      if (!text) return "";
      return text.length > maxLen ? text.slice(0, maxLen) + "…" : text;
   }

   function bodyPreview(text) {
      if (!text) return "";
      return text.length > 100 ? text.slice(0, 100) + "…" : text;
   }

   function isExpanded(id) {
      return expandedIntel.includes(id);
   }

   function getCategoryBadge(category) {
      if (!category) return "badge-reference";
      const c = category.toLowerCase();
      if (c === "decision") return "badge-decision";
      if (c === "context") return "badge-context";
      if (c === "gotcha") return "badge-gotcha";
      return "badge-reference";
   }
</script>

{#if loading}
   <div class="loading-state">
      <div class="loading-spinner"></div>
      <p>Loading dashboard…</p>
   </div>
{:else if campaigns.length === 0 && intelEntries.length === 0}
   <div class="empty-state">
      <h3>No data yet</h3>
      <p>
         Create your first campaign or add some intel entries to get started.
      </p>
      <div class="mt-4">
         <a
            href="#/campaigns"
            class="btn btn-primary"
            onclick={(e) => {
               e.preventDefault();
               goTo("/campaigns");
            }}
         >
            Go to Campaigns
         </a>
      </div>
   </div>
{:else}
   <!-- Summary Stats -->
   <div class="dashboard-section">
      <div class="card card-padding">
         <div class="summary-stats">
            <div class="summary-stat summary-stat-bg-active">
               <div class="summary-card-label">Active Campaigns</div>
               <div class="summary-card-value">{activeCount}</div>
            </div>
            <div class="summary-stat summary-stat-bg-completed">
               <div class="summary-card-label">Completed Campaigns</div>
               <div class="summary-card-value">{completedCount}</div>
            </div>
            <div class="summary-stat summary-stat-bg-intel">
               <div class="summary-card-label">Intel Entries</div>
               <div class="summary-card-value">{intelCount}</div>
            </div>
            <div class="summary-stat summary-stat-bg-total">
               <div class="summary-card-label">Total Campaigns</div>
               <div class="summary-card-value">{totalCount}</div>
            </div>
         </div>
      </div>
   </div>

   <!-- Active Campaigns -->
   <div class="dashboard-section">
      <div class="section-header">
         <h2>Active Campaigns</h2>
         <a
            href="#/campaigns"
            class="btn btn-primary"
            onclick={(e) => {
               e.preventDefault();
               goTo("/campaigns");
            }}
         >
            All Campaigns
         </a>
      </div>

      {#if activeCampaigns.length === 0}
         <div class="empty-state">
            <h3>No active campaigns</h3>
            <p>All campaigns are completed or abandoned. Start a new one!</p>
            <div class="mt-4">
               <a
                  href="#/campaigns"
                  class="btn btn-primary"
                  onclick={(e) => {
                     e.preventDefault();
                     goTo("/campaigns");
                  }}
               >
                  View All Campaigns
               </a>
            </div>
         </div>
      {:else}
         <div class="dashboard-campaign-list">
            {#each activeCampaigns as campaign (campaign.id)}
               <a
                  href="#/campaign/{campaign.id}"
                  class="dashboard-campaign-item"
                  onclick={(e) => {
                     e.preventDefault();
                     goTo(`/campaign/${campaign.id}`);
                  }}
               >
                  <div class="dashboard-campaign-item-title">
                     {campaign.title}
                     <div class="dashboard-campaign-item-meta">
                        <span class="badge badge-{(campaign.status || 'planning').toLowerCase()}"
                           >{campaign.status || "planning"}</span
                        >
                     </div>
                  </div>
                  {#if campaign.tags}
                     <div class="dashboard-campaign-item-meta">
                        {#each parseTags(campaign.tags) as tag}
                           <span class="tag-badge">{tag}</span>
                        {/each}
                     </div>
                  {/if}
               </a>
            {/each}
         </div>
      {/if}
   </div>

   <!-- Recent Intel -->
   <div class="dashboard-section">
      <div class="section-header">
         <h2>Recent Intel</h2>
         <a
            href="#/intel"
            class="btn btn-primary"
            onclick={(e) => {
               e.preventDefault();
               goTo("/intel");
            }}
         >
            View All Intel
         </a>
      </div>

      {#if intelEntries.length === 0}
         <div class="empty-state">
            <h3>No intel entries yet</h3>
            <p>Intel entries will appear here once they are added.</p>
         </div>
      {:else}
         <div class="intel-feed">
            {#each recentIntel as entry (entry.id)}
               <div
                  class="intel-entry"
                  onclick={() => toggleIntel(entry.id)}
                  onkeydown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); toggleIntel(entry.id); } }}
                  role="button"
                  tabindex="0"
               >
                  <div class="intel-entry-header">
                     <span class="intel-entry-title">{entry.title}</span>
                     <span class="badge {getCategoryBadge(entry.category)}"
                        >{entry.category || "reference"}</span
                     >
                  </div>
                  <div class="intel-entry-meta">
                     {#if entry.tags}
                        {#each parseTags(entry.tags) as tag}
                           <span class="tag-badge">{tag}</span>
                        {/each}
                     {/if}
                     {#if entry.createdAt}
                        <span class="text-muted text-xs"
                           >{timeAgo(entry.createdAt)}</span
                        >
                     {/if}
                  </div>
                  {#if entry.body}
                     <div class="intel-entry-body">
                        {#if isExpanded(entry.id)}
                           {entry.body}
                        {:else}
                           {bodyPreview(entry.body)}
                           {#if entry.body.length > 100}
                              <span
                                 class="text-xs text-muted"
                                 style="display: inline-block; margin-top: 6px;"
                                 >Show more…</span
                              >
                           {/if}
                        {/if}
                     </div>
                  {/if}
               </div>
            {/each}
         </div>
      {/if}
   </div>
{/if}
