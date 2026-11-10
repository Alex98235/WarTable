<script>
	import { renderMarkdown } from '../lib/markdown.js';
	import { openAliasPopover } from '../lib/aliasPopover.svelte.js';

	let {
		source = '',
		campaignId = '',
		clamp = null,
		collapsible = false,
		showLabel = 'Show more',
		hideLabel = 'Show less'
	} = $props();

	let containerEl = $state(null);
	let isOverflowing = $state(false);
	let expanded = $state(false);
	let rendered = $derived(renderMarkdown(source));

	const ALIAS_RE = /(?<!\w)#(\d+)\b/g;

	// ── Post-render alias linkification ──
	// Delegated handlers live on the container; this effect only wraps #N text
	// nodes into span.alias-ref (skipping code/pre/a). Re-runs when source changes.
	$effect(() => {
		const el = containerEl;
		if (!el || !rendered) return;

		// Remove previous alias spans so we never double-wrap on re-run.
		el.querySelectorAll('.alias-ref').forEach((n) => {
			const text = n.textContent || '';
			const parent = n.parentNode;
			if (parent) {
				parent.replaceChild(document.createTextNode(text), n);
				parent.normalize();
			}
		});

		const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT, null, false);
		const textNodes = [];
		while (walker.nextNode()) textNodes.push(walker.currentNode);

		for (const node of textNodes) {
			// Skip text inside code, pre, a elements.
			let skip = false;
			let p = node.parentNode;
			while (p && p !== el) {
				const tag = p.tagName?.toLowerCase();
				if (tag === 'code' || tag === 'pre' || tag === 'a') {
					skip = true;
					break;
				}
				p = p.parentNode;
			}
			if (skip) continue;

			const text = node.textContent || '';
			if (!text) continue;
			ALIAS_RE.lastIndex = 0;
			if (!ALIAS_RE.test(text)) continue;
			ALIAS_RE.lastIndex = 0;

			const frag = document.createDocumentFragment();
			let lastIdx = 0;
			let m;
			while ((m = ALIAS_RE.exec(text)) !== null) {
				if (m.index > lastIdx) {
					frag.appendChild(document.createTextNode(text.slice(lastIdx, m.index)));
				}
				const span = document.createElement('span');
				span.className = 'alias-ref';
				span.setAttribute('data-alias', m[1]);
				span.setAttribute('role', 'button');
				span.setAttribute('tabindex', '0');
				span.textContent = m[0];
				frag.appendChild(span);
				lastIdx = m.index + m[0].length;
			}
			if (lastIdx < text.length) {
				frag.appendChild(document.createTextNode(text.slice(lastIdx)));
			}
			node.parentNode.replaceChild(frag, node);
		}
	});

	// ── Measure overflow (must run after linkification above in the same flush) ──
	// `scrollHeight` is unreliable under -webkit-line-clamp (it can report the
	// clamped height), so measure an unclamped clone at the same width instead.
	function overflows(el) {
		const probe = el.cloneNode(true);
		probe.removeAttribute('id');
		probe.style.cssText = [
			'position:absolute',
			'visibility:hidden',
			'pointer-events:none',
			'left:-99999px',
			'top:0',
			'display:block',
			'overflow:visible',
			'-webkit-line-clamp:none',
			'line-clamp:none',
			`width:${el.clientWidth}px`
		].join(';');
		el.parentNode.appendChild(probe);
		const full = probe.getBoundingClientRect().height;
		probe.remove();
		return full > el.clientHeight + 1;
	}

	// Also watches `rendered` so it collapses + re-measures when source changes.
	$effect(() => {
		const el = containerEl;
		const src = rendered;
		if (!el || !clamp) return;
		void src;
		// Source changed: collapse and re-evaluate.
		expanded = false;
		requestAnimationFrame(() => {
			if (!el.isConnected) return;
			isOverflowing = overflows(el);
		});
	});

	function getStyle() {
		if (!clamp || expanded) return '';
		return [
			'display:-webkit-box',
			`-webkit-line-clamp:${clamp}`,
			`line-clamp:${clamp}`,
			'-webkit-box-orient:vertical',
			'overflow:hidden'
		].join(';');
	}

	function toggleExpand(e) {
		e.stopPropagation();
		expanded = !expanded;
	}

	// ── Delegated click/keydown for alias references ──
	function openAliasFromTarget(target, rect) {
		const alias = parseInt(target.getAttribute('data-alias'), 10);
		if (!alias) return;
		openAliasPopover(alias, rect, campaignId);
	}

	function handleContainerClick(e) {
		const target = e.target.closest('[data-alias]');
		if (!target) return;
		openAliasFromTarget(target, target.getBoundingClientRect());
	}

	function handleContainerKeydown(e) {
		if (e.key !== 'Enter' && e.key !== ' ') return;
		const target = e.target.closest('[data-alias]');
		if (!target) return;
		e.preventDefault();
		openAliasFromTarget(target, target.getBoundingClientRect());
	}
</script>

<div class="markdown-wrapper">
	<div
		class="markdown-body"
		bind:this={containerEl}
		style={getStyle()}
		onclick={handleContainerClick}
		onkeydown={handleContainerKeydown}
		role="presentation"
	>
		<!-- eslint-disable-next-line svelte/no-at-html-tags -- sanitized by renderMarkdown -->
		{@html rendered}
	</div>
	{#if clamp && (isOverflowing || expanded) && collapsible}
		<button
			type="button"
			class="btn btn-primary-inverted btn-sm markdown-toggle"
			onclick={toggleExpand}
		>
			{expanded ? hideLabel : showLabel}
		</button>
	{/if}
</div>

<style>
	.markdown-wrapper {
		display: flex;
		flex-direction: column;
	}
	.markdown-toggle {
		align-self: flex-start;
		margin-top: 4px;
	}
</style>
