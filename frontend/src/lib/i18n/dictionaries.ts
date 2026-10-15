import type { Locale } from '$lib/stores/locale.svelte';
import { de, type Dictionary } from './de';

/**
 * The dictionaries loaded so far. German is in the initial bundle (default and
 * fallback); every other language is its own chunk, fetched by `loadDictionary`
 * — `locale.set()` awaits it before switching, and the root `+layout.ts` before
 * the first render, so the UI never shows keys or a half-translated page.
 */
export const dictionaries: Partial<Record<Locale, Dictionary>> = { de };

const loaders: Record<Locale, () => Promise<Dictionary>> = {
	de: async () => de,
	// The only import of './en' — keep it dynamic, a static import puts English back into the app shell.
	en: () => import('./en').then((m) => m.en)
};

export function isLoaded(l: Locale): boolean {
	return !!dictionaries[l];
}

export async function loadDictionary(l: Locale): Promise<void> {
	dictionaries[l] ??= await loaders[l]();
}
