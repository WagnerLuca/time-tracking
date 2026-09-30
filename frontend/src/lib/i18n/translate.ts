/**
 * Pure translation core (no Svelte, no stores) so it can be unit-tested.
 * `index.ts` binds it to the real dictionaries and the reactive locale.
 */

export type Params = Record<string, string | number>;

/** A counted string: `tp()` picks `one` for 1 and `other` for everything else. */
export interface Plural {
	one: string;
	other: string;
}

type Join<P extends string, K extends string> = P extends '' ? K : `${P}.${K}`;

/** Every dotted path in T that ends at a plain string, e.g. 'common.save'. */
export type LeafPaths<T, P extends string = ''> = {
	[K in keyof T & string]: T[K] extends string
		? Join<P, K>
		: T[K] extends Plural
			? never
			: LeafPaths<T[K], Join<P, K>>;
}[keyof T & string];

/** Every dotted path in T that ends at a { one, other } plural entry. */
export type PluralPaths<T, P extends string = ''> = {
	[K in keyof T & string]: T[K] extends string
		? never
		: T[K] extends Plural
			? Join<P, K>
			: PluralPaths<T[K], Join<P, K>>;
}[keyof T & string];

export function lookup(dict: unknown, key: string): unknown {
	let node: unknown = dict;
	for (const part of key.split('.')) {
		if (node == null || typeof node !== 'object') return undefined;
		node = (node as Record<string, unknown>)[part];
	}
	return node;
}

export function interpolate(template: string, params?: Params): string {
	if (!params) return template;
	return template.replace(/\{(\w+)\}/g, (match, name: string) => (name in params ? String(params[name]) : match));
}

function isPlural(value: unknown): value is Plural {
	return !!value && typeof value === 'object' && typeof (value as Plural).one === 'string' && typeof (value as Plural).other === 'string';
}

/**
 * Look `key` up in `dicts[locale]`, falling back to `dicts[fallback]` and
 * finally to the key itself — a missing translation shows up as "timer.start"
 * rather than an empty gap.
 */
export function translate<L extends string>(
	dicts: Record<L, unknown>,
	locale: L,
	fallback: L,
	key: string,
	params?: Params
): string {
	for (const l of [locale, fallback]) {
		const value = lookup(dicts[l], key);
		if (typeof value === 'string') return interpolate(value, params);
	}
	return key;
}

export function translatePlural<L extends string>(
	dicts: Record<L, unknown>,
	locale: L,
	fallback: L,
	key: string,
	count: number,
	params?: Params
): string {
	const all = { count, ...params };
	for (const l of [locale, fallback]) {
		const value = lookup(dicts[l], key);
		if (isPlural(value)) return interpolate(count === 1 ? value.one : value.other, all);
	}
	return key;
}
