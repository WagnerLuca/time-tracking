import { locale } from '$lib/stores/locale.svelte';
import { de, type Dictionary } from './de';
import { en } from './en';
import { translate, translatePlural, type LeafPaths, type Params, type PluralPaths } from './translate';

/**
 * UI strings, German first. Components call `t('section.key')` — never
 * hardcode either language. Both functions read `locale.current`, so every
 * template expression using them re-renders when the language is toggled.
 *
 * Where to add a string: the namespace module in `messages/` that matches the
 * page/component (both `de` and `en` in the same file; `en` is typed against
 * `de`, so a missing English key is a type error). Counted strings use a
 * `{ one, other }` entry and `tp()`.
 */
export type MessageKey = LeafPaths<Dictionary>;
export type PluralKey = PluralPaths<Dictionary>;
export type { Params };

const dictionaries = { de, en };

export function t(key: MessageKey, params?: Params): string {
	return translate(dictionaries, locale.current, 'de', key, params);
}

/** Counted strings: tp('calendar.sickDays', 3) → "3 Krankheitstage". `{count}` is always available. */
export function tp(key: PluralKey, count: number, params?: Params): string {
	return translatePlural(dictionaries, locale.current, 'de', key, count, params);
}
