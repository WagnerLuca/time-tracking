import { afterEach, describe, expect, it } from 'vitest';
import { locale } from '$lib/stores/locale.svelte';
import {
	CARRY_OVER_MODES,
	adjustmentLine,
	carryOverLines,
	carryOverModeValue,
	carryOverRuleText,
	clampExpiryDay,
	expiryValue,
	formatExpiry,
	isValidExpiry,
	memberCarryFigures,
	openCarry,
	orgYear,
	parseExpiry,
	signedDays,
	vacationDetailLines,
	vacationTotal
} from './vacation';

describe('vacation balance lines', () => {
	afterEach(() => locale.set('de'));

	it('counts only carried days that are neither used nor lapsed as open', () => {
		expect(openCarry({})).toBe(0);
		expect(openCarry({ carriedIn: 5 })).toBe(5);
		expect(openCarry({ carriedIn: 5, carriedUsed: 2 })).toBe(3);
		expect(openCarry({ carriedIn: 5, carriedUsed: 2, expired: 3 })).toBe(0);
		expect(openCarry({ carriedIn: 2.5, carriedUsed: 0.5 })).toBe(2);
		// Never negative, even with inconsistent figures
		expect(openCarry({ carriedIn: 1, carriedUsed: 2, expired: 1 })).toBe(0);
	});

	it('says nothing without carry-over', () => {
		expect(carryOverLines({}, 2027)).toEqual([]);
		expect(carryOverLines({ carriedIn: 0, carriedUsed: 0, expired: 0, expiresOn: null }, 2027)).toEqual([]);
		// Everything carried is already used: no open line, nothing lapsed
		expect(carryOverLines({ carriedIn: 3, carriedUsed: 3, expiresOn: '2027-03-31' }, 2027)).toEqual([]);
		expect(vacationDetailLines({}, 2027)).toEqual([]);
	});

	it('names the open carried days with the previous year and the expiry date (de)', () => {
		expect(carryOverLines({ carriedIn: 5, carriedUsed: 2, expiresOn: '2027-03-31' }, 2027))
			.toEqual(['davon 3 Tage aus 2026, verfallen am 31.03.2027']);
		expect(carryOverLines({ carriedIn: 1, expiresOn: '2027-03-31' }, 2027))
			.toEqual(['davon 1 Tag aus 2026, verfällt am 31.03.2027']);
		// Unlimited: no date
		expect(carryOverLines({ carriedIn: 4, carriedUsed: 1 }, 2027)).toEqual(['davon 3 Tage aus 2026']);
		expect(carryOverLines({ carriedIn: 1 }, 2027)).toEqual(['davon 1 Tag aus 2026']);
		// Half days in the locale's number format (0.5 is plural)
		expect(carryOverLines({ carriedIn: 2.5, carriedUsed: 2 }, 2027)).toEqual(['davon 0,5 Tage aus 2026']);
	});

	it('names lapsed days once the expiry passed (de/en)', () => {
		expect(carryOverLines({ carriedIn: 5, carriedUsed: 2, expired: 3, expiresOn: '2027-03-31' }, 2027))
			.toEqual(['3 Tage verfallen']);
		expect(carryOverLines({ carriedIn: 1, expired: 1, expiresOn: '2027-03-31' }, 2027)).toEqual(['1 Tag verfallen']);
		expect(carryOverLines({ carriedIn: 1.5, carriedUsed: 1, expired: 0.5, expiresOn: '2027-03-31' }, 2027))
			.toEqual(['0,5 Tage verfallen']);
		// Both lines when some carry is still open and some has lapsed (inconsistent in practice, but rendered)
		expect(carryOverLines({ carriedIn: 6, carriedUsed: 1, expired: 2, expiresOn: '2027-03-31' }, 2027))
			.toEqual(['davon 3 Tage aus 2026, verfallen am 31.03.2027', '2 Tage verfallen']);

		locale.set('en');
		expect(carryOverLines({ carriedIn: 5, carriedUsed: 2, expired: 3 }, 2027)).toEqual(['3 days expired']);
		expect(carryOverLines({ carriedIn: 1, expired: 1 }, 2027)).toEqual(['1 day expired']);
		expect(carryOverLines({ carriedIn: 1.5, expired: 0.5, carriedUsed: 1 }, 2027)).toEqual(['0.5 days expired']);
	});

	it('words the open carry in English with an en-GB date', () => {
		locale.set('en');
		expect(carryOverLines({ carriedIn: 5, carriedUsed: 2, expiresOn: '2027-03-31' }, 2027))
			.toEqual(['incl. 3 days from 2026, expire on 31/03/2027']);
		expect(carryOverLines({ carriedIn: 1, expiresOn: '2027-03-31' }, 2027))
			.toEqual(['incl. 1 day from 2026, expires on 31/03/2027']);
		expect(carryOverLines({ carriedIn: 2 }, 2027)).toEqual(['incl. 2 days from 2026']);
		expect(carryOverLines({ carriedIn: 1 }, 2027)).toEqual(['incl. 1 day from 2026']);
	});

	it('signs day counts with a real minus sign', () => {
		expect(signedDays(2)).toBe('+2');
		expect(signedDays(-1.5)).toBe('−1,5');
		expect(signedDays(0.5)).toBe('+0,5');
		expect(signedDays(0)).toBe('0');
		locale.set('en');
		expect(signedDays(-1.5)).toBe('−1.5');
		expect(signedDays(2.5)).toBe('+2.5');
	});

	it('describes the adjustment with or without a note (de/en)', () => {
		expect(adjustmentLine({})).toBeNull();
		expect(adjustmentLine({ adjustment: 0, adjustmentNote: 'ignored' })).toBeNull();
		expect(adjustmentLine({ adjustment: 5 })).toBe('Korrektur +5 Tage');
		expect(adjustmentLine({ adjustment: -2.5, adjustmentNote: 'Altsystem' })).toBe('Korrektur −2,5 Tage (Altsystem)');
		expect(adjustmentLine({ adjustment: 1, adjustmentNote: null })).toBe('Korrektur +1 Tag');
		expect(adjustmentLine({ adjustment: -1, adjustmentNote: 'Altsystem' })).toBe('Korrektur −1 Tag (Altsystem)');
		locale.set('en');
		expect(adjustmentLine({ adjustment: 5 })).toBe('Adjustment +5 days');
		expect(adjustmentLine({ adjustment: -3, adjustmentNote: 'old system' })).toBe('Adjustment −3 days (old system)');
	});

	it('puts every detail line together, adjustment last', () => {
		expect(vacationDetailLines({ carriedIn: 5, carriedUsed: 2, expiresOn: '2027-03-31', adjustment: -1 }, 2027))
			.toEqual(['davon 3 Tage aus 2026, verfallen am 31.03.2027', 'Korrektur −1 Tag']);
		expect(vacationDetailLines({ adjustment: 2, adjustmentNote: 'Gutschrift' }, 2027)).toEqual(['Korrektur +2 Tage (Gutschrift)']);
		expect(vacationDetailLines({ carriedIn: 3, expired: 3 }, 2027)).toEqual(['3 Tage verfallen']);
		locale.set('en');
		expect(vacationDetailLines({ carriedIn: 3, carriedUsed: 1, adjustment: 1 }, 2027))
			.toEqual(['incl. 2 days from 2026', 'Adjustment +1 day']);
	});
});

