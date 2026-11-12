<script>
	import {
		ACTOR,
		NOTE_CREATE_KINDS,
		NOTE_KINDS,
		NOTE_STATUSES,
		apiDelete,
		apiGet,
		apiPost,
		apiPut,
		parseTags,
		plainText,
		stepRef,
		timeAgo,
		formatDate
	} from '../lib/api.js';
	import { addToast, showConfirm } from '../lib/stores.js';
	import { onActivity } from '../lib/events.svelte.js';
	import Markdown from '../components/Markdown.svelte';
	import MarkdownEditor from '../components/MarkdownEditor.svelte';
	import StatusBadge from '../components/StatusBadge.svelte';

	let { route } = $props();
	let campaignId = $derived(route.params.id);
	let alias = $derived(route.params.alias ? parseInt(route.params.alias, 10) : null);

	let campaign = $state(null);
	let objectives = $state([]);
	let allNotes = $state([]);
	let notes = $state([]);
	let loading = $state(true);
	let notesLoading = $state(false);
	let refreshing = $state(false);
	let error = $state('');
	let loadToken = 0;
	let notesToken = 0;
	let searchTimer = null;

	let search = $state('');
	let kindFilter = $state('');
	let statusFilter = $state('');
	let includeDeleted = $state(false);
	let includeDormant = $state(false);

	// Semantic search is additive: browse mode above stays the default.
	let knowledgeStatus = $state(null);
	let semanticMode = $state(false);
	let semanticResults = $state([]);
	let semanticQuery = $state('');
	let semanticLoading = $state(false);
	let semanticDegraded = $state(false);
	let semanticReason = $state('');
	let semanticError = $state('');
	let semanticToken = 0;

	let linkedSteps = $state([]);
	let linkedToken = 0;

	let showCapture = $state(false);
	let captureForm = $state({
		kind: 'Context',
		title: '',
		body: '',
		tags: '',
		pinned: false,
		objectiveIds: [],
		stepIds: [],
		supersedesId: ''
	});
	let captureSteps = $state([]);
	let captureLoadingSteps = $state(false);
	let captureSaving = $state(false);

	let editing = $state(null);
	let editForm = $state({
		title: '',
		body: '',
		tags: '',
		pinned: false,
		supersedesId: ''
	});
	let editSaving = $state(false);

	let objectiveById = $derived(
		Object.fromEntries(objectives.map((objective) => [objective.id, objective]))
	);

	let selectedNote = $derived(
		alias != null ? allNotes.find((note) => note.alias === alias) || null : null
	);
	let selectedMissing = $derived(alias != null && !selectedNote && !notesLoading);

	let pinnedNotes = $derived(
		allNotes.filter(
			(note) =>
				!note.deletedAt &&
				!note.dormant &&
				note.kind === 'Directive' &&
				note.status === 'Active' &&
				note.pinned
		)
	);

	let supersedesNote = $derived(
		selectedNote?.supersedesId
			? allNotes.find((note) => note.id === selectedNote.supersedesId) || null
			: null
	);

	let supersededBy = $derived(
		selectedNote ? allNotes.filter((note) => note.supersedesId === selectedNote.id) : []
	);

	let linkedObjectives = $derived(
		(selectedNote?.linkedObjectiveIds || []).map((id) => objectiveById[id]).filter(Boolean)
	);

	let noteOptions = $derived(
		allNotes.filter((note) => !note.deletedAt && note.status === 'Active')
	);

	// The edit modal cannot clear a supersession (the backend treats null as
	// "unchanged"), so the current target must stay selectable even if it has
	// since been marked Superseded.
	let editSupersedesOptions = $derived.by(() => {
		if (!editing) return [];
		return allNotes.filter(
			(note) =>
				note.id !== editing.id &&
				(!note.deletedAt || note.id === editing.supersedesId) &&
				(note.status === 'Active' || note.id === editing.supersedesId)
		);
	});

	let hasFilters = $derived(
		semanticMode
			? search !== '' || kindFilter !== '' || statusFilter !== ''
			: search !== '' ||
					kindFilter !== '' ||
					statusFilter !== '' ||
					includeDeleted ||
					includeDormant
	);

	let semanticAvailable = $derived(knowledgeStatus?.enabled === true);
	let indexBuilding = $derived(semanticAvailable && knowledgeStatus?.ready === false);

	// Kind and status are applied to the returned matches, never to the request:
	// the index decides relevance, the filters just narrow what is shown.
	let semanticMatches = $derived(
		semanticResults.filter(
			(match) =>
				(!kindFilter || match.kind === kindFilter) &&
				(!statusFilter || match.status === statusFilter)
		)
	);

	async function load({ silent = false } = {}) {
		if (silent) refreshing = true;
		else loading = true;
		error = '';
		const token = ++loadToken;
		try {
			const [campaignData, objectiveData, allNoteData] = await Promise.all([
				apiGet(`/api/campaigns/${campaignId}`),
				apiGet('/api/objectives', { campaignId }),
				apiGet('/api/notes', { campaignId, includeDeleted: 'true', includeDormant: 'true' })
			]);
			if (token !== loadToken) return;
			campaign = campaignData;
			objectives = Array.isArray(objectiveData) ? objectiveData : [];
			allNotes = Array.isArray(allNoteData) ? allNoteData : [];
			// Activity refreshes must not swap semantic matches for browse notes.
			if (semanticMode) {
				if (semanticQuery) await runSemanticSearch({ silent: true, query: semanticQuery });
			} else {
				await loadNotes({ silent: true, token });
			}
		} catch (err) {
			if (token !== loadToken) return;
			error = err.message || 'Could not open the Bunker.';
			addToast('Could not load the Bunker.', 'error');
		} finally {
			if (token === loadToken) {
				loading = false;
				refreshing = false;
			}
		}
	}

	async function loadNotes({ silent = false, token = loadToken } = {}) {
		if (!silent) notesLoading = true;
		const requestToken = ++notesToken;
		try {
			const params = { campaignId };
			if (search.trim()) params.search = search.trim();
			if (kindFilter) params.kind = kindFilter;
			if (statusFilter) params.status = statusFilter;
			if (includeDeleted) params.includeDeleted = 'true';
			if (includeDormant) params.includeDormant = 'true';
			const data = await apiGet('/api/notes', params);
			if (requestToken !== notesToken || token !== loadToken) return;
			notes = Array.isArray(data) ? data : [];
		} catch {
			if (requestToken !== notesToken) return;
			addToast('Could not filter Bunker notes.', 'error');
		} finally {
			if (requestToken === notesToken) notesLoading = false;
		}
	}

	function onSearchInput() {
		if (semanticMode) return;
		clearTimeout(searchTimer);
		searchTimer = setTimeout(() => loadNotes({ silent: true }), 260);
	}

	function submitSearch(event) {
		if (event.key !== 'Enter' || !semanticMode) return;
		event.preventDefault();
		runSemanticSearch();
	}

	function applyFilters() {
		if (semanticMode) return;
		loadNotes({ silent: true });
	}

	function clearFilters() {
		clearTimeout(searchTimer);
		search = '';
		kindFilter = '';
		statusFilter = '';
		includeDeleted = false;
		includeDormant = false;
		if (semanticMode) {
			semanticToken += 1;
			semanticQuery = '';
			semanticResults = [];
			semanticLoading = false;
			semanticDegraded = false;
			semanticReason = '';
			semanticError = '';
		} else {
			loadNotes({ silent: true });
		}
	}

	function toggleSemantic() {
		const next = !semanticMode;
		clearTimeout(searchTimer);
		semanticMode = next;
		semanticError = '';
		if (next) {
			includeDeleted = false;
			includeDormant = false;
			// Turning the toggle on is never a search. Fresh text just waits.
			if (search.trim() !== semanticQuery) {
				semanticToken += 1;
				semanticQuery = '';
				semanticResults = [];
				semanticLoading = false;
				semanticDegraded = false;
				semanticReason = '';
			}
		} else {
			loadNotes({ silent: true });
		}
	}

	async function runSemanticSearch({ silent = false, query = null } = {}) {
		const term = String(query ?? search).trim();
		if (!term) {
			semanticToken += 1;
			semanticQuery = '';
			semanticResults = [];
			semanticLoading = false;
			semanticDegraded = false;
			semanticReason = '';
			semanticError = '';
			return;
		}
		const token = ++semanticToken;
		if (!silent) semanticLoading = true;
		semanticError = '';
		try {
			const data = await apiGet('/api/knowledge/search', {
				campaignId,
				q: term,
				mode: 'hybrid',
				limit: 8
			});
			if (token !== semanticToken) return;
			semanticQuery = term;
			semanticResults = Array.isArray(data?.results) ? data.results : [];
			semanticDegraded = data?.degraded === true;
			semanticReason = data?.degradationReason || '';
		} catch (err) {
			if (token !== semanticToken) return;
			semanticResults = [];
			semanticDegraded = false;
			semanticReason = '';
			semanticError = err.message || 'Semantic search is unavailable right now.';
		} finally {
			if (token === semanticToken) semanticLoading = false;
		}
	}

	async function loadKnowledgeStatus() {
		try {
			const data = await apiGet('/api/knowledge/status');
			knowledgeStatus = data && typeof data === 'object' ? data : null;
		} catch {
			// Non-fatal: without status the add-on is simply not offered.
			knowledgeStatus = null;
		}
	}

	function escapeRegExp(value) {
		return String(value).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
	}

	/** Split a snippet into plain text and query hits, without touching HTML. */
	function highlightParts(text, query) {
		const source = String(text || '');
		if (!source) return [{ text: '', hit: false }];
		const terms = [
			...new Set(
				String(query || '')
					.split(/\s+/)
					.map((term) => term.trim())
					.filter((term) => term.length > 1)
			)
		]
			.sort((a, b) => b.length - a.length)
			.slice(0, 8);
		if (terms.length === 0) return [{ text: source, hit: false }];
		const pattern = new RegExp(terms.map(escapeRegExp).join('|'), 'gi');
		const parts = [];
		let last = 0;
		for (const match of source.matchAll(pattern)) {
			if (match.index > last)
				parts.push({ text: source.slice(last, match.index), hit: false });
			parts.push({ text: match[0], hit: true });
			last = match.index + match[0].length;
		}
		if (last < source.length) parts.push({ text: source.slice(last), hit: false });
		return parts.length > 0 ? parts : [{ text: source, hit: false }];
	}

	async function loadLinkedSteps(stepIds) {
		if (!stepIds || stepIds.length === 0) {
			linkedSteps = [];
			return;
		}
		const token = ++linkedToken;
		const results = await Promise.all(
			stepIds.map((id) => apiGet(`/api/steps/${id}`).catch(() => null))
		);
		if (token !== linkedToken) return;
		linkedSteps = results.filter(Boolean);
	}

	$effect(() => {
		const note = selectedNote;
		loadLinkedSteps(note?.linkedStepIds || []);
	});

	function go(path) {
		window.location.hash = path;
	}

	function noteHref(note) {
		return `#/campaign/${campaignId}/bunker/${note.alias}`;
	}

	/** Dormant notes show how long they have been dormant where the list shows recency. */
	function noteAge(note) {
		if (note?.dormant && note.dormantDays != null) return `Dormant ${note.dormantDays}d`;
		return timeAgo(note.updatedAt);
	}

	async function openCapture(prefill = {}) {
		captureForm = {
			kind: 'Context',
			title: '',
			body: '',
			tags: '',
			pinned: false,
			objectiveIds: [],
			stepIds: [],
			supersedesId: '',
			...prefill
		};
		showCapture = true;
		await loadCaptureSteps();
	}

	async function loadCaptureSteps() {
		captureLoadingSteps = true;
		try {
			const details = await Promise.all(
				objectives.map((objective) =>
					apiGet(`/api/objectives/${objective.id}`).catch(() => null)
				)
			);
			captureSteps = details.filter(Boolean).flatMap((objective) =>
				(objective.steps || []).map((step) => ({
					...step,
					objectiveAlias: objective.alias,
					objectiveTitle: objective.title
				}))
			);
		} finally {
			captureLoadingSteps = false;
		}
	}

	async function createNote(event) {
		event.preventDefault();
		if (!captureForm.title.trim()) {
			addToast('A note needs a title.', 'warning');
			return;
		}
		captureSaving = true;
		try {
			const created = await apiPost('/api/notes', {
				campaignId,
				kind: captureForm.kind,
				title: captureForm.title.trim(),
				body: captureForm.body,
				tags: parseTags(captureForm.tags).join(', '),
				pinned: captureForm.pinned,
				objectiveIds: captureForm.objectiveIds,
				stepIds: captureForm.stepIds,
				supersedesId: captureForm.supersedesId || null,
				actor: ACTOR
			});
			addToast('Note captured in the Bunker.', 'success');
			showCapture = false;
			await load({ silent: true });
			if (created?.alias != null) go(`/campaign/${campaignId}/bunker/${created.alias}`);
		} catch (err) {
			addToast(err.message || 'Could not capture the note.', 'error');
		} finally {
			captureSaving = false;
		}
	}

	function startEdit(note) {
		editing = note;
		editForm = {
			title: note.title || '',
			body: note.body || '',
			tags: note.tags || '',
			pinned: !!note.pinned,
			supersedesId: note.supersedesId || ''
		};
	}

	async function saveEdit(event) {
		event.preventDefault();
		if (!editing) return;
		editSaving = true;
		try {
			await apiPut(`/api/notes/${editing.id}`, {
				title: editForm.title.trim(),
				body: editForm.body,
				tags: parseTags(editForm.tags).join(', '),
				pinned: editForm.pinned,
				supersedesId: editForm.supersedesId || null,
				actor: ACTOR
			});
			addToast('Note updated.', 'success');
			editing = null;
			await load({ silent: true });
		} catch (err) {
			addToast(err.message || 'Could not save the note.', 'error');
		} finally {
			editSaving = false;
		}
	}

	async function togglePin(note) {
		try {
			await apiPut(`/api/notes/${note.id}`, { pinned: !note.pinned, actor: ACTOR });
			addToast(note.pinned ? 'Note unpinned.' : 'Note pinned.', 'success');
			await load({ silent: true });
		} catch (err) {
			addToast(err.message || 'Could not change the pin.', 'error');
		}
	}

	function deleteNote(note) {
		showConfirm(
			`Delete note #${note.alias} "${note.title}"? The alias will still resolve for history.`,
			async () => {
				try {
					await apiDelete(`/api/notes/${note.id}`, { actor: ACTOR });
					addToast('Note deleted.', 'success');
					go(`/campaign/${campaignId}/bunker`);
					await load({ silent: true });
				} catch (err) {
					addToast(err.message || 'Could not delete the note.', 'error');
				}
			}
		);
	}

	function kindDefaultPinned(kind) {
		captureForm.kind = kind;
		if (kind === 'Directive') captureForm.pinned = true;
	}

	load();
	loadKnowledgeStatus();

	$effect(() => {
		return onActivity(() => load({ silent: true }));
	});
