import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { toast } from './toast.svelte';
import { confirmDialog, confirmState, promptDialog } from './confirm.svelte';

/** A Map-backed localStorage stand-in. */
function memoryStorage(initial: Record<string, string> = {}) {
	const map = new Map(Object.entries(initial));
	return {
		getItem: vi.fn((k: string) => map.get(k) ?? null),
		setItem: vi.fn((k: string, v: string) => void map.set(k, v)),
		map
	};
}

const blockedStorage = {
	getItem: () => {
		throw new Error('SecurityError');
	},
	setItem: () => {
		throw new Error('SecurityError');
	}
};

/** Just enough of `document` for the stores: <html> with lang + classList. */
function fakeDocument() {
	const classes = new Set<string>();
	const documentElement = {
		lang: '',
		classList: {
			toggle: (c: string, on: boolean) => void (on ? classes.add(c) : classes.delete(c)),
			contains: (c: string) => classes.has(c)
		}
	};
	return { documentElement, classes };
}

afterEach(() => {
	vi.unstubAllGlobals();
	vi.useRealTimers();
	vi.resetModules();
});

describe('toast store', () => {
	beforeEach(() => {
		vi.useFakeTimers();
		for (const t of toast.items) toast.dismiss(t.id);
	});

	it('queues toasts with increasing ids and the right kind', () => {
		const a = toast.success('Gespeichert');
		const b = toast.info('Hinweis');
		const c = toast.warning('Achtung');
		expect(b).toBeGreaterThan(a);
		expect(toast.items.map((t) => [t.id, t.kind, t.message])).toEqual([
			[a, 'success', 'Gespeichert'],
			[b, 'info', 'Hinweis'],
			[c, 'warning', 'Achtung']
		]);
	});

	it('auto-dismisses after 4 s, errors after 7 s', () => {
		toast.success('ok');
		toast.error('kaputt');
		vi.advanceTimersByTime(4000);
		expect(toast.items.map((t) => t.kind)).toEqual(['error']);
		vi.advanceTimersByTime(3000);
		expect(toast.items).toEqual([]);
	});

	it('a timeout of 0 keeps the toast until dismissed', () => {
		const id = toast.show('info', 'sticky', 0);
		vi.advanceTimersByTime(60_000);
		expect(toast.items).toHaveLength(1);
		toast.dismiss(id);
		expect(toast.items).toEqual([]);
	});

	it('dismissing early does not break the later timer; unknown ids are ignored', () => {
		const id = toast.success('x');
		toast.dismiss(id);
		toast.dismiss(9999);
		vi.advanceTimersByTime(5000);
		expect(toast.items).toEqual([]);
	});

	it('error() takes a string or extracts the message from a caught error', () => {
		toast.error('plain');
		toast.error({ response: { status: 400, data: { message: 'Vom Server' } } });
		toast.error(null, 'Fallback');
		expect(toast.items.map((t) => t.message)).toEqual(['plain', 'Vom Server', 'Fallback']);
	});
});

describe('confirm store', () => {
	it('resolves with the answer and clears the pending dialog', async () => {
		const result = confirmDialog({ title: 'Löschen?', danger: true });
		expect(confirmState.current).toMatchObject({ title: 'Löschen?', danger: true });
		confirmState.answer(true);
		await expect(result).resolves.toBe(true);
		expect(confirmState.current).toBeNull();

		const second = confirmDialog({ title: 'Nochmal?' });
		confirmState.answer(false);
		await expect(second).resolves.toBe(false);
	});

	it('a prompt resolves with the trimmed text, or null when cancelled', async () => {
		const asked = promptDialog({ title: 'Grund?', input: { label: 'Grund' } });
		expect(confirmState.current?.input?.label).toBe('Grund');
		confirmState.answer(true, '  Korrektur  ');
		await expect(asked).resolves.toBe('Korrektur');

		const cancelled = promptDialog({ title: 'Grund?', input: { label: 'Grund' } });
		confirmState.answer(false, 'ignored');
		await expect(cancelled).resolves.toBeNull();
	});

	it('a new dialog cancels the one still open', async () => {
		const first = confirmDialog({ title: 'A' });
		const second = confirmDialog({ title: 'B' });
		await expect(first).resolves.toBe(false);
		expect(confirmState.current?.title).toBe('B');
		confirmState.answer(true);
		await expect(second).resolves.toBe(true);
	});

	it('answering with nothing pending is a no-op', () => {
		expect(() => confirmState.answer(true)).not.toThrow();
		expect(confirmState.current).toBeNull();
	});
});

