<script>
   import {
      apiGet,
      apiPost,
      apiPut,
      apiDelete,
      parseTags,
      timeAgo,
      formatDate,
   } from "../lib/api.js";
   import { addToast, showConfirm } from "../lib/stores.js";
   import AliasText from "../components/AliasText.svelte";

   let { route } = $props();
   let campaignId = $derived(route.params.id);

   // ── Campaign state ──
   let campaign = $state(null);
   let loading = $state(true);

   // ── Tabs ──
   let tabs = $state([
      { key: "kanban", label: "Kanban" },
      { key: "sitreps", label: "SitReps" },
      { key: "intel", label: "Intel" },
   ]);
   let activeTab = $state("kanban");

   // ── Lanes ──
   let lanes = $state([
      { key: "backlog", label: "Backlog" },
      { key: "ready", label: "Ready" },
      { key: "in-progress", label: "In Progress" },
      { key: "review", label: "Review" },
      { key: "done", label: "Done" },
   ]);

   let nextLane = {
      backlog: "ready",
      ready: "in-progress",
      "in-progress": "review",
      review: "done",
      done: "done",
   };
   let prevLane = {
      backlog: "backlog",
      ready: "backlog",
      "in-progress": "ready",
      review: "in-progress",
      done: "review",
   };

   // ── Objectives ──
   let objectives = $state([]);
   let objectivesLoading = $state(false);

   // ── SitReps ──
   let sitreps = $state([]);
   let sitrepsLoading = $state(false);

   // ── Intel ──
   let intelEntries = $state([]);
   let intelLoading = $state(false);

   // ── Edit campaign modal ──
   let showEditModal = $state(false);
   let editForm = $state({
      title: "",
      description: "",
      tags: "",
   });

   // ── Objective modal ──
   let showObjectiveModal = $state(false);
   let objectiveForm = $state({
      title: "",
      description: "",
      lane: "backlog",
   });
   let editingObjectiveId = $state(null);
   let objectiveSaving = $state(false);

   // ── SitRep modal ──
   let showSitrepModal = $state(false);
   let sitrepForm = $state({
      summary: "",
      // Map of objective alias -> "Completed" | "Blocked" | "Next" | null
      selections: {},
   });
   let sitrepSaving = $state(false);
   let sitrepApiError = $state("");

   // ── Intel modal ──
   let showIntelModal = $state(false);
   let intelForm = $state({
      title: "",
      body: "",
      category: "Context",
      tags: "",
      source: "",
      campaignId: "",
   });
   let intelSaving = $state(false);

   // ── Expanded intel entries ──
   let expandedIntel = $state({});

   // ── Campaign statuses ──
   let campaignStatuses = $state([
      { key: "Planning", label: "Planning" },
      { key: "Active", label: "Active" },
      { key: "Completed", label: "Completed" },
      { key: "Abandoned", label: "Abandoned" },
   ]);

   // ── Load campaign ──
   async function loadCampaign() {
      loading = true;
      try {
         const data = await apiGet(`/api/campaigns/${campaignId}`);
         campaign = data;
      } catch (err) {
         addToast("Failed to load campaign.", "error");
      } finally {
         loading = false;
      }
   }

   // ── Load objectives ──
   async function loadObjectives() {
      objectivesLoading = true;
      try {
         const data = await apiGet(`/api/objectives?campaignId=${campaignId}`);
         objectives = Array.isArray(data) ? data : [];
      } catch (err) {
         addToast("Failed to load objectives.", "error");
         objectives = [];
      } finally {
         objectivesLoading = false;
      }
   }

   // ── Load sitreps ──
   async function loadSitreps() {
      sitrepsLoading = true;
      try {
         const data = await apiGet(`/api/sitrep?campaignId=${campaignId}`);
         sitreps = Array.isArray(data)
            ? data.sort(
                 (a, b) =>
                    new Date(b.createdAt || b.timestamp) -
                    new Date(a.createdAt || a.timestamp),
              )
            : [];
      } catch (err) {
         addToast("Failed to load sitreps.", "error");
         sitreps = [];
      } finally {
         sitrepsLoading = false;
      }
   }

   // ── Load intel ──
   async function loadIntel() {
      intelLoading = true;
      try {
         const data = await apiGet(`/api/intel?campaignId=${campaignId}`);
         intelEntries = Array.isArray(data) ? data : [];
      } catch (err) {
         addToast("Failed to load intel.", "error");
         intelEntries = [];
      } finally {
         intelLoading = false;
      }
   }

   // ── Tab change effect ──
   $effect(() => {
      if (!campaignId) return;
      if (!campaign) loadCampaign();
      if (activeTab === "kanban") {
         loadObjectives();
      } else if (activeTab === "sitreps") {
         loadSitreps();
      } else if (activeTab === "intel") {
         loadIntel();
      }
   });

   // ── Update campaign status ──
   async function updateStatus(status) {
      if (!campaign || status === campaign.status) return;
      try {
         const payload = {
            title: campaign.title || "",
            description: campaign.description || "",
            status: status,
            tags: Array.isArray(campaign.tags)
               ? campaign.tags.join(", ")
               : campaign.tags || "",
         };
         const updated = await apiPut(`/api/campaigns/${campaignId}`, payload);
         campaign = { ...campaign, ...updated };
         addToast("Status updated.", "success");
      } catch (err) {
         addToast("Failed to update status.", "error");
      }
   }

   // ── Open edit modal ──
   function openEditModal() {
      if (!campaign) return;
      editForm = {
         title: campaign.title || "",
         description: campaign.description || "",
         tags: Array.isArray(campaign.tags)
            ? campaign.tags.join(", ")
            : campaign.tags || "",
      };
      showEditModal = true;
   }

   // ── Save campaign edits ──
   async function saveCampaign() {
      try {
         const tags = parseTags(editForm.tags);
         const payload = {
            title: editForm.title,
            description: editForm.description,
            tags: Array.isArray(tags) ? tags.join(", ") : tags || "",
         };
         const updated = await apiPut(`/api/campaigns/${campaignId}`, payload);
         campaign = { ...campaign, ...updated };
         addToast("Campaign updated.", "success");
         showEditModal = false;
      } catch (err) {
         addToast("Failed to update campaign.", "error");
      }
   }

   // ── Delete campaign ──
   function deleteCampaign() {
      showConfirm(
         "Are you sure you want to delete this campaign? This cannot be undone.",
         async () => {
            try {
               await apiDelete(`/api/campaigns/${campaignId}`);
               addToast("Campaign deleted.", "success");
               window.location.hash = "#/";
            } catch (err) {
               addToast("Failed to delete campaign.", "error");
            }
         },
      );
   }

   // ── Move objective ──
   async function moveObjective(obj, newLane) {
      try {
         await apiPut(`/api/objectives/${obj.id}`, { lane: newLane });
         loadObjectives();
      } catch (err) {
         addToast("Failed to move objective.", "error");
      }
   }

   // ── Move to next lane ──
   function moveNext(obj) {
      const next = nextLane[obj.lane];
      if (next && next !== obj.lane) moveObjective(obj, next);
   }

   // ── Move to previous lane ──
   function movePrev(obj) {
      const prev = prevLane[obj.lane];
      if (prev && prev !== obj.lane) moveObjective(obj, prev);
   }

   // ── Open objective form (create) ──
   function openObjectiveForm(lane) {
      editingObjectiveId = null;
      objectiveForm = {
         title: "",
         description: "",
         lane: lane || "backlog",
      };
      showObjectiveModal = true;
   }

   // ── Open objective form (edit) ──
   function openObjectiveEdit(obj) {
      editingObjectiveId = obj.id;
      objectiveForm = {
         title: obj.title || "",
         description: obj.description || "",
         lane: obj.lane || "backlog",
      };
      showObjectiveModal = true;
   }

   // ── Save objective ──
   async function saveObjective() {
      objectiveSaving = true;
      try {
         const payload = {
            title: objectiveForm.title,
            description: objectiveForm.description,
            lane: objectiveForm.lane,
            campaignId: campaignId,
         };
         if (editingObjectiveId) {
            await apiPut(`/api/objectives/${editingObjectiveId}`, payload);
         } else {
            await apiPost(`/api/objectives`, payload);
         }
         addToast(
            `Objective ${editingObjectiveId ? "updated" : "created"}.`,
            "success",
         );
         showObjectiveModal = false;
         loadObjectives();
      } catch (err) {
         addToast(
            `Failed to ${editingObjectiveId ? "update" : "create"} objective.`,
            "error",
         );
      } finally {
         objectiveSaving = false;
      }
   }

   // ── Save sitrep ──
   async function saveSitrep() {
      sitrepSaving = true;
      sitrepApiError = "";
      const completed = [];
      const blocked = [];
      const next = [];
      for (const [alias, kind] of Object.entries(sitrepForm.selections)) {
         if (kind === "Completed") completed.push(parseInt(alias, 10));
         else if (kind === "Blocked") blocked.push(parseInt(alias, 10));
         else if (kind === "Next") next.push(parseInt(alias, 10));
      }
      try {
         await apiPost(`/api/sitrep`, {
            summary: sitrepForm.summary,
            campaignId: campaignId,
            source: "human",
            completed: completed.length > 0 ? completed : undefined,
            blocked: blocked.length > 0 ? blocked : undefined,
            next: next.length > 0 ? next : undefined,
         });
         addToast("SitRep posted.", "success");
         showSitrepModal = false;
         sitrepForm = { summary: "", selections: {} };
         loadSitreps();
      } catch (err) {
         const msg = err.message || "";
         // Try to parse structured 400 error
         try {
            const match = msg.match(/\{.*\}/s);
            if (match) {
               const body = JSON.parse(match[0]);
               if (body.aliases && Array.isArray(body.aliases)) {
                  const bad = body.aliases
                     .map((a) => `#${a}`)
                     .join(", ");
                  sitrepApiError = `Objective${body.aliases.length > 1 ? "s" : ""} ${bad} no longer exist${body.aliases.length > 1 ? "" : "s"}.`;
               } else if (body.error) {
                  sitrepApiError = body.error;
               } else {
                  sitrepApiError = "Failed to post sitrep.";
               }
            } else {
               sitrepApiError = "Failed to post sitrep.";
            }
         } catch {
            sitrepApiError = "Failed to post sitrep.";
         }
      } finally {
         sitrepSaving = false;
      }
   }

   // ── Save intel ──
   async function saveIntel() {
      intelSaving = true;
      try {
         const tags = parseTags(intelForm.tags);
         await apiPost(`/api/intel`, {
            title: intelForm.title,
            body: intelForm.body,
            category: intelForm.category,
            tags: Array.isArray(tags) ? tags.join(", ") : tags || "",
            source: intelForm.source,
            campaignId: campaignId,
         });
         addToast("Intel logged.", "success");
         showIntelModal = false;
         intelForm = {
            title: "",
            body: "",
            category: "Context",
            tags: "",
            source: "",
            campaignId: "",
         };
         loadIntel();
      } catch (err) {
         addToast("Failed to log intel.", "error");
      } finally {
         intelSaving = false;
      }
   }

   // ── Delete intel entry ──
   async function deleteIntel(entryId) {
      showConfirm("Delete this intel entry?", async () => {
         try {
            await apiDelete(`/api/intel/${entryId}`);
            addToast("Intel entry deleted.", "success");
            loadIntel();
         } catch (err) {
            addToast("Failed to delete intel entry.", "error");
         }
      });
   }

   // ── Open sitrep modal ──
   function openSitrepModal() {
      // Load objectives if not already loaded
      if (objectives.length === 0) loadObjectives();
      sitrepForm = { summary: "", selections: {} };
      sitrepApiError = "";
      showSitrepModal = true;
   }

   // ── Toggle intel expanded ──
   function toggleIntel(id) {
      expandedIntel[id] = !expandedIntel[id];
   }
