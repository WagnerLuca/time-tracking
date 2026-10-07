import { readFile } from 'node:fs/promises';
import { expect, test, type Page } from '@playwright/test';
import { login } from './helpers';

async function download(page: Page, button: string) {
	const [file] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: button }).first().click()]);
	return { name: file.suggestedFilename(), body: await readFile((await file.path())!) };
}

/** Export page: CSV (entries + daily overview, German headers) and the monthly PDF report. */
test('export: CSV and PDF downloads', async ({ page }) => {
	await login(page, 'tom.weber@example.com');
	await page.goto('/export');

	const entries = await download(page, 'CSV herunterladen');
	expect(entries.name).toMatch(/^time-entries-.*\.csv$/);
	expect(entries.body.toString('utf8').split(/\r?\n/)[0]).toBe('Datum,Tag,Beginn,Ende,Dauer (h),Pause (min),Netto (h),Beschreibung');

	await page.getByLabel('Tagesübersicht').check();
	const daily = await download(page, 'CSV herunterladen');
	expect(daily.name).toMatch(/^daily-report-mueller-software-.*\.csv$/);
	const dailyHeader = daily.body.toString('utf8').split(/\r?\n/)[0];
	expect(dailyHeader.startsWith('Datum,')).toBeTruthy();
	expect(dailyHeader).toContain('Überstunden kumuliert (h)');

	await page.getByRole('button', { name: 'PDF-Bericht' }).click();
	const pdf = await download(page, 'PDF herunterladen'); // the monthly report comes first
	expect(pdf.name).toMatch(/^time-report-mueller-software-\d{4}-\d{2}\.pdf$/);
	expect(pdf.body.subarray(0, 4).toString('latin1')).toBe('%PDF');
});
