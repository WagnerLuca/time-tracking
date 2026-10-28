import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { API, apiToken, login } from './helpers';

/** yyyy-MM-dd in Berlin (the browser's and the seeded org's zone). */
function berlinDay(offsetDays = 0): string {
	const d = new Date(Date.now() + offsetDays * 86_400_000);
	return new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Berlin', year: 'numeric', month: '2-digit', day: '2-digit' }).format(d);
}

const SLUG = 'mueller-software';

/** Removes the user's appointments on `date` in the seeded org (as the owner, who may manage everyone's). */
async function removeAppointments(request: APIRequestContext, userId: number, date: string) {
	const headers = { Authorization: `Bearer ${await apiToken(request, 'max.mueller@example.com')}` };
	const res = await request.get(`${API}/organizations/${SLUG}/appointments?from=${date}&to=${date}&userId=${userId}`, { headers });
	expect(res.ok(), await res.text()).toBeTruthy();
	for (const a of (await res.json()) as { id: number }[]) {
		const del = await request.delete(`${API}/organizations/${SLUG}/appointments/${a.id}`, { headers });
		expect(del.ok(), await del.text()).toBeTruthy();
	}
}

async function userId(request: APIRequestContext, email: string): Promise<number> {
	const headers = { Authorization: `Bearer ${await apiToken(request, 'max.mueller@example.com')}` };
	const org = await (await request.get(`${API}/organizations/${SLUG}`, { headers })).json();
	return org.members.find((m: { email: string }) => m.email === email).id;
}

/** Opens the team calendar's week view on the week holding `date`. */
async function openWeek(page: Page, date: string) {
	await page.goto(`/organizations/${SLUG}/absences`);
	await page.getByTestId('view-toggle-week').click();
	await expect(page.getByTestId('week-view')).toBeVisible();
	// Tomorrow may be next week (a Sunday run)
	for (let i = 0; i < 2 && (await page.locator(`[data-testid$="-${date}"]`).count()) === 0; i++)
		await page.getByRole('button', { name: 'Nächste Woche' }).click();
}

test('week view: a member adds "Arzt 10–12", a colleague sees the slot only, the owner the note', async ({ page, request }) => {
	const tomorrow = berlinDay(1);
	const tom = await userId(request, 'tom.weber@example.com');
	await removeAppointments(request, tom, tomorrow);

	await login(page, 'tom.weber@example.com');
	await openWeek(page, tomorrow);
	await page.getByTestId(`week-cell-${tom}-${tomorrow}`).click();
	const dialog = page.getByRole('dialog');
	await dialog.getByLabel('Von').fill('10:00');
	await dialog.getByLabel('Bis').fill('12:00');
	await dialog.getByLabel('Notiz').fill('Arzt');
	await dialog.getByRole('button', { name: 'Speichern' }).click();
	const mine = page.getByTestId(`week-cell-${tom}-${tomorrow}`).getByTestId('appointment-chip');
	await expect(mine).toHaveText(/10–12 Arzt/);

	// The choice of view is remembered
	await page.reload();
	await expect(page.getByTestId('week-view')).toBeVisible();

	// A colleague (plain member): slot visible, note hidden by default
	await login(page, 'lisa.buchhaltung@example.com');
	await openWeek(page, tomorrow);
	await expect(page.getByTestId(`week-cell-${tom}-${tomorrow}`).getByTestId('appointment-chip')).toHaveText(/10–12 Termin/);

	// The owner sees the note
	await login(page, 'max.mueller@example.com');
	await openWeek(page, tomorrow);
	await expect(page.getByTestId(`week-cell-${tom}-${tomorrow}`).getByTestId('appointment-chip')).toHaveText(/10–12 Arzt/);

	await removeAppointments(request, tom, tomorrow);
});

test('week view on a phone: no page-wide horizontal scroll', async ({ page }) => {
	await page.setViewportSize({ width: 390, height: 844 });
	await login(page, 'tom.weber@example.com');
	await page.goto(`/organizations/${SLUG}/absences`);
	await page.getByTestId('view-toggle-week').click();
	await expect(page.getByTestId('week-view')).toBeVisible();
	const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
	expect(overflow).toBeLessThanOrEqual(0);
});

