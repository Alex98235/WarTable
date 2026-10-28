<script>
   import {
      apiGet,
      apiPost,
      apiDelete,
      parseTags,
      timeAgo,
      formatDate,
   } from "../lib/api.js";
   import { addToast, showConfirm } from "../lib/stores.js";
   import MarkdownEditor from "../components/MarkdownEditor.svelte";

   let campaigns = $state([]);
   let loading = $state(true);
   let searchQuery = $state("");
   let statusFilter = $state("");
   let showCreateModal = $state(false);

   // New campaign form fields
   let newTitle = $state("");
   let newDescription = $state("");
   let newTags = $state([]);
   let tagInput = $state("");

   function addTag() {
      const vals = tagInput
         .split(",")
         .map((t) => t.trim())
         .filter((t) => t);
      for (const v of vals) {
         if (v && !newTags.includes(v)) newTags = [...newTags, v];
      }
      tagInput = "";
   }

   function removeTag(tag) {
      newTags = newTags.filter((t) => t !== tag);
   }

   function resetForm() {
      newTitle = "";
      newDescription = "";
      newTags = [];
      tagInput = "";
   }

   async function loadCampaigns() {
      loading = true;
      try {
         const params = {};
         if (searchQuery) params.search = searchQuery;
         if (statusFilter) params.status = statusFilter;
         const data = await apiGet("/api/campaigns", params);
         campaigns = data.campaigns ?? data;
      } catch (err) {
         addToast(err.message || "Failed to load campaigns", "error");
         campaigns = [];
      } finally {
         loading = false;
      }
   }

   async function createCampaign() {
      if (!newTitle || !newTitle.trim()) {
         addToast("Campaign title is required", "warning");
         return;
      }
      try {
         const body = {
            title: newTitle.trim(),
            description: newDescription.trim(),
            tags: newTags.join(", "),
         };
         await apiPost("/api/campaigns", body);
         addToast("Campaign created successfully", "success");
         showCreateModal = false;
         resetForm();
         await loadCampaigns();
      } catch (err) {
         addToast(err.message || "Failed to create campaign", "error");
      }
   }

   async function deleteCampaign(id, title) {
      showConfirm(`Are you sure you want to delete "${title}"?`, async () => {
         try {
            await apiDelete(`/api/campaigns/${id}`);
            addToast("Campaign deleted", "success");
            await loadCampaigns();
         } catch (err) {
            addToast(err.message || "Failed to delete campaign", "error");
         }
      });
   }

   function navigateTo(path) {
      window.location.hash = path;
   }

   function truncate(text, maxLen = 120) {
      if (!text) return "";
      return text.length > maxLen ? text.slice(0, maxLen) + "…" : text;
   }

   const statusOptions = ["", "Planning", "Active", "Completed", "Abandoned"];

   $effect(() => {
      loadCampaigns();
   });
</script>

<div class="filters-row">
   <input
      class="search-input"
      type="text"
      placeholder="Search campaigns…"
      bind:value={searchQuery}
      oninput={() => loadCampaigns()}
   />

   <select
      class="form-select"
      bind:value={statusFilter}
      onchange={() => loadCampaigns()}
   >
      <option value="">All Statuses</option>
      <option value="Planning">Planning</option>
      <option value="Active">Active</option>
      <option value="Completed">Completed</option>
      <option value="Abandoned">Abandoned</option>
   </select>

   <button
      class="btn btn-primary"
      onclick={() => {
         resetForm();
         showCreateModal = true;
      }}
   >
      + New Campaign
   </button>
</div>