describe('theme store', () => {
	async function load(storage: object, prefersDark = false) {
		const doc = fakeDocument();
		vi.stubGlobal('document', doc);
		vi.stubGlobal('localStorage', storage);
		vi.stubGlobal('window', { matchMedia: vi.fn(() => ({ matches: prefersDark })) });
		const { theme } = await import('./theme.svelte');
		return { theme, doc };
	}

	it('load() without a window (SSR) does nothing', async () => {
		const { theme } = await import('./theme.svelte');
		theme.load();
		expect(theme.isDark).toBe(false);
	});

	it('a saved choice wins over the OS preference', async () => {
		const { theme, doc } = await load(memoryStorage({ 'wl-theme': 'light' }), true);
		theme.load();
		expect(theme.isDark).toBe(false);
		expect([...doc.classes]).toEqual(['light']);
	});

	it('without a saved choice it follows prefers-color-scheme', async () => {
		const { theme, doc } = await load(memoryStorage(), true);
		theme.load();
		expect(theme.isDark).toBe(true);
		expect([...doc.classes]).toEqual(['dark']);
	});

	it('set() flips the classes and persists under wl-theme', async () => {
		const storage = memoryStorage({ 'wl-theme': 'dark' });
		const { theme, doc } = await load(storage);
		theme.load();
		expect(theme.isDark).toBe(true);
		theme.set(false);
		expect(theme.isDark).toBe(false);
		expect(doc.documentElement.classList.contains('dark')).toBe(false);
		expect(doc.documentElement.classList.contains('light')).toBe(true);
		expect(storage.map.get('wl-theme')).toBe('light');
	});

	it('blocked storage falls back to the OS preference and set() still applies', async () => {
		const { theme, doc } = await load(blockedStorage, true);
		theme.load();
		expect(theme.isDark).toBe(true);
		theme.set(false);
		expect([...doc.classes]).toEqual(['light']);
	});
});

describe('locale store', () => {
	async function load(storage: object | undefined, withWindow = true) {
		const doc = fakeDocument();
		if (withWindow) vi.stubGlobal('window', {});
		if (storage) vi.stubGlobal('localStorage', storage);
		vi.stubGlobal('document', doc);
		const { locale, LOCALE_STORAGE_KEY } = await import('./locale.svelte');
		return { locale, doc, LOCALE_STORAGE_KEY };
	}

	it('defaults to German on the server (no window)', async () => {
		const { locale } = await load(memoryStorage({ 'wl-locale': 'en' }), false);
		expect(locale.current).toBe('de');
		expect(locale.intl).toBe('de-DE');
	});

	it('restores a stored English choice in the browser; anything else is German', async () => {
		const { locale, LOCALE_STORAGE_KEY } = await load(memoryStorage({ 'wl-locale': 'en' }));
		expect(LOCALE_STORAGE_KEY).toBe('wl-locale');
		expect(locale.current).toBe('en');
		expect(locale.intl).toBe('en-GB');
		vi.resetModules();
		const other = await load(memoryStorage({ 'wl-locale': 'fr' }));
		expect(other.locale.current).toBe('de');
	});

	it('blocked storage falls back to German and set() still switches', async () => {
		const { locale, doc } = await load(blockedStorage);
		expect(locale.current).toBe('de');
		await locale.set('en');
		expect(locale.current).toBe('en');
		expect(doc.documentElement.lang).toBe('en');
	});

	it('set() / toggle() persist and update <html lang>', async () => {
		const storage = memoryStorage();
		const { locale, doc } = await load(storage);
		await locale.toggle();
		expect(locale.current).toBe('en');
		expect(storage.map.get('wl-locale')).toBe('en');
		expect(doc.documentElement.lang).toBe('en');
		locale.toggle();
		expect(locale.current).toBe('de');
		expect(storage.map.get('wl-locale')).toBe('de');
		expect(doc.documentElement.lang).toBe('de');
	});

	it('set() waits for the dictionary; a later choice wins over a slow load', async () => {
		const { locale } = await load(memoryStorage());
		const english = locale.set('en');
		expect(locale.current).toBe('de'); // not before English is there
		await locale.set('de');
		await english;
		expect(locale.current).toBe('de');
	});

	it('a dictionary that fails to load keeps the current language', async () => {
		vi.doMock('$lib/i18n/dictionaries', () => ({ isLoaded: () => false, loadDictionary: () => Promise.reject(new Error('stale chunk')) }));
		const error = vi.spyOn(console, 'error').mockImplementation(() => {});
		const { locale } = await load(memoryStorage());
		await locale.set('en');
		expect(locale.current).toBe('de');
		expect(error).toHaveBeenCalled();
		vi.doUnmock('$lib/i18n/dictionaries');
		error.mockRestore();
	});
});
