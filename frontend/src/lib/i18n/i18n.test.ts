import { readdirSync, readFileSync } from 'node:fs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { locale } from '$lib/stores/locale.svelte';
import { t, tp } from './index';
import { de } from './de';
import { en } from './en';
import { lookup, translate, translatePlural } from './translate';

/** All leaf paths of a nested dictionary; plural entries count as one leaf. */
function leafPaths(node: unknown, prefix = ''): string[] {
	if (typeof node === 'string') return [prefix];
	const obj = node as Record<string, unknown>;
	if (typeof obj.one === 'string' && typeof obj.other === 'string') return [prefix];
	return Object.entries(obj).flatMap(([k, v]) => leafPaths(v, prefix ? `${prefix}.${k}` : k));
}

describe('translate core', () => {
	const dicts = {
		de: { a: { b: 'Hallo {name}' }, only: 'nur deutsch', n: { one: 'ein Tag', other: '{count} Tage' } },
		en: { a: { b: 'Hello {name}' }, n: { one: 'one day', other: '{count} days' } }
	};

	it('looks up nested keys and interpolates params', () => {
		expect(translate(dicts, 'en', 'de', 'a.b', { name: 'Tom' })).toBe('Hello Tom');
		expect(translate(dicts, 'de', 'de', 'a.b', { name: 'Tom' })).toBe('Hallo Tom');
	});

	it('leaves unknown placeholders intact', () => {
		expect(translate(dicts, 'en', 'de', 'a.b')).toBe('Hello {name}');
		expect(translate(dicts, 'en', 'de', 'a.b', { other: 1 })).toBe('Hello {name}');
	});

	it('falls back to German, then to the key', () => {
		expect(translate(dicts, 'en', 'de', 'only')).toBe('nur deutsch');
		expect(translate(dicts, 'en', 'de', 'missing.key')).toBe('missing.key');
		// a key pointing at an object is not a string → key
		expect(translate(dicts, 'en', 'de', 'a')).toBe('a');
	});

	it('plurals pick one/other and always provide {count}', () => {
		expect(translatePlural(dicts, 'de', 'de', 'n', 1)).toBe('ein Tag');
		expect(translatePlural(dicts, 'de', 'de', 'n', 3)).toBe('3 Tage');
		expect(translatePlural(dicts, 'en', 'de', 'n', 0)).toBe('0 days');
		expect(translatePlural(dicts, 'en', 'de', 'nope', 2)).toBe('nope');
	});

	it('lookup tolerates missing branches', () => {
		expect(lookup(dicts.de, 'a.b.c')).toBeUndefined();
		expect(lookup(undefined, 'a')).toBeUndefined();
	});
});

describe('app dictionaries', () => {
	afterEach(() => locale.set('de'));

	it('German is the default locale', () => {
		expect(t('common.save')).toBe('Speichern');
		locale.set('en');
		expect(t('common.save')).toBe('Save');
	});

	it('tp uses the dictionaries', () => {
		expect(tp('nav.failure.title', 1)).toBe('1 Timer wurde nicht gestoppt');
		expect(tp('nav.failure.title', 2)).toBe('2 Timer wurden nicht gestoppt');
	});

	it('every German key exists in English and vice versa', () => {
		const deKeys = leafPaths(de).sort();
		const enKeys = leafPaths(en).sort();
		expect(enKeys.filter((k) => !deKeys.includes(k))).toEqual([]);
		expect(deKeys.filter((k) => !enKeys.includes(k))).toEqual([]);
	});

	it('no empty translations', () => {
		const empty = leafPaths(de).filter((k) => {
			const v = lookup(en, k) as unknown;
			const d = lookup(de, k) as unknown;
			return [v, d].some((x) => x === '' || (typeof x === 'object' && x && Object.values(x).some((s) => s === '')));
		});
		expect(empty).toEqual([]);
	});

	it('placeholders match between German and English', () => {
		const placeholders = (s: unknown) => JSON.stringify(s).match(/\{\w+\}/g)?.sort().join(',') ?? '';
		const mismatched = leafPaths(de).filter((k) => placeholders(lookup(de, k)) !== placeholders(lookup(en, k)));
		expect(mismatched).toEqual([]);
	});

	it('every literal t()/tp() key used in the source exists', () => {
		// Read from disk rather than import.meta.glob('?raw'): the raw-string modules would
		// shadow the real ones in the v8 coverage remapping (same path once the query is stripped).
		const sources = Object.fromEntries(
			readdirSync('src', { recursive: true, encoding: 'utf8' })
				.map((f) => f.replaceAll('\\', '/'))
				.filter((f) => /\.(svelte|ts)$/.test(f) && !f.endsWith('.test.ts') && !f.startsWith('lib/api/'))
				.map((f) => [`/src/${f}`, readFileSync(`src/${f}`, 'utf8')])
		);
		expect(Object.keys(sources).length).toBeGreaterThan(20);
		const known = new Set(leafPaths(de));
		const missing: string[] = [];
		for (const [file, raw] of Object.entries(sources)) {
			// ignore comments (docs mention example keys)
			const src = raw
				.replace(/\/\*[\s\S]*?\*\//g, '')
				.split('\n')
				.filter((line) => !/^\s*(\/\/|\*)/.test(line))
				.join('\n');
			for (const m of src.matchAll(/\bt[p]?\(\s*'([a-zA-Z0-9_.]+)'/g)) {
				if (!known.has(m[1])) missing.push(`${file}: ${m[1]}`);
			}
		}
		expect(missing).toEqual([]);
	});
});

describe('loading on demand', () => {
	afterEach(() => {
		vi.doUnmock('./en');
		vi.resetModules();
	});

	it('the English dictionary is not imported at startup in German, only on switching', async () => {
		vi.resetModules(); // a fresh app: the test setup preloads English into the shared modules
		let imported = 0;
		vi.doMock('./en', async (importOriginal) => {
			imported++;
			return importOriginal();
		});
		const { locale } = await import('$lib/stores/locale.svelte');
		const { t } = await import('./index');
		const { isLoaded } = await import('./dictionaries');
		expect(locale.current).toBe('de');
		expect(t('common.save')).toBe('Speichern');
		expect(imported).toBe(0);
		expect(isLoaded('en')).toBe(false);

		await locale.set('en');
		expect(imported).toBe(1);
		expect(t('common.save')).toBe('Save');
		await locale.set('de');
		expect(t('common.save')).toBe('Speichern');
	});
});
