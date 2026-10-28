import { test, expect, type Page } from '@playwright/test';
import { API, apiToken, login, myOrgs } from './helpers';
import type {
	AccountingSummaryResponse,
	BalanceResponse,
	OrgChartResponse,
	OrganizationDetailResponse,
	OrganizationMemberResponse,
	TeamOverviewResponse,
	TimeEntryResponsePaginatedResponse,
	VacationOverviewResponse
} from '../../src/lib/api';

/**
 * Worst-case data for the screens that list people (docs/DESIGN_REWORK_PLAN.md, Phase 5, break-ui):
 * the screen data is mocked with page.route (signed in for real, so the shell is the real one),
 * every screen is rendered at desktop and phone width, must not scroll sideways, and is
 * screenshotted to test-results/ for a look. Names are plausible or at the schema limit
 * (first/last name ≤ 100, group name ≤ 100, personnel number ≤ 50).
 */

const OWNER = 'max.mueller@example.com';

const PEOPLE: { firstName: string; lastName: string; email: string }[] = [
	{ firstName: 'Aleksandra Wiśniewska-Kowalczyk', lastName: 'von Hohenzollern-Sigmaringen', email: 'a.wisniewska@example.com' },
	{ firstName: 'Bartholomew', lastName: 'Fitzgerald', email: 'bartholomew.fitzgerald@northwind-industries-holdings.example.com' },
	{ firstName: 'Jo', lastName: 'K', email: 'a@b.co' },
	{ firstName: 'نور الهدى', lastName: 'عبد الرحمن', email: 'noor@example.com' },
	{ firstName: '🦊', lastName: 'Fox', email: 'fox@example.com' },
	{ firstName: '👩🏽‍💻 Priya', lastName: 'Raghunathan', email: 'first.last+billing-notifications@example.com' },
	{ firstName: 'Đặng Thị', lastName: 'Ngọc Hân', email: 'dang@example.com' },
	{ firstName: 'Christopher Alexander', lastName: 'Montgomery III', email: 'ops@sub.department.region.example.co.uk' }
];
const GROUPS = [
	{ id: 9001, name: 'Arbeitszeitkontoausgleichsbeauftragtenstelle', color: '#7c9a6d', parentGroupId: null },
	{ id: 9002, name: 'Qualitätssicherung, Prozessoptimierung und Betriebsorganisation Süd', color: '#4a7ab0', parentGroupId: 9001 },
	{ id: 9003, name: 'IT', color: null, parentGroupId: 9002 }
];
const BALANCES = [9999, -9999, 0, 0.5, -42.25, 1284, null, 12];

const person = (i: number) => {
	const p = PEOPLE[i % PEOPLE.length];
	return i < PEOPLE.length ? p : { ...p, email: `${i}.${p.email}` };
};

function members(count: number, me: { id: number; firstName: string; lastName: string; email: string }): OrganizationMemberResponse[] {
	const list: OrganizationMemberResponse[] = [
		{ id: me.id, firstName: me.firstName, lastName: me.lastName, email: me.email, role: 'Owner', groups: [], visibleInfo: ['Member'], isPresent: true }
	];
	for (let i = 0; i < count - 1; i++) {
		const p = person(i);
		list.push({
			id: 10_000 + i,
			...p,
			role: i % 7 === 1 ? 'Admin' : 'Member',
			isAccountant: i % 5 === 2,
			isPresent: i % 3 === 0 ? true : i % 3 === 1 ? false : null,
			personnelNumber: i === 0 ? 'PN-2026-ARBEITSZEITKONTOAUSGLEICH-000000000000000001' : String(i + 1),
			groups: GROUPS.slice(0, (i % 3) + 1).map((g) => ({ groupId: g.id, name: g.name, color: g.color, isGroupAdmin: i % 4 === 0 })),
			vacationDaysPerYear: 30,
			vacationEntitlement: 30,
			vacationAvailable: i === 0 ? 365 : 30,
			vacationDaysUsed: 12.5,
			vacationDaysRemaining: i === 0 ? 352.5 : 17.5,
			vacationVisible: true,
			visibleInfo: ['Member', 'Email', 'Groups', 'Presence', 'Vacation', 'VacationBalance', 'TrackedTime']
		});
	}
	return list;
}

async function mockJson(page: Page, pattern: RegExp, body: unknown) {
	// only API calls: the dev server's module URLs (…/utils/balance.ts) match these patterns too
	await page.route(pattern, (route) =>
		route.request().method() === 'GET' && route.request().url().includes('/api/v1/') ? route.fulfill({ json: body }) : route.fallback()
	);
}

/** Nothing on the page may be wider than the viewport (a table may scroll inside its own box). */
async function expectNoSidewaysScroll(page: Page) {
	const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
	expect(overflow, 'page scrolls sideways').toBeLessThanOrEqual(0);
}

