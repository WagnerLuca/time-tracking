import { expect, test } from '@playwright/test';
import { API } from './helpers';

/**
 * Core working hours: an admin switches them on in the org settings (they persist), and once the
 * background check has run after the window, the day carries the ⚠ marker in the calendar.
 * A fresh user + org keeps the seeded org free of core hours (and of their notifications).
 */
test('core hours: enable in settings, the missed day gets a calendar marker', async ({ page, request }) => {
	test.setTimeout(150_000);
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

	// Every day is a working day, so the check also applies on a weekend run
	const today = await page.evaluate(() => {
		const d = new Date();
		return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
	});
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

	// A window that is already over today (00:00–00:01), no tolerance
	const box = page.getByTestId('core-hours-settings');
	const toggle = box.getByRole('switch', { name: 'Kernarbeitszeit' });
	await expect(toggle).not.toBeChecked();
	await toggle.click();
	await expect(toggle).toBeChecked();
	await box.locator('#core-hours-start').fill('00:00');
	await box.locator('#core-hours-end').fill('00:01');
	await box.locator('#core-hours-end').dispatchEvent('change');
	await expect(page.getByText('Gespeichert').first()).toBeVisible();
	await box.locator('#core-hours-tolerance').fill('0');
	await box.locator('#core-hours-tolerance').dispatchEvent('change');

	await page.reload();
	await expect(box.getByRole('switch', { name: 'Kernarbeitszeit' })).toBeChecked();
	await expect(box.locator('#core-hours-start')).toHaveValue('00:00');
	await expect(box.locator('#core-hours-end')).toHaveValue('00:01');
	await expect(box.locator('#core-hours-tolerance')).toHaveValue('0');

	// The background job runs once a minute; nothing was tracked, so the day is missed
	const me = (session.user as { id: number }).id;
	await expect
		.poll(
			async () => {
				const res = await request.get(`${API}/organizations/${slug}/members/${me}/compliance?from=${today}&to=${today}`, { headers });
				return ((await res.json()) as { coreHours?: boolean }[]).some((d) => d.coreHours);
			},
			{ timeout: 100_000, intervals: [5_000] }
		)
		.toBe(true);

	await page.goto(`/organizations/${slug}/members/${me}`);
	const marker = page.locator(`[data-day="${today}"]`).getByTestId('compliance-marker');
	await expect(marker).toHaveAttribute('title', /Kernarbeitszeit 00:00–00:01 nicht eingehalten: keine Zeit erfasst/);
});
