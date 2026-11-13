// Copies the canonical MCP instruction text into the site's fallback block.
//
// The text lives in WarTable.Api/Mcp/instructions.txt and is embedded into the
// server at build time. This script keeps site/docs/embedding.md byte-identical
// to it, so the documented fallback cannot drift from what the server sends.
//
// Usage: node scripts/sync-instructions.mjs [--check]

import { readFileSync, writeFileSync } from 'node:fs';

const SOURCE = new URL('../../WarTable.Api/Mcp/instructions.txt', import.meta.url);
const TARGET = new URL('../docs/embedding.md', import.meta.url);
const BEGIN = '<!-- instructions:begin';
const END = '<!-- instructions:end -->';

const text = readFileSync(SOURCE, 'utf8')
	.replace(/\r\n/g, '\n')
	.replace(/\n+$/, '');

const page = readFileSync(TARGET, 'utf8');
const start = page.indexOf(BEGIN);
const end = page.indexOf(END);

if (start === -1 || end === -1 || end < start) {
	throw new Error(`embedding.md is missing the "${BEGIN}" ... "${END}" markers`);
}

const from = page.lastIndexOf('\n', start) + 1;
const to = page.indexOf('\n', end) + 1;

const block = [
	'<!-- instructions:begin — generated from WarTable.Api/Mcp/instructions.txt; do not edit by hand -->',
	'',
	'```text',
	text,
	'```',
	'',
	'<!-- instructions:end -->',
	'',
].join('\n');

const next = page.slice(0, from) + block + page.slice(to);

if (process.argv.includes('--check')) {
	if (next !== page) {
		console.error('site/docs/embedding.md is out of date; run: npm run sync:instructions');
		process.exit(1);
	}
	console.log('site/docs/embedding.md matches WarTable.Api/Mcp/instructions.txt');
} else {
	writeFileSync(TARGET, next);
	console.log(`Synced ${text.length} characters into site/docs/embedding.md`);
}
