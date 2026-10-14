import { expect, test, type Page } from '@playwright/test';
import { API, apiToken, futureWorkday, login, removeAbsences } from './helpers';

/**
 * UX review (#62): one walk per role through its main tasks with the new navigation —
 * everyday places on top (and in the phone's bottom bar), administration behind "Organisation",
 * pending work in the inbox behind the bell.
 */
const SLUG = 'mueller-software';
const topNav = (page: Page) => page.getByRole('navigation', { name: 'Menü' });

async function noHorizontalScroll(page: Page) {
	expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);
}

test('member: timer by keyboard, forgotten day, vacation, team calendar, rules, inbox, phone', async ({ page, request }) => {
	await request.post(`${API}/TimeTracking/stop`, { headers: { Authorization: `Bearer ${await apiToken(request, 'tom.weber@example.com')}` }, data: {} });
	await login(page, 'tom.weber@example.com');
	await page.evaluate(() => Object.keys(localStorage).filter((k) => k.startsWith('tt-onboarding')).forEach((k) => localStorage.removeItem(k)));

	// Everyday destinations on top; no "Anfragen" for a member who approves nothing
	for (const name of ['Dashboard', 'Zeit', 'Verlauf', 'Teamkalender', 'Organisation'])
		await expect(topNav(page).getByRole('link', { name, exact: true })).toBeVisible();
	await expect(topNav(page).getByRole('link', { name: 'Anfragen' })).toHaveCount(0);

	// First steps: ticked from real data (schedule + entries exist), the team calendar still open
	const checklist = page.getByTestId('onboarding-member');
	await expect(checklist).toContainText('2 von 3 erledigt');

	// Start and stop the timer with the keyboard only
	const start = page.getByRole('button', { name: 'Timer starten' });
	await start.focus();
	await page.keyboard.press('Enter');
	const stop = page.getByRole('button', { name: 'Timer stoppen' });
	await expect(stop).toBeVisible();
	await stop.focus();
	await page.keyboard.press('Enter');
	await expect(start).toBeVisible();

	// Forgotten day: one tap opens yesterday's "add entry" form
	await page.getByRole('link', { name: '+ Vergessenen Tag nachtragen' }).click();
	await expect(page).toHaveURL(/\/history\?date=\d{4}-\d{2}-\d{2}&do=add/);
	await expect(page.getByText(/Vergessenen Eintrag für .* nachtragen/)).toBeVisible();
	await page.getByRole('button', { name: 'Abbrechen' }).first().click();

	// Vacation: one tap opens the absence dialog for today
	await page.goto('/');
	await page.getByRole('link', { name: '+ Urlaub oder Abwesenheit' }).click();
	await expect(page.getByRole('dialog')).toBeVisible();
	await page.keyboard.press('Escape');
	await expect(page.getByRole('dialog')).toHaveCount(0);

	// Who is absent this week: the team calendar, which also ticks the checklist
	await topNav(page).getByRole('link', { name: 'Teamkalender' }).click();
	await expect(page).toHaveURL(/\/absences$/);
	await topNav(page).getByRole('link', { name: 'Dashboard' }).click();
	await expect(checklist).toHaveCount(0);

	// What applies to me, in plain language
	await topNav(page).getByRole('link', { name: 'Organisation' }).click();
	await page.getByRole('navigation', { name: 'Organisation' }).getByRole('link', { name: 'Regeln' }).click();
	await expect(page.getByTestId('applied-editPastEntriesMode')).toContainText('Für dich gilt:');

	// The bell leads to the inbox
	await page.getByRole('link', { name: /Benachrichtigungen öffnen/ }).click();
	await expect(page.getByRole('heading', { name: 'Anfragen & Hinweise' })).toBeVisible();

	// Phone: bottom navigation, "Mehr" for the rest, nothing scrolls sideways
	await page.setViewportSize({ width: 390, height: 844 });
	const bottom = page.getByRole('navigation', { name: 'Hauptnavigation' });
	await bottom.getByRole('link', { name: 'Zeit' }).click();
	await expect(page).toHaveURL(/\/time$/);
	await noHorizontalScroll(page);
	await bottom.getByRole('button', { name: 'Mehr' }).click();
	await page.locator('#more-menu').getByRole('link', { name: 'Organisation' }).click();
	await expect(page).toHaveURL(new RegExp(`/organizations/${SLUG}$`));
	await noHorizontalScroll(page);
});

