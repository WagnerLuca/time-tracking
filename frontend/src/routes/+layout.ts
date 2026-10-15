import { loadDictionary } from '$lib/i18n/dictionaries';
import { locale } from '$lib/stores/locale.svelte';

// Auth tokens live in localStorage, so every page renders client-side only.
export const ssr = false;

// Load the stored language's dictionary before the first render (German is bundled, English is a chunk).
// A failed load (stale chunk after a redeploy) still renders, in the German fallback.
export const load = () => loadDictionary(locale.current).catch(() => {});