/** A viewport screenshot with `at` (the first rows) scrolled to the top: full pages of 500 rows show nothing. */
async function shoot(page: Page, name: string, at?: string) {
	if (at) await page.locator(at).first().evaluate((el) => el.scrollIntoView({ block: 'start' }));
	await page.screenshot({ path: test.info().outputPath(`${name}.png`) });
}

const WIDTHS = [
	{ name: 'desktop', width: 1280, height: 900 },
	{ name: 'phone', width: 360, height: 780 }
];

let slug = '';
let me = { id: 0, firstName: '', lastName: '', email: OWNER };
let realOrg: OrganizationDetailResponse;
let realMine: Awaited<ReturnType<typeof myOrgs>> = [];
const LONG_ORG = 'Gemeinnützige Wohnungsbaugenossenschaft Arbeitszeitkontoausgleich eG';

test.beforeAll(async ({ request }) => {
	const token = await apiToken(request, OWNER);
	const headers = { Authorization: `Bearer ${token}` };
	realMine = await myOrgs(request, token);
	slug = realMine[0].slug;
	const user = await (await request.get(`${API}/Auth/me`, { headers })).json();
	me = { id: user.id, firstName: user.firstName, lastName: user.lastName, email: OWNER };
	realOrg = await (await request.get(`${API}/Organizations/${slug}`, { headers })).json();
});

// The org switcher, breadcrumb and page intros take the name from the membership list
test.beforeEach(async ({ page }) => {
	await mockJson(page, /\/Organizations\/mine(\?|$)/, realMine.map((o) => ({ ...o, name: LONG_ORG, memberCount: 500 })));
});

const orgDetail = (count: number): OrganizationDetailResponse => ({
	...realOrg,
	name: LONG_ORG,
	memberTimeEntryVisibility: true,
	members: members(count, me)
});

