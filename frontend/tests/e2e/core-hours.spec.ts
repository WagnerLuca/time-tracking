import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { API } from './helpers';

/** A fresh user + org (keeps the seeded org free of core hours and their notifications), signed in. */
async function freshOrg(page: Page, request: APIRequestContext, name: string) {
	const stamp = Date.now();
	const reg = await request.post(`${API}/Auth/register`, {
		data: { email: `e2e-${name}-${stamp}@example.com`, password: 'Kernzeit-2026!', firstName: 'E2E', lastName: 'Kernzeit' }
	});
	expect(reg.ok(), await reg.text()).toBeTruthy();
	const session = await reg.json();
	const headers = { Authorization: `Bearer ${session.accessToken}` };
	const slug = `e2e-${name}-${stamp}`;
	const org = await request.post(`${API}/Organizations`, { headers, data: { name: `E2E ${name} ${stamp}`, slug } });
	expect(org.ok(), await org.text()).toBeTruthy();
	await page.goto('/login');
	await page.evaluate((s) => {
		localStorage.setItem('authToken', s.accessToken);
		localStorage.setItem('refreshToken', s.refreshToken);
		localStorage.setItem('authUser', JSON.stringify(s.user));
	}, session);
	return { headers, slug };
}

/**
 * Core working hours: an admin sets a window and switches core hours on (with the backfill option,
 * which persists but finds nothing: a window that started before joining is never flagged). The
 * window starts two minutes from now and lasts one minute; nothing is tracked, so the on-time check
 * after its end flags the day and it carries the ⚠ marker. Backfill itself is covered by backend tests.
 */
test('core hours: enable in settings, a missed window gets a calendar marker', async ({ page, request }) => {
	test.setTimeout(330_000);
	const stamp = Date.now();
	const reg = await request.post(`${API}/Auth/register`, {
		data: { email: `e2e-core-${stamp}@example.com`, password: 'Kernzeit-2026!', firstName: 'E2E', lastName: 'Kernzeit' }
	});
	expect(reg.ok(), await reg.text()).toBeTruthy();
	const session = await reg.json();
	const headers = { Authorization: `Bearer ${session.accessToken}` };
	const slug = `e2e-core-${stamp}`;
	const org = await request.post(`${API}/Organizations`, { headers, data: { name: `E2E Kernzeit ${stamp}`, slug } });
	expect(org.ok(), await org.text()).toBeTruthy();

	// The window: the organization's local time (Europe/Berlin, the default) two minutes from now, one minute long
	const berlin = (d: Date) =>
		Object.fromEntries(
			new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Berlin', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
				.formatToParts(d)
				.map((p) => [p.type, p.value])
		);
	const startAt = new Date(Math.floor(Date.now() / 60_000) * 60_000 + 2 * 60_000);
	const s = berlin(startAt);
	const e = berlin(new Date(startAt.getTime() + 60_000));
	const today = `${s.year}-${s.month}-${s.day}`;
	const start = `${s.hour}:${s.minute}`;
	const end = `${e.hour}:${e.minute}`;

	// Every day is a working day, so the check also applies on a weekend run
	const schedule = await request.post(`${API}/organizations/${slug}/work-schedules`, {
		headers,
		data: { validFrom: today, distributeEvenly: false, targetMon: 8, targetTue: 8, targetWed: 8, targetThu: 8, targetFri: 8, targetSat: 8, targetSun: 8 }
	});
	expect(schedule.ok(), await schedule.text()).toBeTruthy();

	await page.goto('/login');
	await page.evaluate((s) => {
		localStorage.setItem('authToken', s.accessToken);
		localStorage.setItem('refreshToken', s.refreshToken);
		localStorage.setItem('authUser', JSON.stringify(s.user));
	}, session);
	await page.goto(`/organizations/${slug}/settings`);

	// The upcoming one-minute window, no tolerance; then on, with the backfill option ticked
	const box = page.getByTestId('core-hours-settings');
	const toggle = box.getByRole('switch', { name: 'Kernarbeitszeit' });
	await expect(toggle).not.toBeChecked();
	await box.locator('#core-hours-start').fill(start);
	await box.locator('#core-hours-end').fill(end);
	await box.locator('#core-hours-end').dispatchEvent('change');
	await expect(page.getByText('Gespeichert').first()).toBeVisible();
	await box.locator('#core-hours-tolerance').fill('0');
	await box.locator('#core-hours-tolerance').dispatchEvent('change');
	const backfill = box.getByTestId('core-hours-backfill');
	await backfill.getByRole('checkbox').check();
	await backfill.getByRole('spinbutton').fill('1');
	await toggle.click();
	await expect(toggle).toBeChecked();
	await expect(backfill).toBeHidden();

	await page.reload();
	await expect(box.getByRole('switch', { name: 'Kernarbeitszeit' })).toBeChecked();
	await expect(box.locator('#core-hours-start')).toHaveValue(start);
	await expect(box.locator('#core-hours-end')).toHaveValue(end);
	await expect(box.locator('#core-hours-tolerance')).toHaveValue('0');

	// The background job runs once a minute; after the window ends nothing was tracked, so the day is missed
	const me = (session.user as { id: number }).id;
	await expect
		.poll(
			async () => {
				const res = await request.get(`${API}/organizations/${slug}/members/${me}/compliance?from=${today}&to=${today}`, { headers });
				return ((await res.json()) as { coreHours?: boolean }[]).some((d) => d.coreHours);
			},
			{ timeout: 270_000, intervals: [5_000] }
		)
		.toBe(true);

	await page.goto(`/organizations/${slug}/members/${me}`);
	const marker = page.locator(`[data-day="${today}"]`).getByTestId('compliance-marker');
	await expect(marker).toHaveAttribute('title', new RegExp(`Kernarbeitszeit ${start}–${end} nicht eingehalten: keine Zeit erfasst`));
});

