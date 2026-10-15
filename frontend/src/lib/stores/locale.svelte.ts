/**
 * UI language — German by default, English via the toggle. Persisted under the
 * shared `wl-locale` key (values 'de' | 'en') so the choice follows the visitor
 * across Wagner Luca apps on one origin, like `wl-theme` does for light/dark.
 * app.html sets <html lang> before first paint; `set()` keeps it in sync.
 * `set()` loads a language's dictionary first (only German is bundled), so the
 * switch happens in one render, never with keys showing.
 */

import { isLoaded, loadDictionary } from '$lib/i18n/dictionaries';

export type Locale = 'de' | 'en';

export const LOCALE_STORAGE_KEY = 'wl-locale';

/** BCP-47 tags for Intl. en-GB rather than en-US: a European app, day-first dates, 24h time. */
const INTL_TAGS: Record<Locale, string> = { de: 'de-DE', en: 'en-GB' };

function readStoredLocale(): Locale {
	try {
		return localStorage.getItem(LOCALE_STORAGE_KEY) === 'en' ? 'en' : 'de';
	} catch {
		return 'de';
	}
}

function createLocaleStore() {
	const initial: Locale = typeof window !== 'undefined' ? readStoredLocale() : 'de';
	let current = $state<Locale>(initial);
	/** The last language asked for: a slow load must not override a later choice. */
	let wanted = initial;

	/** Switches at once when the dictionary is loaded, else after loading it (resolves when switched). */
	function set(locale: Locale): Promise<void> {
		wanted = locale;
		if (isLoaded(locale)) {
			apply(locale);
			return Promise.resolve();
		}
		return loadDictionary(locale).then(
			() => {
				if (wanted === locale) apply(locale);
			},
			// e.g. a stale chunk after a redeploy: stay in the current language
			(err) => console.error('Could not load the language', locale, err)
		);
	}

	function apply(locale: Locale) {
		current = locale;
		try {
			localStorage.setItem(LOCALE_STORAGE_KEY, locale);
		} catch {
			// storage blocked (private mode) — the choice just isn't remembered
		}
		if (typeof document !== 'undefined') document.documentElement.lang = locale;
	}

	return {
		get current() { return current; },
		/** Full BCP-47 tag for Intl formatters (de-DE / en-GB). */
		get intl() { return INTL_TAGS[current]; },
		set,
		toggle: () => set(current === 'de' ? 'en' : 'de')
	};
}

export const locale = createLocaleStore();