test('group lead: sees the team and decides a request inline in the inbox', async ({ page, request }) => {
	const stamp = Date.now();
	const date = futureWorkday(2031, stamp);
	const note = `E2E Lead ${stamp}`;
	await removeAbsences(request, 'lukas.hartmann@example.com', date);
	// Lukas (DevOps only) asks for a day off: it goes to the DevOps lead, Niklas
	const created = await request.post(`${API}/organizations/${SLUG}/requests`, {
		headers: { Authorization: `Bearer ${await apiToken(request, 'lukas.hartmann@example.com')}` },
		data: { type: 6, message: note, requestData: JSON.stringify({ dates: [date], type: 1, isHalfDay: false, note }) }
	});
	expect(created.ok(), await created.text()).toBeTruthy();

	try {
		await login(page, 'niklas.hoffmann@example.com');
		await expect(page.getByText('Mein Team')).toBeVisible();
		await expect(page.getByText('Lukas Hartmann').first()).toBeVisible();

		await topNav(page).getByRole('link', { name: /Anfragen/ }).click();
		const item = page.getByTestId('inbox-approve').getByTestId('inbox-item').filter({ hasText: note });
		await expect(item).toContainText('Lukas Hartmann');
		await item.getByRole('button', { name: 'Annehmen' }).click();
		await expect(page.getByText('Antrag von Lukas Hartmann angenommen.')).toBeVisible();
		await expect(item).toHaveCount(0);
	} finally {
		await removeAbsences(request, 'lukas.hartmann@example.com', date);
	}
});

test('admin: setup checklist and every Organisation section in one place', async ({ page }) => {
	await login(page, 'anna.schmidt@example.com');
	await page.evaluate(() => Object.keys(localStorage).filter((k) => k.startsWith('tt-onboarding')).forEach((k) => localStorage.removeItem(k)));
	await page.reload();

	// Setup checklist: members and holidays exist, the rules are not looked at yet; dismissible for good
	const setup = page.getByTestId('onboarding-admin');
	await expect(setup).toContainText('2 von 3 erledigt');
	await setup.getByRole('button', { name: 'Checkliste ausblenden' }).click();
	await expect(setup).toHaveCount(0);
	await page.reload();
	await expect(page.getByTestId('onboarding-member')).toBeVisible();
	await expect(setup).toHaveCount(0);

	await expect(topNav(page).getByRole('link', { name: /Anfragen/ })).toBeVisible();
	await topNav(page).getByRole('link', { name: 'Organisation' }).click();
	const sections = page.getByRole('navigation', { name: 'Organisation' });
	const visit = async (tab: string, check: () => Promise<void>) => {
		await sections.getByRole('link', { name: tab, exact: true }).click();
		await check();
	};
	await visit('Mitglieder', () => expect(page.getByRole('heading', { name: 'Mitglieder einladen' })).toBeVisible());
	await visit('Gruppen', () => expect(page.getByTestId('group-node').first()).toBeVisible());
	await visit('Regeln & Sichtbarkeit', async () => {
		await page.getByRole('link', { name: 'Sichtbarkeit', exact: true }).click();
		await expect(page.getByTestId('visibility-settings')).toBeInViewport();
	});
	await visit('Feiertage', () => expect(page).toHaveURL(/\/holidays$/));
	await visit('Terminal', () => expect(page.getByPlaceholder('Name, z. B. Scanner Eingang')).toBeVisible());
	await visit('Export', () => expect(page.getByText('Lohnabrechnung (alle Mitglieder)')).toBeVisible());

	// The rules page was opened: the setup checklist would now count it
	await page.evaluate(() => Object.keys(localStorage).filter((k) => k.includes('dismissed')).forEach((k) => localStorage.removeItem(k)));
	await topNav(page).getByRole('link', { name: 'Dashboard' }).click();
	await expect(setup).toHaveCount(0); // all three done → no card
});