/**
 * Group overrides: a parent group sets its own tolerance, the sub-group shows it as inherited;
 * the sub-group's own window may run past midnight (night shift).
 */
test('core hours: group tolerance is inherited, a night window ends the next day', async ({ page, request }) => {
	const { headers, slug } = await freshOrg(page, request, 'core-group');
	const create = async (name: string) => {
		const res = await request.post(`${API}/Organizations/${slug}/groups`, { headers, data: { name } });
		expect(res.ok(), await res.text()).toBeTruthy();
		return (await res.json()).id as number;
	};
	const shift = await create('Schicht');
	const night = await create('Nacht');
	const moved = await request.put(`${API}/Organizations/${slug}/groups/${night}`, { headers, data: { name: 'Nacht', parentGroupId: shift } });
	expect(moved.ok(), await moved.text()).toBeTruthy();

	await page.goto(`/organizations/${slug}/groups`);
	const header = (name: string) => page.getByTestId('group-header').filter({ hasText: name });
	await header('Schicht').click();
	await page.getByRole('tab', { name: 'Berechtigungen' }).click();
	const tolerance = page.getByTestId('group-core-tolerance');
	await expect(tolerance.getByText('geerbt von der Organisation: 5 min')).toBeVisible();
	await tolerance.getByRole('button', { name: 'Eigene' }).click();
	await tolerance.getByRole('spinbutton').fill('20');
	await tolerance.getByRole('spinbutton').dispatchEvent('change');
	await expect(page.getByText('Berechtigung gespeichert.').first()).toBeVisible();

	await header('Nacht').click();
	await page.getByRole('tab', { name: 'Berechtigungen' }).click();
	await expect(tolerance.getByText('geerbt von Schicht: 20 min')).toBeVisible();
	const core = page.getByTestId('group-core-hours');
	await core.getByRole('button', { name: 'Eigene' }).click();
	await core.getByLabel('Beginn der Kernarbeitszeit').fill('22:00');
	await core.getByLabel('Beginn der Kernarbeitszeit').dispatchEvent('change');
	await core.getByLabel('Ende der Kernarbeitszeit').fill('02:00');
	await core.getByLabel('Ende der Kernarbeitszeit').dispatchEvent('change');
	await expect(core.getByText('endet am Folgetag')).toBeVisible();

	const perms = await request.get(`${API}/Organizations/${slug}/groups/${night}/permissions`, { headers });
	expect(await perms.json()).toMatchObject({ coreHoursMode: 'Own', coreHoursStart: '22:00', coreHoursEnd: '02:00', coreHoursToleranceMinutes: null });
});
