import { expect, test } from '@playwright/test';
import { API, apiToken, login } from './helpers';

/**
 * Terminal enrolment: the owner creates a key, a terminal scans an unknown card (simulated
 * with an API request), the owner assigns it to Tom from "Unbekannte Karten", the next scan
 * punches Tom, and the entry shows its terminal source in Tom's history.
 */
test('terminal: unknown card is assigned from the list and then punches', async ({ page, request }) => {
	test.setTimeout(150_000);
	const stamp = Date.now();
	const terminal = `E2E Tor ${stamp}`;
	const card = `E2E:${stamp.toString(16).toUpperCase()}`;
	const hint = card.replace(':', '').slice(-4);
	// Tom's own timer must not be running (the scan below then starts one), and is stopped again at the end
	const tomToken = await apiToken(request, 'tom.weber@example.com');
	const stopTom = () =>
		request.post(`${API}/TimeTracking/stop`, {
			headers: { Authorization: `Bearer ${tomToken}` },
			data: {}
		});
	await stopTom();

	await login(page, 'max.mueller@example.com');
	await page.goto('/organizations/mueller-software/terminal');
	await page.getByPlaceholder('Name, z. B. Scanner Eingang').fill(terminal);
	await page.getByRole('button', { name: 'Schlüssel erstellen' }).click();
	const key = (await page.locator('code.select-all').textContent())?.trim() ?? '';
	expect(key).toMatch(/^tt_/);

	const punch = (badgeId: string) =>
		request.post(`${API}/terminal/punch`, {
			headers: { 'X-Api-Key': key },
			data: { badgeId }
		});
	const unknown = await punch(card);
	expect(unknown.status()).toBe(404);
	expect((await unknown.json()).code).toBe('terminal.unknownCard');

	await page.reload();
	const item = page.getByTestId('unknown-cards').locator('li', { hasText: `…${hint}` });
	await expect(item).toContainText(terminal);
	await item.getByRole('button', { name: 'Mitglied zuordnen' }).click();
	await item.getByRole('combobox').selectOption({ label: 'Tom Weber' });
	await item.getByRole('button', { name: 'Mitglied zuordnen' }).click();
	await expect(page.getByText('Karte zugeordnet.')).toBeVisible();
	await expect(page.getByTestId('unknown-cards').locator('li', { hasText: `…${hint}` })).toHaveCount(0);

	const ok = await punch(card.toLowerCase());
	try {
		expect(ok.status(), await ok.text()).toBe(200);
		const body = await ok.json();
		expect(body.action).toBe('Started');
		await page.getByRole('button', { name: 'Aktualisieren' }).click();
		await expect(page.getByTestId('punch-log').locator('tr', { hasText: terminal }).first()).toBeVisible();

		// Tom's history: the entry carries the terminal's name. After the rest of the suite the owner is
		// near the per-user rate limit, so this checks as Tom; one retry lands in the next limiter window.
		const day = await page.evaluate((iso: string) => {
			const d = new Date(iso);
			return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
		}, body.entry.startTime);
		await login(page, 'tom.weber@example.com');
		await expect(async () => {
			await page.goto('/history');
			await page.locator(`[data-day="${day}"]`).first().click();
			await expect(page.getByTestId('day-details').getByText(`Terminal ${terminal}`).first()).toBeVisible({ timeout: 5_000 });
		}).toPass({ timeout: 100_000, intervals: [65_000] });
	} finally {
		// Never leave Tom's timer running for the other specs
		await stopTom();
	}
});