for (const vp of WIDTHS) {
	test.describe(`worst case at ${vp.name} width`, () => {
		test.use({ viewport: { width: vp.width, height: vp.height } });

		for (const count of [1, 500]) {
			test(`member list with ${count} member(s)`, async ({ page }) => {
				const org = orgDetail(count);
				await mockJson(page, new RegExp(`/api/v1/Organizations/${slug}(\\?|$)`), org);
				await mockJson(page, /\/time-overview/, (org.members ?? []).map((m, i) => ({
					userId: m.id, firstName: m.firstName, lastName: m.lastName, netTrackedMinutes: i === 1 ? 9999 * 60 : 2400, entryCount: i, weeklyWorkHours: 40
				})));
				await login(page, OWNER);
				await page.goto(`/organizations/${slug}/members`);
				await expect(page.locator('main ul a[href*="/members/"]')).toHaveCount(count);
				await expectNoSidewaysScroll(page);
				await shoot(page, `members-${count}-${vp.name}`, 'main section ul');
			});
		}

		for (const count of [0, 500]) {
			test(`team overview with ${count} member(s)`, async ({ page }) => {
				const team: TeamOverviewResponse = {
					groups: GROUPS.map((g) => ({ id: g.id, name: g.name })),
					members: Array.from({ length: count }, (_, i) => ({
						userId: 10_000 + i,
						...person(i),
						groupIds: [GROUPS[i % 3].id],
						balanceHours: BALANCES[i % BALANCES.length],
						present: i % 3 === 0 ? true : i % 3 === 1 ? false : null,
						presentSince: i % 3 === 0 ? new Date().toISOString() : null,
						failureCount: i % 4 === 0 ? 1284 : 0,
						coreHoursViolationToday: i % 4 === 0
					}))
				};
				await mockJson(page, /\/team-overview/, team);
				await login(page, OWNER);
				await page.goto('/');
				if (count) await expect(page.getByTestId('team-card').first()).toBeVisible();
				else await expect(page.locator('#team-heading')).toBeVisible();
				await expectNoSidewaysScroll(page);
				await shoot(page, `team-${count}-${vp.name}`, '#team-heading');
			});
		}

		test('org chart', async ({ page }) => {
			const chart: OrgChartResponse = {
				organizationName: LONG_ORG,
				groups: GROUPS.map((g) => ({ ...g, description: 'Zuständig für Arbeitszeitkontoausgleichsvereinbarungen', canManageMembers: true })),
				people: members(500, me).map((m) => ({
					userId: m.id, firstName: m.firstName, lastName: m.lastName, email: m.email, role: m.role ?? 'Member',
					groups: (m.groups ?? []).map((g) => ({ groupId: g.groupId, isLead: g.isGroupAdmin })),
					present: m.isPresent, visibleInfo: m.visibleInfo
				}))
			};
			const period = { targetHours: 8, workedHours: 9999, overtimeHours: 9991 };
			await mockJson(page, /\/org-chart/, chart);
			await mockJson(page, /\/balance/, { cumulativeOvertimeHours: 9999.75, today: period, week: period, month: period } satisfies BalanceResponse);
			await mockJson(page, new RegExp(`/api/v1/Organizations/${slug}(\\?|$)`), orgDetail(1));
			await login(page, OWNER);
			await page.goto(`/organizations/${slug}/chart`);
			await expect(page.getByTestId('oc-node').first()).toBeVisible();
			await shoot(page, `chart-tree-${vp.name}`);
			// open the deepest group and the longest e-mail
			await page.getByRole('searchbox').fill('bartholomew');
			await page.getByTestId('oc-node').filter({ hasText: 'Bartholomew' }).first().click();
			await expectNoSidewaysScroll(page);
			await shoot(page, `chart-${vp.name}`);
		});

		test('member calendar month and year', async ({ page }) => {
			const now = new Date();
			const items = Array.from({ length: now.getDate() }, (_, d) => {
				const start = new Date(now.getFullYear(), now.getMonth(), d + 1, 0, 0);
				return {
					id: 70_000 + d, userId: me.id, organizationName: realOrg.name,
					description: d % 2 ? 'Arbeitszeitkontoausgleichsvereinbarungsüberprüfung' : 'https://example.com/workspaces/acme/projects/q3-launch/docs/9f8e7d6c5b4a?tab=comments',
					startTime: start.toISOString(), endTime: new Date(+start + 23.9 * 3_600_000).toISOString(),
					isRunning: false, durationMinutes: 1434, pauseDurationMinutes: 45, netDurationMinutes: 1389, source: 'Terminal',
					terminalStartName: 'Haupteingang Verwaltungsgebäude Nord, Drehkreuz 3'
				};
			});
			const entries: TimeEntryResponsePaginatedResponse = { items, totalCount: items.length, limit: 500, offset: 0, hasMore: false };
			const period = { targetHours: 8, workedHours: 9999, overtimeHours: 9991 };
			const balance: BalanceResponse = { cumulativeOvertimeHours: -9999.75, initialOvertimeHours: 0, today: period, week: period, month: period };
			await mockJson(page, /\/api\/v1\/TimeTracking\?/, entries);
			await mockJson(page, /\/balance/, balance);
			await login(page, OWNER);
			await page.goto(`/history?date=${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`);
			await expect(page.getByTestId('day-details')).toBeVisible();
			await expectNoSidewaysScroll(page);
			await shoot(page, `calendar-day-${vp.name}`);
			await shoot(page, `calendar-month-${vp.name}`, 'main h1');
			await page.getByRole('button', { name: 'Jahr', exact: true }).click();
			await expectNoSidewaysScroll(page);
			await shoot(page, `calendar-year-${vp.name}`);
		});

		test('accounting tables', async ({ page }) => {
			const people = members(500, me);
			const summary: AccountingSummaryResponse = {
				year: 2026, month: 9, isClosed: true,
				members: people.map((m, i) => ({
					userId: m.id, personnelNumber: m.personnelNumber, name: `${m.firstName} ${m.lastName}`, email: m.email ?? null,
					targetHours: 168, workedHours: i === 1 ? 9999.99 : 160.5, overtimeHours: i === 1 ? 9831.99 : -7.5,
					overtimeBalance: i % 2 ? -9999.75 : 9999.75, vacationDays: 365, sickDays: 0.5, otherAbsenceDays: 0,
					failureEntryCount: i % 4 === 0 ? 1284 : 0, isFormerMember: i === 3
				}))
			};
			const vacation: VacationOverviewResponse = {
				year: 2026,
				members: people.map((m, i) => ({
					userId: m.id, personnelNumber: m.personnelNumber, name: `${m.firstName} ${m.lastName}`,
					entitlement: 365, adjustment: i % 2 ? -12.5 : 7, carriedIn: 99.5, expired: 3, taken: 300.5, planned: 0,
					remaining: 161, sickDays: 365, otherAbsenceDays: 0.5
				}))
			};
			await mockJson(page, /\/accounting\/summary\?/, summary);
			await mockJson(page, /\/accounting\/vacation\?/, vacation);
			await mockJson(page, new RegExp(`/api/v1/Organizations/${slug}(\\?|$)`), orgDetail(1));
			await login(page, OWNER);
			await page.goto(`/organizations/${slug}/accounting?tab=reports`);
			await expect(page.getByTestId('accounting-summary').locator('tbody tr')).toHaveCount(500);
			await expectNoSidewaysScroll(page);
			await shoot(page, `accounting-reports-${vp.name}`, '[data-testid=accounting-summary]');
			await page.getByTestId('accounting-tab-vacation').click();
			await expect(page.getByTestId('accounting-vacation').locator('tbody tr')).toHaveCount(500);
			await expectNoSidewaysScroll(page);
			await shoot(page, `accounting-vacation-${vp.name}`, '[data-testid=accounting-vacation]');
		});
	});
}