/**
 * Core hours with an appointment covering the window: a fresh org (no core-hours noise in the seeded one), a one-minute
 * window two minutes from now, nothing tracked but an all-day appointment entered in the week view — after the
 * background check (once a minute) the day carries no violation. The opposite (an uncovered window is flagged) is
 * core-hours.spec.ts; partial coverage and the "Termin … berücksichtigt" text are backend tests.
 */
test('core hours: a day covered by an appointment shows no violation', async ({ page, request }) => {
	test.setTimeout(300_000);
	const stamp = Date.now();
	const reg = await request.post(`${API}/Auth/register`, {
		data: { email: `e2e-appt-${stamp}@example.com`, password: 'Kernzeit-2026!', firstName: 'E2E', lastName: 'Termin' }
	});
	expect(reg.ok(), await reg.text()).toBeTruthy();
	const session = await reg.json();
	const headers = { Authorization: `Bearer ${session.accessToken}` };
	const slug = `e2e-appt-${stamp}`;
	expect((await request.post(`${API}/Organizations`, { headers, data: { name: `E2E Termin ${stamp}`, slug } })).ok()).toBeTruthy();

	const berlin = (d: Date) =>
		Object.fromEntries(
			new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Berlin', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
				.formatToParts(d)
				.map((p) => [p.type, p.value])
		);
	const startAt = new Date(Math.floor(Date.now() / 60_000) * 60_000 + 2 * 60_000);
	const endAt = new Date(startAt.getTime() + 60_000);
	const s = berlin(startAt);
	const e = berlin(endAt);
	const today = `${s.year}-${s.month}-${s.day}`;
	const me = (session.user as { id: number }).id;

	const schedule = await request.post(`${API}/organizations/${slug}/work-schedule/change`, {
		headers,
		data: { validFrom: today, distributeEvenly: false, targetMon: 8, targetTue: 8, targetWed: 8, targetThu: 8, targetFri: 8, targetSat: 8, targetSun: 8 }
	});
	expect(schedule.ok(), await schedule.text()).toBeTruthy();
	const settings = await request.put(`${API}/Organizations/${slug}/settings`, {
		headers,
		data: { coreHoursEnabled: true, coreHoursStart: `${s.hour}:${s.minute}`, coreHoursEnd: `${e.hour}:${e.minute}`, coreHoursToleranceMinutes: 0 }
	});
	expect(settings.ok(), await settings.text()).toBeTruthy();

	// The appointment, entered in the week view: all day today
	await page.goto('/login');
	await page.evaluate((s) => {
		localStorage.setItem('authToken', s.accessToken);
		localStorage.setItem('refreshToken', s.refreshToken);
		localStorage.setItem('authUser', JSON.stringify(s.user));
	}, session);
	await page.goto(`/organizations/${slug}/absences`);
	await page.getByTestId('view-toggle-week').click();
	await page.getByTestId(`week-cell-${me}-${today}`).click();
	const dialog = page.getByRole('dialog');
	await dialog.getByLabel('Ganztägig').check();
	await dialog.getByLabel('Notiz').fill('Fortbildung');
	await dialog.getByRole('button', { name: 'Speichern' }).click();
	await expect(page.getByTestId(`week-cell-${me}-${today}`).getByTestId('appointment-chip')).toBeVisible();

	// Past the window's end plus one run of the minute loop (and a margin): still no violation
	await page.waitForTimeout(Math.max(0, endAt.getTime() + 100_000 - Date.now()));
	const res = await request.get(`${API}/organizations/${slug}/members/${me}/compliance?from=${today}&to=${today}`, { headers });
	expect(res.ok()).toBeTruthy();
	expect(((await res.json()) as { coreHours?: boolean }[]).some((d) => d.coreHours)).toBe(false);
	await page.goto(`/organizations/${slug}/members/${me}`);
	await expect(page.locator(`[data-day="${today}"]`).getByTestId('compliance-marker')).toHaveCount(0);
});