describe('carry-over settings', () => {
	afterEach(() => locale.set('de'));

	it('lists the modes in the order of the select', () => {
		expect(CARRY_OVER_MODES).toEqual(['UntilDate', 'Unlimited', 'None']);
	});

	it('maps mode names to the request enum, UntilDate as the default', () => {
		expect(carryOverModeValue('None')).toBe(0);
		expect(carryOverModeValue('UntilDate')).toBe(1);
		expect(carryOverModeValue('Unlimited')).toBe(2);
		expect(carryOverModeValue(undefined)).toBe(1);
		expect(carryOverModeValue(null)).toBe(1);
		expect(carryOverModeValue('Sometimes')).toBe(1);
	});

	it('parses MM-DD and falls back to 31 March for anything else', () => {
		expect(parseExpiry('03-31')).toEqual({ month: 3, day: 31 });
		expect(parseExpiry('02-29')).toEqual({ month: 2, day: 29 });
		expect(parseExpiry('12-01')).toEqual({ month: 12, day: 1 });
		for (const bad of [null, undefined, '', '3-31', '03/31', '03-31-2027', 'MM-DD', '2027-03-31', ' 03-31'])
			expect(parseExpiry(bad)).toEqual({ month: 3, day: 31 });
	});

	it('accepts only days the month can have (29 February included)', () => {
		expect(isValidExpiry(3, 31)).toBe(true);
		expect(isValidExpiry(2, 29)).toBe(true);
		expect(isValidExpiry(12, 31)).toBe(true);
		expect(isValidExpiry(1, 1)).toBe(true);
		expect(isValidExpiry(2, 30)).toBe(false);
		expect(isValidExpiry(4, 31)).toBe(false);
		expect(isValidExpiry(0, 10)).toBe(false);
		expect(isValidExpiry(13, 1)).toBe(false);
		expect(isValidExpiry(3, 0)).toBe(false);
		expect(isValidExpiry(3.5, 1)).toBe(false);
		expect(isValidExpiry(3, 1.5)).toBe(false);
		expect(isValidExpiry(Number.NaN, 1)).toBe(false);
	});

	it('clamps the day to the month when the month changes (29 February allowed)', () => {
		expect(clampExpiryDay(2, 31)).toBe(29);
		expect(clampExpiryDay(4, 31)).toBe(30);
		expect(clampExpiryDay(3, 31)).toBe(31);
		expect(clampExpiryDay(3, 15)).toBe(15);
		expect(clampExpiryDay(3, 0)).toBe(1);
		expect(clampExpiryDay(3, -4)).toBe(1);
		expect(isValidExpiry(2, clampExpiryDay(2, 30))).toBe(true);
	});

	it('writes month/day back as MM-DD (round trip)', () => {
		expect(expiryValue(3, 31)).toBe('03-31');
		expect(expiryValue(12, 1)).toBe('12-01');
		expect(parseExpiry(expiryValue(2, 29))).toEqual({ month: 2, day: 29 });
	});

	it('formats the expiry without a year in the UI locale', () => {
		expect(formatExpiry('03-31')).toBe('31.03.');
		expect(formatExpiry('02-29')).toBe('29.02.');
		expect(formatExpiry(null)).toBe('31.03.');
		expect(formatExpiry('garbage')).toBe('31.03.');
		locale.set('en');
		expect(formatExpiry('03-31')).toBe('31/03');
		expect(formatExpiry('12-01')).toBe('01/12');
	});

	it('describes the rule in one line (de)', () => {
		expect(carryOverRuleText({ vacationCarryOverMode: 'UntilDate', vacationCarryOverExpiry: '03-31', vacationCarryOverSince: 2026 }))
			.toBe('Übertragen bis Stichtag (31.03.), Übertrag ab 2026');
		expect(carryOverRuleText({
			vacationCarryOverMode: 'UntilDate', vacationCarryOverExpiry: '06-30', vacationCarryOverMaxDays: 10, vacationCarryOverSince: 2025
		})).toBe('Übertragen bis Stichtag (30.06.), höchstens 10 Tage, Übertrag ab 2025');
		expect(carryOverRuleText({ vacationCarryOverMode: 'Unlimited', vacationCarryOverMaxDays: 2.5, vacationCarryOverSince: 2026 }))
			.toBe('Unbegrenzt übertragen, höchstens 2,5 Tage, Übertrag ab 2026');
		// A cap of 0 is a cap, not "no cap"
		expect(carryOverRuleText({ vacationCarryOverMode: 'Unlimited', vacationCarryOverMaxDays: 0, vacationCarryOverSince: 2026 }))
			.toBe('Unbegrenzt übertragen, höchstens 0 Tage, Übertrag ab 2026');
		expect(carryOverRuleText({ vacationCarryOverMode: 'None', vacationCarryOverMaxDays: 10, vacationCarryOverSince: 2026 }))
			.toBe('Verfällt zum Jahresende');
	});

	it('falls back to UntilDate, 31 March and the current year when fields are missing', () => {
		const year = new Date().getFullYear();
		expect(carryOverRuleText({})).toBe(`Übertragen bis Stichtag (31.03.), Übertrag ab ${year}`);
		expect(carryOverRuleText({ vacationCarryOverMode: 'Weird', vacationCarryOverMaxDays: null, vacationCarryOverSince: 2026 }))
			.toBe('Übertragen bis Stichtag (31.03.), Übertrag ab 2026');
	});

	it('describes the rule in one line (en)', () => {
		locale.set('en');
		expect(carryOverRuleText({ vacationCarryOverMode: 'UntilDate', vacationCarryOverExpiry: '03-31', vacationCarryOverMaxDays: 10, vacationCarryOverSince: 2026 }))
			.toBe('Carry over until a deadline (31/03), at most 10 days, carry-over from 2026');
		expect(carryOverRuleText({ vacationCarryOverMode: 'Unlimited', vacationCarryOverSince: 2027 }))
			.toBe('Carry over without limit, carry-over from 2027');
		expect(carryOverRuleText({ vacationCarryOverMode: 'None' })).toBe('Lapses at year end');
	});
});

