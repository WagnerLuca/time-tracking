import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vitest/config';

// Unit tests only (pure TS + .svelte.ts rune modules); no DOM environment needed.
// TZ is pinned so date-key / DST tests behave the same on every machine and in CI.
export default defineConfig({
	plugins: [sveltekit()],
	test: {
		include: ['src/**/*.test.ts'],
		env: { TZ: 'Europe/Berlin' }
	}
});
