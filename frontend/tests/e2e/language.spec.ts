import { expect, test, type Page } from '@playwright/test';
import { de } from '../../src/lib/i18n/de';
import { login } from './helpers';

/** A raw key like "nav.history" on screen means a dictionary was not there when the page rendered. */
const rawKey = new RegExp(`\b(${Object.keys(de).join('|')})\.[a-zA-Z]+`);

async function expectNoRawKeys(page: Page) {
	expect((await page.locator('body').innerText()).match(rawKey)).toBeNull();
}

/** Only German is bundled; English is loaded on the switch and before the first render after a reload. */
test('language: DE → EN → DE without raw keys, English survives a reload', async ({ page }) => {
	await login(page, 'tom.weber@example.com');
	const nav = page.getByRole('navigation').first();
	await expect(nav.getByRole('link', { name: 'Verlauf' })).toBeVisible();
	await expectNoRawKeys(page);

	// On desktop the language toggle is in the user menu
	await page.getByRole('button', { name: 'Benutzermenü' }).click();
	await page.getByRole('menu').locator('button[lang="en"]').click();
	await expect(nav.getByRole('link', { name: 'History' })).toBeVisible();
	await expect(page.locator('html')).toHaveAttribute('lang', 'en');
	await expectNoRawKeys(page);

	await page.reload();
	await expect(nav.getByRole('link', { name: 'History' })).toBeVisible();
	await expectNoRawKeys(page);

	await page.getByRole('button', { name: 'User menu' }).click();
	await page.getByRole('menu').locator('button[lang="de"]').click();
	await expect(nav.getByRole('link', { name: 'Verlauf' })).toBeVisible();
	await expect(page.locator('html')).toHaveAttribute('lang', 'de');
	await expectNoRawKeys(page);
});
