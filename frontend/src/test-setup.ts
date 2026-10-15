// Only German is bundled; the app loads English on the first switch (`locale.set` awaits it).
// Unit tests switch with a plain `locale.set('en')` and expect English on the next line,
// so load it once up front — as after a first switch in the app. Tests that need a fresh
// app without it call `vi.resetModules()` (see i18n.test.ts).
import { loadDictionary } from '$lib/i18n/dictionaries';

await loadDictionary('en');
