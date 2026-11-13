// Generates the instruction section of the embedding page from the canonical
// text in WarTable.Api/Mcp/instructions.txt. The generated file is gitignored:
// the page in version control carries only the import and the component, so an
// instructions change never shows up as a docs diff. Wired as the site
// prebuild and prestart; there is no in-repo copy to drift from.
import { readFileSync, writeFileSync } from 'node:fs';

const SOURCE = new URL('../../WarTable.Api/Mcp/instructions.txt', import.meta.url);
const PARTIAL = new URL('../docs/_instructions.generated.mdx', import.meta.url);
const PAGE = new URL('../docs/embedding.md', import.meta.url);

const IMPORT = "import Instructions from './_instructions.generated.mdx';";
const COMPONENT = '<Instructions />';

const text = readFileSync(SOURCE, 'utf8').replace(/\r\n/g, '\n').replace(/\n+$/, '');

if (text.length < 500)
	throw new Error(
		`WarTable.Api/Mcp/instructions.txt is only ${text.length} characters; refusing to generate a truncated page`,
	);

const page = readFileSync(PAGE, 'utf8').replace(/\r\n/g, '\n');

if (!page.includes(IMPORT) || !page.includes(COMPONENT))
	throw new Error(
		`site/docs/embedding.md must contain ${IMPORT} and ${COMPONENT}; the instructions have nowhere to land`,
	);

const partial = `{/* Generated from WarTable.Api/Mcp/instructions.txt by site/scripts/sync-instructions.mjs. Do not edit. */}

\`\`\`text
${text}
\`\`\`
`;

writeFileSync(PARTIAL, partial.replace(/\r\n/g, '\n'));
console.log(`Synced ${text.length} characters into site/docs/_instructions.generated.mdx`);
