// Refreshes src/lib/theme/wl-tokens.fallback.css from the installed @wagnerluca/ui,
// so builds without a GitHub Packages token look the same as builds with one.
import { readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';

const require = createRequire(import.meta.url);
const tokensPath = require.resolve('@wagnerluca/ui/tokens.css');
// package.json is not in the package's exports; it sits two levels above src/styles/
const pkg = JSON.parse(readFileSync(new URL('../../package.json', pathToFileURL(tokensPath)), 'utf8'));
const tokens = readFileSync(tokensPath, 'utf8');
const header = `/*
  Fallback copy of @wagnerluca/ui ${pkg.version} tokens.css (src/styles/tokens.css).
  vite.config.ts uses it only when @wagnerluca/ui is not installed (no
  GitHub Packages token at install time); otherwise the package wins.
  Refresh it with \`npm run tokens:sync\` after upgrading the package.
*/
`;
writeFileSync(new URL('../src/lib/theme/wl-tokens.fallback.css', import.meta.url), header + tokens);
console.log(`Fallback tokens synced from @wagnerluca/ui ${pkg.version}`);