describe('org-local year', () => {
	it("takes the year in the organization's time zone", () => {
		// 31 Dec 23:30 UTC is already 1 Jan in Berlin, still 31 Dec in New York
		const newYearsEve = new Date(Date.UTC(2026, 11, 31, 23, 30));
		expect(orgYear('Europe/Berlin', newYearsEve)).toBe(2027);
		expect(orgYear('UTC', newYearsEve)).toBe(2026);
		expect(orgYear('America/New_York', newYearsEve)).toBe(2026);
		// 1 Jan 03:00 UTC is still the old year in Los Angeles
		expect(orgYear('America/Los_Angeles', new Date(Date.UTC(2027, 0, 1, 3)))).toBe(2026);
		expect(orgYear('Asia/Tokyo', new Date(Date.UTC(2026, 11, 31, 16)))).toBe(2027);
	});

	it("falls back to the browser's zone (Europe/Berlin in tests) without or with an unknown time zone", () => {
		const newYearsEve = new Date(Date.UTC(2026, 11, 31, 23, 30));
		expect(orgYear(null, newYearsEve)).toBe(2027);
		expect(orgYear(undefined, newYearsEve)).toBe(2027);
		expect(orgYear('Not/AZone', newYearsEve)).toBe(2027);
		expect(orgYear()).toBe(new Date().getFullYear());
	});
});

