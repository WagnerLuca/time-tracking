import { expect, test, type APIRequestContext } from '@playwright/test';
import { API, apiToken, futureWorkday, login } from './helpers';

const SLUG = 'mueller-software';

/** Cancels Tom's planned (future) schedule, if any (left over from an aborted run). */
async function cancelFuture(request: APIRequestContext) {
	const headers = { Authorization: `Bearer ${await apiToken(request, 'tom.weber@example.com')}` };
	const plan = await (await request.get(`${API}/organizations/${SLUG}/work-schedule/plan`, { headers })).json();
	if (plan.future) await request.delete(`${API}/organizations/${SLUG}/work-schedule/future`, { headers });
}

const deDate = (key: string) => new Date(`${key}T00:00:00`).toLocaleDateString('de-DE', { month: 'short', day: 'numeric', year: 'numeric' });

/**
 * #82, Settings → Organisation: Tom (his org allows schedule changes) sees his current schedule (Mo–So, weekly total) and the
 * read-only history with his earlier schedule (seeded), plans a new one from a later day ("Ab <Datum>" card) and cancels it.
 */
test('work schedule: a member plans a change from a date, sees it, cancels it; the history shows the old one', async ({ page, request }) => {
	const stamp = Date.now();
	const validFrom = futureWorkday(2031, stamp);
	const hours = 12 + (stamp % 20) / 2; // 12 … 21.5, step 0.5
	await cancelFuture(request);

	try {
		await login(page, 'tom.weber@example.com');
		await page.goto('/settings');
		await page.getByRole('tab', { name: 'Organisation' }).click();
		const card = page.getByTestId('schedule-plan');
		await expect(card.getByTestId('schedule-current')).toContainText('40 h');

		const history = card.getByTestId('schedule-history');
		await history.locator('summary').click();
		await expect(history.locator('li')).not.toHaveCount(0);
		await expect(history).toContainText('30 h/Woche');

		await card.getByRole('button', { name: 'Ändern ab …' }).click();
		const form = card.getByTestId('schedule-form');
		await expect(form.getByLabel('Wochenstunden')).toHaveValue('40');
		await form.getByLabel('Gültig ab').fill(validFrom);
		await form.getByLabel('Wochenstunden').fill(String(hours));
		await form.getByRole('button', { name: 'Speichern' }).click();
		await expect(page.getByText(/Neues Arbeitszeitmodell ab .* geplant\./)).toBeVisible();

		const future = card.getByTestId('schedule-future');
		await expect(future).toContainText(deDate(validFrom));
		await expect(future).toContainText(`${new Intl.NumberFormat('de-DE').format(hours)} h`);

		await future.getByRole('button', { name: 'Planung verwerfen' }).click();
		await page.getByRole('dialog').getByRole('button', { name: 'Verwerfen' }).click();
		await expect(page.getByText('Geplantes Arbeitszeitmodell verworfen.')).toBeVisible();
		await expect(card.getByTestId('schedule-future')).toHaveCount(0);
	} finally {
		await cancelFuture(request);
	}
});

/**
 * #82, Anna on Tom's member page: one card with the current schedule; "Aktuelles korrigieren" opens the form with the current
 * values and the closed-month hint, saving them unchanged is a correction (re-runnable: nothing changes).
 */
test('work schedule: an admin corrects the current schedule on the member page', async ({ page, request }) => {
	const headers = { Authorization: `Bearer ${await apiToken(request, 'anna.schmidt@example.com')}` };
	const org = await (await request.get(`${API}/organizations/${SLUG}`, { headers })).json();
	const tomId: number = org.members.find((m: { email: string }) => m.email === 'tom.weber@example.com').id;

	await login(page, 'anna.schmidt@example.com');
	await page.goto(`/organizations/${SLUG}/members/${tomId}`);
	const card = page.getByTestId('schedule-plan');
	await expect(card.getByTestId('schedule-current')).toContainText('40 h');
	await card.getByRole('button', { name: 'Aktuelles korrigieren' }).click();
	const form = card.getByTestId('schedule-form');
	await expect(form).toContainText('Abgeschlossene Monate bleiben unverändert');
	await expect(form.getByLabel('Wochenstunden')).toHaveValue('40');
	await form.getByRole('button', { name: 'Korrektur speichern' }).click();
	await expect(page.getByText('Arbeitszeitmodell korrigiert.')).toBeVisible();
	await expect(card.getByTestId('schedule-current')).toContainText('40 h');
});