</script>

{#if loading}
   <div class="loading">Loading campaign...</div>
{:else if !campaign}
   <div class="empty-state">Campaign not found.</div>
{:else}
   <!-- ── Campaign Header ── -->
   <div class="campaign-detail-header">
      <div
         style="display: flex; align-items: flex-start; justify-content: space-between; gap: 16px;"
      >
         <div>
            <h1 class="campaign-detail-title {campaign?.status.toLowerCase()}">
               {campaign.title}
            </h1>
            <div class="campaign-detail-meta">
               <span class="badge badge-{campaign.status?.toLowerCase() || ''}"
                  >{campaign.status}</span
               >
               {#if Array.isArray(campaign.tags)}
                  {#each campaign.tags as tag}
                     <span class="tag-badge">{tag}</span>
                  {/each}
               {:else if typeof campaign.tags === "string" && campaign.tags}
                  {#each parseTags(campaign.tags) as tag}
                     <span class="tag-badge">{tag}</span>
                  {/each}
               {/if}
            </div>
         </div>
         <div class="campaign-detail-actions" style="flex-shrink: 0;">
            <button class="btn btn-primary btn-sm" onclick={openEditModal}
               >Edit</button
            >
            <button class="btn btn-danger btn-sm" onclick={deleteCampaign}
               >Delete</button
            >
         </div>
      </div>
      {#if campaign.description}
         <p class="campaign-detail-desc"><AliasText text={campaign.description} campaignId={campaignId} /></p>
      {/if}
   </div>

   <!-- ── Status Buttons ── -->
   <div class="filters">
      {#each campaignStatuses as status}
         <button
            class="btn btn-sm {campaign?.status === status.key
               ? 'btn-primary'
               : 'btn-ghost'}"
            onclick={() => updateStatus(status.key)}
            disabled={campaign?.status === status.key}>{status.label}</button
         >
      {/each}
   </div>

   <!-- ── Tabs ── -->
   <div class="tabs">
      {#each tabs as tab}
         <button
            class="tab-btn {activeTab === tab.key ? 'active' : ''}"
            onclick={() => (activeTab = tab.key)}>{tab.label}</button
         >
      {/each}
   </div>

   <div class="tab-content">
      <!-- ═══ KANBAN TAB ═══ -->
      {#if activeTab === "kanban"}
         {#if objectivesLoading}
            <div class="loading">Loading objectives...</div>
         {:else}
            <div class="kanban-board">
               {#each lanes as lane}
                  <div class="kanban-column">
                     <div class="kanban-column-header">{lane.label}</div>
                     <div class="kanban-column-body">
                        {#each objectives.filter((o) => o.lane === lane.key) as obj (obj.id)}
                           <div
                              class="kanban-card"
                              onclick={() => openObjectiveEdit(obj)}
                              onkeydown={(e) => {
                                 if (e.key === "Enter" || e.key === " ") {
                                    e.preventDefault();
                                    openObjectiveEdit(obj);
                                 }
                              }}
                              role="button"
                              tabindex="0"
                           >
                              <div class="kanban-card-title">
                               {#if obj.alias != null}
                                  <span class="alias-chip-sm">#{obj.alias}</span>
                               {/if}
                               {obj.title}
                            </div>
                              {#if obj.description}
                                 <div class="kanban-card-meta">
                                    {obj.description}
                                 </div>
                              {/if}
                              <div class="kanban-card-meta">
                                 <button
                                    class="btn btn-ghost btn-sm"
                                    onclick={(e) => {
                                       e.stopPropagation();
                                       movePrev(obj);
                                    }}
                                    disabled={lane.key === "backlog"}>⇦</button
                                 >
                                 <button
                                    class="btn btn-ghost btn-sm"
                                    onclick={(e) => {
                                       e.stopPropagation();
                                       moveNext(obj);
                                    }}
                                    disabled={lane.key === "done"}>⇨</button
                                 >
                              </div>
                           </div>
                        {/each}
                     </div>
                     <button
                        class="kanban-add-btn"
                        onclick={() => openObjectiveForm(lane.key)}
                        >+ Add</button
                     >
                  </div>
               {/each}
            </div>
         {/if}

         <!-- ═══ SITREPS TAB ═══ -->
      {:else if activeTab === "sitreps"}
         <div class="campaign-actions">
            <button
               class="btn btn-primary"
               onclick={openSitrepModal}>Post SitRep</button
            >
         </div>
         {#if sitrepsLoading}
            <div class="loading">Loading sitreps...</div>
         {:else if sitreps.length === 0}
            <div class="empty-state">No sitreps yet. Post your first one!</div>
         {:else}
            <div class="sitrep-timeline">
               {#each sitreps as sitrep (sitrep.id || sitrep._id)}
                  <div class="sitrep-item">
                     <div class="sitrep-item-header">
                        <span class="alias-chip">#{sitrep.alias}</span>
                        <small class="sitrep-item-date"
                           >{timeAgo(
                              sitrep.createdAt || sitrep.timestamp,
                           )}</small
                        >
                        <span
                           class="badge badge-{sitrep.source === 'agent'
                              ? 'reference'
                              : 'active'} badge-sm"
                           >{sitrep.source || 'human'}</span
                        >
                     </div>
                     <div class="sitrep-item-summary">
                        <AliasText text={sitrep.summary} campaignId={campaignId} />
                     </div>
                     {#if sitrep.objectives?.length}
                        <div class="sitrep-item-obj-groups">
                           {#each ["Completed", "Blocked", "Next"] as groupKind}
                              {@const group = sitrep.objectives.filter((o) => o.kind === groupKind)}
                              {#if group.length > 0}
                                 <div class="sitrep-obj-group">
                                    <span class="sitrep-obj-group-label">{groupKind}</span>
                                    <div class="sitrep-obj-group-items">
                                       {#each group as ref}
                                          <span
                                             class="obj-ref-chip obj-ref-{groupKind.toLowerCase()}"
                                             >#{ref.objectiveAlias} {ref.titleSnapshot}
                                             <span class="text-muted"
                                                >({ref.laneAtWrite})</span
                                             ></span
                                          >
                                       {/each}
                                    </div>
                                 </div>
                              {/if}
                           {/each}
                        </div>
                     {/if}
                  </div>
               {/each}
            </div>
         {/if}

         <!-- ═══ INTEL TAB ═══ -->
      {:else if activeTab === "intel"}
         <div class="campaign-actions">
            <button
               class="btn btn-primary"
               onclick={() => {
                  intelForm.campaignId = campaignId;
                  showIntelModal = true;
               }}>Log Intel</button
            >
         </div>
         {#if intelLoading}
            <div class="loading">Loading intel...</div>
         {:else if intelEntries.length === 0}
            <div class="empty-state">No intel entries yet.</div>
         {:else}
            <div class="intel-feed">
               {#each intelEntries as entry (entry.id || entry._id)}
                  <div class="intel-tab-entry">
                     <div
                        class="intel-tab-entry-header"
                        onclick={() => toggleIntel(entry.id || entry._id)}
                        onkeydown={(e) => {
                           if (e.key === "Enter" || e.key === " ") {
                              e.preventDefault();
                              toggleIntel(entry.id || entry._id);
                           }
                        }}
                        role="button"
                        tabindex="0"
                     >
                        <strong class="intel-tab-entry-title">
                           {#if entry.alias != null}
                              <span class="alias-chip-sm">#{entry.alias}</span>
                           {/if}
                           {entry.title}
                        </strong>
                        <span
                           class="badge badge-{entry.category?.toLowerCase() ||
                              'reference'}"
                           >{entry.category || "reference"}</span
                        >
                     </div>
                     {#if expandedIntel[entry.id || entry._id]}
                        <div class="intel-tab-entry-body">
                           <p>{entry.body}</p>
                        </div>
                     {:else}
                        <div class="intel-tab-entry-body">
                           <p>
                              {entry.body?.length > 150
                                 ? entry.body.slice(0, 150) + "..."
                                 : entry.body}
                           </p>
                           {#if entry.body?.length > 150}
                              <button
                                 class="btn btn-ghost btn-sm"
                                 onclick={() =>
                                    toggleIntel(entry.id || entry._id)}
                                 >Read more</button
                              >
                           {/if}
                        </div>
                     {/if}
                     <div class="intel-tab-entry-meta">
                        {#if Array.isArray(entry.tags) && entry.tags.length > 0}
                           {#each entry.tags as tag}
                              <span class="tag-badge">{tag}</span>
                           {/each}
                        {:else if typeof entry.tags === "string" && entry.tags}
                           {#each parseTags(entry.tags) as tag}
                              <span class="tag-badge">{tag}</span>
                           {/each}
                        {/if}
                        <small class="text-muted"
                           >{timeAgo(entry.createdAt || entry.timestamp)}</small
                        >
                        <button
                           class="btn btn-danger btn-sm"
                           onclick={() => deleteIntel(entry.id || entry._id)}
                           >Delete</button
                        >
                     </div>
                  </div>
               {/each}
            </div>
         {/if}
      {/if}
   </div>

   <!-- ─── EDIT CAMPAIGN MODAL ─── -->
   {#if showEditModal}
      <div
         class="modal-overlay"
         onclick={() => (showEditModal = false)}
         role="presentation"
      >
         <div
            class="modal"
            onclick={(e) => e.stopPropagation()}
            onkeydown={(e) => {
               if (e.key === "Escape") showEditModal = false;
            }}
            role="dialog"
            tabindex="-1"
         >
            <div class="modal-header">
               <h2>Edit Campaign</h2>
            </div>
            <div class="modal-body">
               <div class="form-group">
                  <label class="form-label" for="edit-title">Title</label>
                  <input
                     id="edit-title"
                     class="form-input"
                     type="text"
                     bind:value={editForm.title}
                  />
               </div>
               <div class="form-group">
                  <label class="form-label" for="edit-description"
                     >Description</label
                  >
                  <textarea
                     id="edit-description"
                     class="form-textarea"
                     bind:value={editForm.description}></textarea>
               </div>
               <div class="form-group">
                  <label class="form-label" for="edit-tags"
                     >Tags (comma separated)</label
                  >
                  <input
                     id="edit-tags"
                     class="form-input"
                     type="text"
                     bind:value={editForm.tags}
                  />
               </div>
            </div>
            <div class="modal-actions">
               <button
                  class="btn btn-ghost"
                  onclick={() => (showEditModal = false)}>Cancel</button
               >
               <button class="btn btn-primary" onclick={saveCampaign}
                  >Save</button
               >
            </div>
         </div>
      </div>
   {/if}

   <!-- ─── OBJECTIVE MODAL ─── -->
   {#if showObjectiveModal}
      <div
         class="modal-overlay"
         onclick={() => (showObjectiveModal = false)}
         role="presentation"
      >
         <div
            class="modal"
            onclick={(e) => e.stopPropagation()}
            onkeydown={(e) => {
               if (e.key === "Escape") showObjectiveModal = false;
            }}
            role="dialog"
            tabindex="-1"
         >
            <div class="modal-header">
               <h2>{editingObjectiveId ? "Edit" : "Add"} Objective</h2>
            </div>
            <div class="modal-body">
               <div class="form-group">
                  <label class="form-label" for="obj-title">Title</label>
                  <input
                     id="obj-title"
                     class="form-input"
                     type="text"
                     bind:value={objectiveForm.title}
                  />
               </div>
               <div class="form-group">
                  <label class="form-label" for="obj-description"
                     >Description</label
                  >
                  <textarea
                     id="obj-description"
                     class="form-textarea"
                     bind:value={objectiveForm.description}></textarea>
               </div>
               <div class="form-group">
                  <label class="form-label" for="obj-lane">Lane</label>
                  <select
                     id="obj-lane"
                     class="form-select"
                     bind:value={objectiveForm.lane}
                  >
                     <option value="backlog">Backlog</option>
                     <option value="ready">Ready</option>
                     <option value="in-progress">In Progress</option>
                     <option value="review">Review</option>
                     <option value="done">Done</option>
                  </select>
               </div>
            </div>
            <div class="modal-actions">
               <button
                  class="btn btn-ghost"
                  onclick={() => (showObjectiveModal = false)}>Cancel</button
               >
               <button
                  class="btn btn-primary"
                  onclick={saveObjective}
                  disabled={objectiveSaving}
                  >{objectiveSaving
                     ? "Saving..."
                     : editingObjectiveId
                       ? "Update"
                       : "Create"}</button
               >
            </div>
         </div>
      </div>
   {/if}

   <!-- ─── SITREP MODAL ─── -->
   {#if showSitrepModal}
      <div
         class="modal-overlay"
         onclick={() => (showSitrepModal = false)}
         role="presentation"
      >
         <div
            class="modal"
            onclick={(e) => e.stopPropagation()}
            onkeydown={(e) => {
               if (e.key === "Escape") showSitrepModal = false;
            }}
            role="dialog"
            tabindex="-1"
         >
            <div class="modal-header">
               <h2>Post SitRep</h2>
            </div>
            <div class="modal-body">
               <div class="form-group">
                  <label class="form-label" for="sitrep-summary">Summary</label>
                  <textarea
                     id="sitrep-summary"
                     class="form-textarea"
                     bind:value={sitrepForm.summary}
                     placeholder="What happened this period? Use #N to reference objectives."></textarea>
               </div>

               <div class="form-group">
                  <span class="form-label" style="margin: 0 0 4px 0; display: block;"><strong>Reference Objectives</strong></span>
                  <p class="text-sm text-muted" style="margin: 0 0 8px 0;">
                     Tag each objective's role in this sitrep. Only objectives
                     with a role are submitted.
                  </p>
                  {#if objectives.length === 0}
                     <p class="text-sm text-muted">
                        No objectives yet. Create some on the Kanban tab first.
                     </p>
                  {:else}
                     <div class="sitrep-objective-picker">
                        {#each objectives as obj (obj.id)}
                           <div class="sitrep-obj-row">
                              <div class="sitrep-obj-info">
                                 <span class="alias-chip">#{obj.alias}</span>
                                 <span class="sitrep-obj-title">{obj.title}</span>
                                 <span
                                    class="badge badge-{obj.lane} badge-sm"
                                    >{obj.lane}</span
                                 >
                              </div>
                              <div class="segmented-control" role="radiogroup" aria-label="Role for #{obj.alias}">
                                 <button
                                    class="seg-btn {!sitrepForm.selections[obj.alias] ? 'seg-active' : ''}"
                                    onclick={() => {
                                       const s = { ...sitrepForm.selections };
                                       delete s[obj.alias];
                                       sitrepForm = { ...sitrepForm, selections: s };
                                    }}
                                    role="radio"
                                    aria-checked={!sitrepForm.selections[obj.alias]}
                                    tabindex="0"
                                    >None</button
                                 >
                                 <button
                                    class="seg-btn seg-completed {sitrepForm.selections[obj.alias] === 'Completed' ? 'seg-active' : ''}"
                                    onclick={() => {
                                       sitrepForm = {
                                          ...sitrepForm,
                                          selections: {
                                             ...sitrepForm.selections,
                                             [obj.alias]: "Completed",
                                          },
                                       };
                                    }}
                                    role="radio"
                                    aria-checked={sitrepForm.selections[obj.alias] === 'Completed'}
                                    tabindex="-1"
                                    >Done</button
                                 >
                                 <button
                                    class="seg-btn seg-blocked {sitrepForm.selections[obj.alias] === 'Blocked' ? 'seg-active' : ''}"
                                    onclick={() => {
                                       sitrepForm = {
                                          ...sitrepForm,
                                          selections: {
                                             ...sitrepForm.selections,
                                             [obj.alias]: "Blocked",
                                          },
                                       };
                                    }}
                                    role="radio"
                                    aria-checked={sitrepForm.selections[obj.alias] === 'Blocked'}
                                    tabindex="-1"
                                    >Blocked</button
                                 >
                                 <button
                                    class="seg-btn seg-next {sitrepForm.selections[obj.alias] === 'Next' ? 'seg-active' : ''}"
                                    onclick={() => {
                                       sitrepForm = {
                                          ...sitrepForm,
                                          selections: {
                                             ...sitrepForm.selections,
                                             [obj.alias]: "Next",
                                          },
                                       };
                                    }}
                                    role="radio"
                                    aria-checked={sitrepForm.selections[obj.alias] === 'Next'}
                                    tabindex="-1"
                                    >Next</button
                                 >
                              </div>
                           </div>
                        {/each}
                     </div>
                  {/if}
               </div>

               {#if sitrepApiError}
                  <div class="form-error" role="alert">{sitrepApiError}</div>
               {/if}
            </div>
            <div class="modal-actions">
               <button
                  class="btn btn-ghost"
                  onclick={() => (showSitrepModal = false)}>Cancel</button
               >
               <button
                  class="btn btn-primary"
                  onclick={saveSitrep}
                  disabled={sitrepSaving}
                  >{sitrepSaving ? "Posting..." : "Post"}</button
               >
            </div>
         </div>
      </div>
   {/if}

   <!-- ─── INTEL MODAL ─── -->
   {#if showIntelModal}
      <div
         class="modal-overlay"
         onclick={() => (showIntelModal = false)}
         role="presentation"
      >
         <div
            class="modal"
            onclick={(e) => e.stopPropagation()}
            onkeydown={(e) => {
               if (e.key === "Escape") showIntelModal = false;
            }}
            role="dialog"
            tabindex="-1"
         >
            <div class="modal-header">
               <h2>Log Intel</h2>
            </div>
            <div class="modal-body">
               <div class="form-group">
                  <label class="form-label" for="intel-title">Title</label>
                  <input
                     id="intel-title"
                     class="form-input"
                     type="text"
                     bind:value={intelForm.title}
                  />
               </div>
               <div class="form-group">
                  <label class="form-label" for="intel-body">Body</label>
                  <textarea
                     id="intel-body"
                     class="form-textarea"
                     bind:value={intelForm.body}></textarea>
               </div>
               <div class="form-group">
                  <label class="form-label" for="intel-category">Category</label
                  >
                  <select
                     id="intel-category"
                     class="form-select"
                     bind:value={intelForm.category}
                  >
                     <option value="Decision">Decision</option>
                     <option value="Context">Context</option>
                     <option value="Gotcha">Gotcha</option>
                     <option value="Reference">Reference</option>
                  </select>
               </div>
               <div class="form-group">
                  <label class="form-label" for="intel-tags"
                     >Tags (comma separated)</label
                  >
                  <input
                     id="intel-tags"
                     class="form-input"
                     type="text"
                     bind:value={intelForm.tags}
                  />
               </div>
               <div class="form-group">
                  <label class="form-label" for="intel-source">Source</label>
                  <input
                     id="intel-source"
                     class="form-input"
                     type="text"
                     bind:value={intelForm.source}
                  />
               </div>
            </div>
            <div class="modal-actions">
               <button
                  class="btn btn-ghost"
                  onclick={() => (showIntelModal = false)}>Cancel</button
               >
               <button
                  class="btn btn-primary"
                  onclick={saveIntel}
                  disabled={intelSaving}
                  >{intelSaving ? "Saving..." : "Log Intel"}</button
               >
            </div>
         </div>
      </div>
   {/if}
{/if}

<style>
   /* ── Alias chips ── */
   .alias-chip {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      padding: 1px 7px;
      border-radius: 100px;
      font-size: 0.7rem;
      font-weight: 700;
      line-height: 1.4;
      background: var(--color-olive-dark);
      color: var(--color-olive-light);
      white-space: nowrap;
   }
   .alias-chip-sm {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      padding: 0 5px;
      border-radius: 100px;
      font-size: 0.6rem;
      font-weight: 700;
      line-height: 1.5;
      background: var(--color-olive-dark);
      color: var(--color-olive-light);
      white-space: nowrap;
      margin-right: 4px;
      vertical-align: middle;
   }

   /* ── SitRep objective picker ── */
   .sitrep-objective-picker {
      display: flex;
      flex-direction: column;
      gap: 8px;
      max-height: 320px;
      overflow-y: auto;
      border: 1px solid var(--border-color);
      border-radius: var(--radius-sm);
      padding: 8px;
   }
   .sitrep-obj-row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 6px;
      padding: 6px 8px;
      border-radius: var(--radius-sm);
      background: var(--bg-surface);
   }
   .sitrep-obj-row:hover {
      background: var(--bg-hover);
   }
   .sitrep-obj-info {
      display: flex;
      align-items: center;
      gap: 6px;
      min-width: 0;
      flex: 1 1 auto;
   }
   .sitrep-obj-title {
      font-size: 0.85rem;
      color: var(--text-primary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
   }

   /* ── Segmented control ── */
   .segmented-control {
      display: inline-flex;
      border: 1px solid var(--border-color);
      border-radius: var(--radius-sm);
      overflow: hidden;
      flex-shrink: 0;
   }
   .seg-btn {
      padding: 3px 10px;
      font-size: 0.72rem;
      font-weight: 500;
      border: none;
      border-right: 1px solid var(--border-color);
      background: transparent;
      color: var(--text-muted);
      cursor: pointer;
      transition: all 0.12s ease;
      line-height: 1.5;
   }
   .seg-btn:last-child {
      border-right: none;
   }
   .seg-btn:hover {
      background: var(--bg-hover);
   }
   .seg-btn.seg-active {
      background: var(--color-olive);
      color: var(--text-inverse);
      font-weight: 600;
   }
   .seg-btn.seg-completed.seg-active {
      background: var(--color-olive);
      color: var(--text-inverse);
   }
   .seg-btn.seg-blocked.seg-active {
      background: var(--color-danger);
      color: var(--text-inverse);
   }
   .seg-btn.seg-next.seg-active {
      background: var(--color-khaki);
      color: var(--text-inverse);
   }

   /* ── SitRep timeline objective refs ── */
   .sitrep-item-header {
      display: flex;
      align-items: center;
      gap: 8px;
      flex-wrap: wrap;
   }
   .sitrep-item-obj-groups {
      display: flex;
      flex-direction: column;
      gap: 6px;
      margin-top: 10px;
   }
   .sitrep-obj-group {
      display: flex;
      flex-direction: column;
      gap: 3px;
   }
   .sitrep-obj-group-label {
      font-size: 0.68rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--text-muted);
   }
   .sitrep-obj-group-items {
      display: flex;
      flex-wrap: wrap;
      gap: 4px;
   }
   .obj-ref-chip {
      display: inline-flex;
      align-items: center;
      gap: 3px;
      padding: 2px 8px;
      border-radius: 100px;
      font-size: 0.75rem;
      font-weight: 500;
      line-height: 1.4;
      background: var(--bg-surface);
      border: 1px solid var(--border-color);
      color: var(--text-secondary);
   }
   .obj-ref-completed {
      border-color: var(--color-olive);
      background: color-mix(in srgb, var(--color-olive) 10%, var(--bg-card));
   }
   .obj-ref-blocked {
      border-color: var(--color-danger);
      background: color-mix(in srgb, var(--color-danger) 10%, var(--bg-card));
   }
   .obj-ref-next {
      border-color: var(--color-khaki);
      background: color-mix(in srgb, var(--color-khaki) 10%, var(--bg-card));
   }

   /* ── Form errors ── */
   .form-error {
      padding: 8px 12px;
      border-radius: var(--radius-sm);
      background: color-mix(in srgb, var(--color-danger) 12%, var(--bg-card));
      border: 1px solid var(--color-danger);
      color: var(--color-danger);
      font-size: 0.82rem;
      margin-top: 8px;
   }
</style>