describe('member vacation rows', () => {
	it('takes what the year offers, else the allowance of older responses, else 0', () => {
		expect(vacationTotal({ vacationAvailable: 32, vacationDaysPerYear: 30 })).toBe(32);
		expect(vacationTotal({ vacationAvailable: 0, vacationDaysPerYear: 30 })).toBe(0);
		expect(vacationTotal({ vacationAvailable: -2, vacationDaysPerYear: 30 })).toBe(-2);
		expect(vacationTotal({ vacationDaysPerYear: 30 })).toBe(30);
		expect(vacationTotal({})).toBe(0);
	});

	it('maps a member row to carry figures for the detail lines', () => {
		const row = {
			vacationDaysPerYear: 30, vacationAvailable: 32, vacationAdjustment: -1, vacationCarriedIn: 5,
			vacationCarriedUsed: 2, vacationExpired: 3, vacationExpiresOn: '2027-03-31'
		};
		expect(memberCarryFigures(row)).toEqual({
			carriedIn: 5, carriedUsed: 2, expired: 3, expiresOn: '2027-03-31', adjustment: -1
		});
		expect(vacationDetailLines(memberCarryFigures(row), 2027)).toEqual(['3 Tage verfallen', 'Korrektur −1 Tag']);
		expect(memberCarryFigures({})).toEqual({
			carriedIn: undefined, carriedUsed: undefined, expired: undefined, expiresOn: undefined, adjustment: undefined
		});
		expect(vacationDetailLines(memberCarryFigures({}), 2027)).toEqual([]);
	});
});
