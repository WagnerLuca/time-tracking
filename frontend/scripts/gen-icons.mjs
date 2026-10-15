// Renders the PWA icons in static/icons/ from the WL logo geometry (the port in +layout.svelte,
// @wagnerluca/ui's LogoLockup) with the `time` accent. Run after a logo/colour change:
//   node scripts/gen-icons.mjs        (E2E_CHANNEL=msedge to use an installed Edge)
import { chromium } from '@playwright/test';
import { writeFileSync } from 'node:fs';

const INK = '#2A2B2E', ACCENT = '#7FA98E', SURFACE = '#FFFFFF';
// The logo spans about x 1.5–100.5, y 0–92.5 in its 100×100 box (stroke included)
const logo = (scale, color = INK, accent = ACCENT) => `
	<g transform="translate(50 50) scale(${scale}) translate(-51 -48)">
		<rect x="68" y="4" width="20" height="10" rx="5" fill="${accent}"/>
		<polyline points="6,26 24,88 42,30 60,88 78,26 78,88 96,88" fill="none" stroke="${color}"
			stroke-width="9" stroke-linecap="round" stroke-linejoin="round"/>
	</g>`;
const svg = (body) => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">${body}</svg>`;

const icons = {
	// Regular: a rounded tile, the logo at ~68 %
	'icon-192.png': [192, svg(`<rect width="100" height="100" rx="22" fill="${SURFACE}"/>${logo(0.68)}`)],
	'icon-512.png': [512, svg(`<rect width="100" height="100" rx="22" fill="${SURFACE}"/>${logo(0.68)}`)],
	// Maskable: full bleed, the logo inside the 80 % safe circle (≈ 56 % box)
	'icon-maskable-192.png': [192, svg(`<rect width="100" height="100" fill="${SURFACE}"/>${logo(0.56)}`)],
	'icon-maskable-512.png': [512, svg(`<rect width="100" height="100" fill="${SURFACE}"/>${logo(0.56)}`)],
	// Monochrome (Android 13+ themed icons, notification badge): only the alpha channel counts
	'icon-monochrome-512.png': [512, svg(logo(0.56, '#FFFFFF', '#FFFFFF'))],
	'badge-96.png': [96, svg(logo(0.8, '#FFFFFF', '#FFFFFF'))],
	// iOS rounds the corners itself
	'apple-touch-icon.png': [180, svg(`<rect width="100" height="100" fill="${SURFACE}"/>${logo(0.68)}`)]
};

const browser = await chromium.launch({ channel: process.env.E2E_CHANNEL || undefined });
const page = await browser.newPage();
for (const [name, [size, markup]] of Object.entries(icons)) {
	await page.setViewportSize({ width: size, height: size });
	await page.setContent(`<style>html,body{margin:0;background:transparent}svg{display:block;width:${size}px;height:${size}px}</style>${markup}`);
	writeFileSync(new URL(`../static/icons/${name}`, import.meta.url), await page.screenshot({ omitBackground: true }));
	console.log(name);
}
await browser.close();
