import { describe, expect, it } from 'vitest';
import { closeWarnings, previousMonth, summaryTotals, tabFromQuery, validYear } from './accounting';
import { canGrantAccountant, canUseAccounting, seesAllHours } from './orgRules';

describe('accounting role', () => {
	it('opens the panel to owners, admins and accountants only', () => {
		expect(canUseAccounting({ role: 'Owner' })).toBe(true);
		expect(canUseAccounting({ role: 'Admin' })).toBe(true);
		expect(canUseAccounting({ role: 'Member', isAccountant: true })).toBe(true);
		expect(canUseAccounting({ role: 'Member', isAccountant: false })).toBe(false);
		expect(canUseAccounting(null)).toBe(false);
	});

	it('shows everybody\'s hours to accountants always, to admins only while the org allows it', () => {
		const hidden = { memberTimeEntryVisibility: false };
		const shown = { memberTimeEntryVisibility: true };
		expect(seesAllHours({ role: 'Member', isAccountant: true }, hidden)).toBe(true);
		expect(seesAllHours({ role: 'Owner' }, hidden)).toBe(false);
		expect(seesAllHours({ role: 'Admin' }, shown)).toBe(true);
		expect(seesAllHours({ role: 'Member' }, shown)).toBe(false);
		expect(seesAllHours(undefined, shown)).toBe(false);
	});

	it('lets the owner grant the role to anyone, admins only to other members', () => {
		expect(canGrantAccountant('Owner', 'Admin', false)).toBe(true);
		expect(canGrantAccountant('Owner', 'Owner', true)).toBe(true);
		expect(canGrantAccountant('Admin', 'Member', false)).toBe(true);
		expect(canGrantAccountant('Admin', 'Admin', false)).toBe(false);
		expect(canGrantAccountant('Admin', 'Admin', true)).toBe(false);
		expect(canGrantAccountant('Member', 'Member', false)).toBe(false);
	});
});

describe('accounting panel helpers', () => {
	it('sums the monthly summary', () => {
		expect(summaryTotals([
			{ targetHours: 160, workedHours: 150.25, overtimeHours: -9.75, vacationDays: 2, sickDays: 0.5, failureEntryCount: 1 },
			{ targetHours: 80.1, workedHours: 81.2, overtimeHours: 1.1, otherAbsenceDays: 1 }
		])).toEqual({
			targetHours: 240.1, workedHours: 231.45, overtimeHours: -8.65, vacationDays: 2, sickDays: 0.5, otherAbsenceDays: 1, failureEntryCount: 1
		});
		expect(summaryTotals([]).workedHours).toBe(0);
	});

	it('warns about failure entries and pending requests before closing', () => {
		expect(closeWarnings({ failureEntryCount: 0, pendingRequestCount: 0 })).toEqual([]);
		expect(closeWarnings(null)).toEqual([]);
		expect(closeWarnings({ failureEntryCount: 2, pendingRequestCount: 1 })).toEqual(['2 nicht gestoppte Timer', '1 offene Anfragen']);
	});

	it('picks the previous month, the tab and valid years', () => {
		expect(previousMonth(new Date(2026, 0, 15))).toEqual({ year: 2025, month: 12 });
		expect(previousMonth(new Date(2026, 9, 16))).toEqual({ year: 2026, month: 9 });
		expect(tabFromQuery('closing')).toBe('closing');
		expect(tabFromQuery('nope')).toBe('reports');
		expect(tabFromQuery(null)).toBe('reports');
		expect(validYear(2026)).toBe(true);
		expect(validYear(1999)).toBe(false);
		expect(validYear(2026.5)).toBe(false);
	});
});
