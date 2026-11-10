import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import path from 'path';

export default defineConfig({
	plugins: [svelte()],
	root: '.',
	build: {
		outDir: path.resolve(__dirname, '..', 'WarTable.Api', 'wwwroot'),
		emptyOutDir: true
	},
	server: {
		proxy: {
			'/api': 'http://localhost:5110'
		}
	}
});
