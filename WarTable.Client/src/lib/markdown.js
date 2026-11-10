// markdown-it + DOMPurify renderer
// Pure function — no component state, no side effects per call.

import MarkdownIt from 'markdown-it';
import DOMPurify from 'dompurify';

const md = new MarkdownIt({
	html: false,
	linkify: true,
	typographer: true,
	breaks: false
});

// Whitelist for sanitized output. Everything else is stripped.
const ALLOWED_TAGS = [
	'h1',
	'h2',
	'h3',
	'h4',
	'h5',
	'h6',
	'p',
	'br',
	'hr',
	'strong',
	'em',
	'del',
	'code',
	'pre',
	'blockquote',
	'ul',
	'ol',
	'li',
	'a',
	'table',
	'thead',
	'tbody',
	'tr',
	'th',
	'td'
];

// Force every link to open in a new, untrusted context.
DOMPurify.addHook('afterSanitizeAttributes', (node) => {
	if (node.tagName && node.tagName.toLowerCase() === 'a') {
		node.setAttribute('target', '_blank');
		node.setAttribute('rel', 'noopener noreferrer');
	}
});

/**
 * Render markdown source to a safe, sanitized HTML string.
 * Returns "" for empty / whitespace input.
 */
export function renderMarkdown(src) {
	if (!src || (typeof src === 'string' && !src.trim())) return '';
	const raw = md.render(String(src));

	return DOMPurify.sanitize(raw, {
		ALLOWED_TAGS,
		ALLOWED_ATTR: ['href', 'title', 'target', 'rel'],
		WHOLE_DOCUMENT: false
	});
}