{#if loading}
   <div class="loading">
      <p>Loading campaigns…</p>
   </div>
{:else if campaigns.length === 0}
   <div class="empty-state">
      <h3>No campaigns found</h3>
      <p>
         {#if searchQuery || statusFilter}
            Try adjusting your search or filters.
         {:else}
            Create your first campaign to get started.
         {/if}
      </p>
   </div>
{:else}
   <div class="card-grid">
      {#each campaigns as campaign (campaign.id)}
         <div
            class="campaign-card"
            onclick={() => navigateTo(`/campaign/${campaign.id}`)}
            onkeydown={(e) => {
               if (e.key === "Enter" || e.key === " ") {
                  e.preventDefault();
                  navigateTo(`/campaign/${campaign.id}`);
               }
            }}
            role="button"
            tabindex="0"
         >
            <div class="campaign-card-title {campaign?.status.toLowerCase()}">
               {campaign.title}
            </div>
            <div class="campaign-card-meta">
               <span
                  class="badge badge-{(
                     campaign.status || 'planning'
                  ).toLowerCase()}">{campaign.status || "planning"}</span
               >
            </div>
            <div class="campaign-card-desc">
               <p>{truncate(campaign.description)}</p>
            </div>
            {#if campaign.tags}
               <div class="campaign-card-meta" style="margin-top: 8px;">
                  {#each parseTags(campaign.tags) as tag}
                     <span class="tag-badge">{tag}</span>
                  {/each}
               </div>
            {/if}
            <div class="campaign-card-footer">
               <span class="text-muted">{timeAgo(campaign.createdAt)}</span>
               <button
                  class="btn btn-danger btn-sm"
                  onclick={(e) => {
                     e.stopPropagation();
                     deleteCampaign(campaign.id, campaign.title);
                  }}>Delete</button
               >
            </div>
         </div>
      {/each}
   </div>
{/if}

{#if showCreateModal}
   <div
      class="modal-overlay"
      onclick={(e) => {
         if (e.target === e.currentTarget) showCreateModal = false;
      }}
      onkeydown={(e) => {
         if (e.key === "Escape") showCreateModal = false;
      }}
      role="dialog"
      tabindex="-1"
   >
      <div class="modal" role="document">
         <div class="modal-header">
            <h3>New Campaign</h3>
            <button
               class="btn btn-ghost btn-sm"
               onclick={() => (showCreateModal = false)}>×</button
            >
         </div>
         <form
            class="modal-body"
            onsubmit={(e) => {
               e.preventDefault();
               createCampaign();
            }}
         >
            <div class="form-group">
               <label class="form-label" for="campaign-title">Title</label>
               <input
                  id="campaign-title"
                  class="form-input"
                  type="text"
                  bind:value={newTitle}
                  placeholder="Campaign title"
                  required
               />
            </div>

            <div class="form-group">
               <label class="form-label" for="campaign-description"
                  >Description</label
               >
               <MarkdownEditor
                  id="campaign-description"
                  bind:value={newDescription}
                  campaignId=""
                  placeholder="Campaign description (optional)"
                  rows="4"
               />
            </div>

            <div class="form-group">
               <label class="form-label" for="campaign-tags">Tags</label>
               <div class="tag-input-group">
                  <input
                     id="campaign-tags"
                     class="form-input"
                     type="text"
                     bind:value={tagInput}
                     placeholder="Type a tag and press comma…"
                     onkeydown={(e) => {
                        if (e.key === "," || e.key === "Enter") {
                           e.preventDefault();
                           addTag();
                        }
                     }}
                  />
                  {#if newTags.length > 0}
                     <div class="tag-tokens">
                        {#each newTags as tag}
                           <span class="tag-token">
                              {tag}
                              <button
                                 onclick={() => removeTag(tag)}
                                 aria-label="Remove tag {tag}">×</button
                              >
                           </span>
                        {/each}
                     </div>
                  {/if}
               </div>
            </div>

            <div class="modal-actions">
               <button
                  type="button"
                  class="btn btn-ghost"
                  onclick={() => {
                     showCreateModal = false;
                     resetForm();
                  }}>Cancel</button
               >
               <button type="submit" class="btn btn-primary"
                  >Create Campaign</button
               >
            </div>
         </form>
      </div>
   </div>
{/if}