</script>

<svelte:head>
	<title>WarTable — Bunker</title>
</svelte:head>

<div class="page-intro reveal" style="--i: 0">
	<div>
		<a class="back-link" href="#/campaign/{campaignId}">← {campaign?.title || 'Campaign'}</a>
		<p class="eyebrow">Campaign knowledge</p>
		<h1 class="page-title">The Bunker</h1>
		<p class="lede">
			Directives are always applied. Decisions, gotchas, and context are searched when they
			matter. The charter frames the campaign. Nothing here is a status report.
		</p>
	</div>
	<div class="page-actions">
		{#if refreshing}<span class="sync-note">Syncing…</span>{/if}
		<button type="button" class="btn btn-primary" onclick={() => openCapture()}
			>+ Capture note</button
		>
	</div>
</div>

{#if loading}
	<div class="loading-state">
		<div class="loading-spinner"></div>
		<p>Opening the Bunker…</p>
	</div>
{:else if error}
	<div class="error-state">
		<p class="eyebrow">Connection trouble</p>
		<h1>The Bunker could not be opened.</h1>
		<p class="lede">{error}</p>
		<div class="page-actions">
			<button type="button" class="btn btn-primary" onclick={() => load()}>Try again</button>
			<a class="btn btn-secondary" href="#/campaigns">Back to campaigns</a>
		</div>
	</div>
{:else}
	{#if pinnedNotes.length > 0}
		<section class="pinned-zone reveal" style="--i: 1">
			<div class="pinned-zone-head">
				<div>
					<p class="eyebrow">Always applied</p>
					<h2 class="section-title">Pinned directives</h2>
				</div>
				<span class="inbox-count">{pinnedNotes.length}</span>
			</div>
			<div class="directive-grid">
				{#each pinnedNotes as note (note.id)}
					<a class="directive-card" href={noteHref(note)}>
						<span class="directive-top">
							<span class="ref-chip">#{note.alias}</span>
							<span class="pin-mark" title="Pinned directive">◆</span>
						</span>
						<span class="directive-title">{note.title}</span>
						<span class="directive-body">{plainText(note.body, 130)}</span>
					</a>
				{/each}
			</div>
		</section>
	{/if}

	<div class="filter-bar reveal" class:is-semantic={semanticMode} style="--i: 2">
		<label class="search-field">
			<span class="search-glyph" aria-hidden="true">⌕</span>
			<span class="sr-only">
				{semanticMode ? 'Search notes by meaning' : 'Search notes'}
			</span>
			<input
				type="search"
				enterkeyhint="search"
				placeholder={semanticMode
					? 'Search meaning… (press Enter)'
					: 'Search title, body, or tags…'}
				bind:value={search}
				oninput={onSearchInput}
				onkeydown={submitSearch}
			/>
		</label>

		{#if semanticAvailable}
			<button
				type="button"
				class="semantic-toggle"
				aria-pressed={semanticMode}
				title="Search by meaning instead of words"
				onclick={toggleSemantic}
			>
				<span class="semantic-toggle-mark" aria-hidden="true">◇</span>
				<span>Semantic</span>
			</button>
		{/if}

		<label class="select-field">
			<span class="sr-only">Filter by kind</span>
			<select class="form-select" bind:value={kindFilter} onchange={applyFilters}>
				<option value="">All kinds</option>
				{#each NOTE_KINDS as kind}<option value={kind}>{kind}</option>{/each}
			</select>
		</label>

		<label class="select-field">
			<span class="sr-only">Filter by status</span>
			<select class="form-select" bind:value={statusFilter} onchange={applyFilters}>
				<option value="">All statuses</option>
				{#each NOTE_STATUSES as status}<option value={status}>{status}</option>{/each}
			</select>
		</label>

		{#if !semanticMode}
			<label class="check-row compact">
				<input type="checkbox" bind:checked={includeDeleted} onchange={applyFilters} />
				<span>Show deleted</span>
			</label>
			<label class="check-row compact">
				<input type="checkbox" bind:checked={includeDormant} onchange={applyFilters} />
				<span>Show dormant</span>
			</label>
		{/if}

		{#if hasFilters}
			<button type="button" class="btn btn-ghost btn-sm" onclick={clearFilters}>Clear</button>
		{/if}
		<span class="filter-spacer"></span>
		{#if semanticMode}
			{#if semanticQuery && !semanticLoading}
				<span class="result-count"
					>{semanticMatches.length} match{semanticMatches.length === 1 ? '' : 'es'}</span
				>
			{/if}
		{:else}
			<span class="result-count">{notes.length} note{notes.length === 1 ? '' : 's'}</span>
		{/if}
	</div>

	<div class="bunker-layout" class:has-detail={!!selectedNote}>
		<section class="bunker-list panel">
			{#if semanticMode}
				<header class="panel-head compact">
					<div class="panel-heading">
						<p class="eyebrow">Meaning search</p>
						<h2 class="panel-title">Matches</h2>
					</div>
					{#if semanticLoading}<span class="sync-note">Searching…</span>{/if}
				</header>

				{#if semanticDegraded}
					<div class="match-banner is-degraded" role="status">
						<p class="match-banner-line">
							Semantic index unavailable — showing keyword matches.
						</p>
						{#if semanticReason}
							<p class="match-banner-detail">{semanticReason}</p>
						{/if}
					</div>
				{:else if indexBuilding}
					<div class="match-banner" role="status">
						<p class="match-banner-line">
							Index is building — results may be incomplete.
						</p>
					</div>
				{/if}

				{#if semanticError}
					<div class="inline-empty" title={semanticError}>
						Semantic search did not answer. Press Enter to try again.
					</div>
				{:else if semanticLoading}
					<div class="inline-empty semantic-loading">Searching by meaning…</div>
				{:else if !semanticQuery}
					<div class="semantic-prompt">
						<span class="semantic-prompt-mark" aria-hidden="true">◇</span>
						{#if search.trim()}
							<p class="semantic-prompt-title">Ready when you are</p>
							<p class="semantic-prompt-body">
								Press Enter to search by meaning. Nothing runs until you do.
							</p>
						{:else}
							<p class="semantic-prompt-title">Search by meaning</p>
							<p class="semantic-prompt-body">
								Type what you are trying to recall, then press Enter. Related notes
								can surface even when the words differ.
							</p>
						{/if}
					</div>
				{:else if semanticMatches.length === 0}
					<div class="inline-empty">
						{#if semanticResults.length > 0}
							No matches pass those filters.
						{:else if indexBuilding}
							Nothing surfaced yet — the index has not finished building.
						{:else}
							Nothing surfaced for that search. Try describing it differently.
						{/if}
					</div>
				{:else}
					<ul class="note-list match-list">
						{#each semanticMatches as match (match.noteId)}
							{@const parts = highlightParts(match.snippet, semanticQuery)}
							<li>
								<a
									class="match-card"
									class:is-selected={selectedNote?.id === match.noteId}
									href={noteHref(match)}
								>
									<span class="match-card-top">
										<span class="ref-chip">#{match.alias}</span>
										<StatusBadge status={match.kind} sm />
										<StatusBadge status={match.status} sm />
										<span class="note-age">{timeAgo(match.updatedAt)}</span>
										<span class="match-chip match-chip-{match.matchedBy}"
											>{match.matchedBy}</span
										>
									</span>
									<span class="match-card-title">{match.title}</span>
									{#if match.snippet}
										<span class="match-snippet"
											>{#each parts as part}{#if part.hit}<mark
														>{part.text}</mark
													>{:else}{part.text}{/if}{/each}</span
										>
									{/if}
									{#if match.headingPath}
										<span class="match-context">
											<span class="match-context-mark" aria-hidden="true"
												>↳</span
											>
											under {match.headingPath}
										</span>
									{/if}
								</a>
							</li>
						{/each}
					</ul>
				{/if}
			{:else}
				<header class="panel-head compact">
					<div class="panel-heading">
						<p class="eyebrow">Knowledgebase</p>
						<h2 class="panel-title">Notes</h2>
					</div>
					{#if notesLoading}<span class="sync-note">Filtering…</span>{/if}
				</header>

				{#if notes.length === 0}
					<div class="inline-empty">
						{hasFilters
							? 'No notes match those filters.'
							: 'The Bunker is empty. Capture a directive, decision, gotcha, or context note.'}
					</div>
				{:else}
					<ul class="note-list">
						{#each notes as note (note.id)}
							<li>
								<a
									class="note-card"
									class:is-selected={selectedNote?.id === note.id}
									class:is-deleted={!!note.deletedAt}
									href={noteHref(note)}
								>
									<span class="note-card-top">
										<span class="ref-chip">#{note.alias}</span>
										<StatusBadge status={note.kind} sm />
										{#if note.dormant}<StatusBadge
												status="Dormant"
												tone="muted"
												sm
											/>{/if}
										{#if note.pinned}<span class="pin-mark" title="Pinned"
												>◆</span
											>{/if}
										<span class="note-age">{noteAge(note)}</span>
									</span>
									<span class="note-card-title">{note.title}</span>
									<span class="note-card-excerpt"
										>{plainText(note.body, 120)}</span
									>
									<span class="note-card-foot">
										<StatusBadge status={note.status} sm />
										{#each parseTags(note.tags).slice(0, 3) as tag}
											<span class="tag">{tag}</span>
										{/each}
										{#if note.deletedAt}<span class="deleted-mark">deleted</span
											>{/if}
									</span>
								</a>
							</li>
						{/each}
					</ul>
				{/if}
			{/if}
		</section>

		<section class="bunker-detail">
			{#if selectedNote}
				<article class="panel note-detail">
					<header class="note-detail-head">
						<div>
							<a class="back-link small" href="#/campaign/{campaignId}/bunker"
								>← All notes</a
							>
							<div class="note-detail-ids">
								<span class="ref-chip large">#{selectedNote.alias}</span>
								<StatusBadge status={selectedNote.kind} />
								{#if selectedNote.dormant}<StatusBadge
										status="Dormant"
										tone="muted"
									/>{/if}
								<StatusBadge status={selectedNote.status} />
								{#if selectedNote.pinned}<span class="pin-mark" title="Pinned"
										>◆</span
									>{/if}
								{#if selectedNote.deletedAt}<span class="deleted-mark">deleted</span
									>{/if}
							</div>
							<h1 class="note-detail-title">{selectedNote.title}</h1>
						</div>
						<div class="note-detail-actions">
							<button
								type="button"
								class="btn btn-secondary btn-sm"
								onclick={() => startEdit(selectedNote)}>Edit</button
							>
							<button
								type="button"
								class="btn btn-ghost btn-sm"
								onclick={() => togglePin(selectedNote)}
							>
								{selectedNote.pinned ? 'Unpin' : 'Pin'}
							</button>
							<button
								type="button"
								class="btn btn-ghost btn-sm danger-hover"
								onclick={() => deleteNote(selectedNote)}>Delete</button
							>
						</div>
					</header>

					{#if selectedNote.deletedAt}
						<div class="notice notice-danger">
							This note is soft-deleted. The alias still resolves so old references
							stay meaningful.
						</div>
					{/if}

					{#if selectedNote.dormant}
						<div class="notice">
							Nothing links this note to live work, so it is not served to agents.
							Link it to an open step or unfinished objective to bring it back.
						</div>
					{/if}

					<div class="note-body markdown-wrap">
						{#if selectedNote.body}
							<Markdown source={selectedNote.body} {campaignId} />
						{:else}
							<p class="inline-empty">No body yet. Edit the note to add one.</p>
						{/if}
					</div>

					{#if parseTags(selectedNote.tags).length > 0}
						<div class="tag-row">
							{#each parseTags(selectedNote.tags) as tag}<span class="tag">{tag}</span
								>{/each}
						</div>
					{/if}

					{#if linkedObjectives.length > 0 || linkedSteps.length > 0}
						<div class="note-links">
							<p class="zone-label">Linked work</p>
							<div class="link-chip-row">
								{#each linkedObjectives as objective (objective.id)}
									<a
										class="link-chip"
										href="#/campaign/{campaignId}/objective/{objective.alias}"
									>
										<span class="ref-chip">#{objective.alias}</span>
										{objective.title}
									</a>
								{/each}
								{#each linkedSteps as step (step.id)}
									<a
										class="link-chip"
										href="#/campaign/{campaignId}/objective/{objectiveById[
											step.objectiveId
										]?.alias}"
									>
										<span class="ref-chip"
											>{stepRef(
												objectiveById[step.objectiveId]?.alias,
												step.stepNumber
											)}</span
										>
										{step.title}
										<StatusBadge status={step.status} sm />
									</a>
								{/each}
							</div>
						</div>
					{/if}

					{#if supersedesNote || supersededBy.length > 0}
						<div class="supersession">
							<p class="zone-label">Supersession chain</p>
							{#if supersedesNote}
								<a class="chain-row" href={noteHref(supersedesNote)}>
									<span class="chain-mark">↑ supersedes</span>
									<span class="ref-chip">#{supersedesNote.alias}</span>
									<span>{supersedesNote.title}</span>
									<StatusBadge status={supersedesNote.status} sm />
								</a>
							{/if}
							{#each supersededBy as replacement (replacement.id)}
								<a class="chain-row" href={noteHref(replacement)}>
									<span class="chain-mark">↓ superseded by</span>
									<span class="ref-chip">#{replacement.alias}</span>
									<span>{replacement.title}</span>
									<StatusBadge status={replacement.status} sm />
								</a>
							{/each}
						</div>
					{/if}

					<footer class="note-provenance">
						<span
							>Created by {selectedNote.createdBy || 'human'} · {formatDate(
								selectedNote.createdAt
							)}</span
						>
						<span>Updated {formatDate(selectedNote.updatedAt)}</span>
					</footer>
				</article>
			{:else if selectedMissing}
				<div class="panel not-found-panel">
					<p class="eyebrow">Reference unresolved</p>
					<h2>No note with that alias in this campaign.</h2>
					<p class="lede">It may belong to another campaign, or the link may be stale.</p>
					<button
						type="button"
						class="btn btn-secondary"
						onclick={() => go(`/campaign/${campaignId}/bunker`)}
						>Back to all notes</button
					>
				</div>
			{:else}
				<div class="panel detail-placeholder">
					<span class="placeholder-mark" aria-hidden="true">✦</span>
					<h2>Select a note</h2>
					<p>
						Read a note, follow its links, or trace a supersession chain without leaving
						the Bunker.
					</p>
				</div>
			{/if}
		</section>
	</div>
{/if}

{#if showCapture}
	<div
		class="modal-overlay"
		role="presentation"
		onclick={(e) => {
			if (e.target === e.currentTarget) showCapture = false;
		}}
	>
		<div
			class="modal modal-wide"
			role="dialog"
			aria-modal="true"
			aria-labelledby="capture-note-title"
			tabindex="-1"
			onclick={(e) => e.stopPropagation()}
			onkeydown={(e) => {
				if (e.key === 'Escape') showCapture = false;
			}}
		>
			<form onsubmit={createNote}>
				<div class="modal-header">
					<div>
						<p class="eyebrow">Campaign knowledge</p>
						<h2 id="capture-note-title">Capture a note</h2>
					</div>
					<button
						type="button"
						class="modal-close"
						onclick={() => (showCapture = false)}
						aria-label="Close">×</button
					>
				</div>
				<div class="modal-body">
					<div class="form-row">
						<div class="form-group">
							<label class="form-label" for="note-kind">Kind</label>
							<select
								id="note-kind"
								class="form-select"
								value={captureForm.kind}
								onchange={(e) => kindDefaultPinned(e.currentTarget.value)}
							>
								{#each NOTE_CREATE_KINDS as kind}<option value={kind}>{kind}</option
									>{/each}
							</select>
						</div>
						<div class="form-group">
							<label class="form-label" for="note-title">Title</label>
							<input
								id="note-title"
								class="form-input"
								type="text"
								bind:value={captureForm.title}
								required
							/>
						</div>
					</div>

					<div class="form-group">
						<label class="form-label" for="note-body"
							>Body <span class="optional">(markdown)</span></label
						>
						<MarkdownEditor
							id="note-body"
							bind:value={captureForm.body}
							{campaignId}
							rows={5}
						/>
					</div>

					<div class="form-row">
						<div class="form-group">
							<label class="form-label" for="note-tags">Tags</label>
							<input
								id="note-tags"
								class="form-input"
								type="text"
								bind:value={captureForm.tags}
								placeholder="comma separated"
							/>
						</div>
						<div class="form-group">
							<label class="form-label" for="note-supersedes"
								>Supersedes <span class="optional">(optional)</span></label
							>
							<select
								id="note-supersedes"
								class="form-select"
								bind:value={captureForm.supersedesId}
							>
								<option value="">Nothing</option>
								{#each noteOptions as note (note.id)}
									<option value={note.id}>#{note.alias} {note.title}</option>
								{/each}
							</select>
						</div>
					</div>

					<label class="check-row">
						<input type="checkbox" bind:checked={captureForm.pinned} />
						<span
							>Pin this note. Directives are always injected; pin other notes to keep
							them visible.</span
						>
					</label>

					<div class="link-picker">
						<div class="link-picker-col">
							<p class="zone-label">Link objectives</p>
							{#if objectives.length === 0}
								<p class="micro-empty">No objectives yet.</p>
							{:else}
								<div class="check-list">
									{#each objectives as objective (objective.id)}
										<label class="check-row">
											<input
												type="checkbox"
												value={objective.id}
												bind:group={captureForm.objectiveIds}
											/>
											<span class="ref-chip">#{objective.alias}</span>
											<span>{objective.title}</span>
										</label>
									{/each}
								</div>
							{/if}
						</div>
						<div class="link-picker-col">
							<p class="zone-label">Link steps</p>
							{#if captureLoadingSteps}
								<p class="micro-empty">Loading steps…</p>
							{:else if captureSteps.length === 0}
								<p class="micro-empty">No steps yet.</p>
							{:else}
								<div class="check-list scrollable">
									{#each captureSteps as step (step.id)}
										<label class="check-row">
											<input
												type="checkbox"
												value={step.id}
												bind:group={captureForm.stepIds}
											/>
											<span class="ref-chip"
												>{stepRef(
													step.objectiveAlias,
													step.stepNumber
												)}</span
											>
											<span>{step.title}</span>
										</label>
									{/each}
								</div>
							{/if}
						</div>
					</div>
				</div>
				<div class="modal-actions">
					<button
						type="button"
						class="btn btn-ghost"
						onclick={() => (showCapture = false)}>Cancel</button
					>
					<button type="submit" class="btn btn-primary" disabled={captureSaving}>
						{captureSaving ? 'Capturing…' : 'Capture note'}
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}

{#if editing}
	<div
		class="modal-overlay"
		role="presentation"
		onclick={(e) => {
			if (e.target === e.currentTarget) editing = null;
		}}
	>
		<div
			class="modal modal-wide"
			role="dialog"
			aria-modal="true"
			aria-labelledby="edit-note-title"
			tabindex="-1"
			onclick={(e) => e.stopPropagation()}
			onkeydown={(e) => {
				if (e.key === 'Escape') editing = null;
			}}
		>
			<form onsubmit={saveEdit}>
				<div class="modal-header">
					<div>
						<p class="eyebrow">#{editing.alias} · {editing.kind}</p>
						<h2 id="edit-note-title">Edit note</h2>
					</div>
					<button
						type="button"
						class="modal-close"
						onclick={() => (editing = null)}
						aria-label="Close">×</button
					>
				</div>
				<div class="modal-body">
					<div class="form-group">
						<label class="form-label" for="edit-note-title-input">Title</label>
						<input
							id="edit-note-title-input"
							class="form-input"
							type="text"
							bind:value={editForm.title}
							required
						/>
					</div>
					<div class="form-group">
						<label class="form-label" for="edit-note-body"
							>Body <span class="optional">(markdown)</span></label
						>
						<MarkdownEditor
							id="edit-note-body"
							bind:value={editForm.body}
							{campaignId}
							rows={6}
						/>
					</div>
					<div class="form-group">
						<label class="form-label" for="edit-note-tags">Tags</label>
						<input
							id="edit-note-tags"
							class="form-input"
							type="text"
							bind:value={editForm.tags}
						/>
					</div>
					<div class="form-group">
						<label class="form-label" for="edit-note-supersedes">Supersedes</label>
						<select
							id="edit-note-supersedes"
							class="form-select"
							bind:value={editForm.supersedesId}
						>
							{#if !editing.supersedesId}
								<option value="">Nothing</option>
							{/if}
							{#each editSupersedesOptions as note (note.id)}
								<option value={note.id}>#{note.alias} {note.title}</option>
							{/each}
						</select>
					</div>
					<label class="check-row">
						<input type="checkbox" bind:checked={editForm.pinned} />
						<span>Pinned</span>
					</label>
				</div>
				<div class="modal-actions">
					<button type="button" class="btn btn-ghost" onclick={() => (editing = null)}
						>Cancel</button
					>
					<button type="submit" class="btn btn-primary" disabled={editSaving}>
						{editSaving ? 'Saving…' : 'Save note'}
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}
