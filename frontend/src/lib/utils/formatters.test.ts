import { afterEach, describe, expect, it, vi } from 'vitest';
import { locale } from '$lib/stores/locale.svelte';
import {
	absenceTypeLabel,
	barWidth,
	dayNamesShort,
	formatClock,
	formatDate,
	formatDateFull,
	formatDateShort,
	formatDateTime,
	formatDelta,
	formatDuration,
	formatHours,
	formatHoursDecimal,
	formatNumber,
	formatRequestStatus,
	formatRequestType,
	formatRequestTypeFull,
	formatTime,
	formatTimeAgo,
	formatWeekLabel,
	getInitials,
	getMonthName,
	normalizeAbsenceType,
	parseRequestData,
	holidayRegionLabel,
	roleLabel,
	statusBadgeClass
} from './formatters';

describe('formatters', () => {
	afterEach(() => locale.set('de'));

	it('formatHours', () => {
		expect(formatHours(0)).toBe('0h');
		expect(formatHours(null)).toBe('0h');
		expect(formatHours(90)).toBe('1h 30m');
		expect(formatHours(60)).toBe('1h');
		expect(formatHours(30)).toBe('30m');
		expect(formatHours(-90)).toBe('-1h 30m');
		// fractional minutes (running timers) are rounded, never "1h 60m"
		expect(formatHours(119.7)).toBe('2h');
	});

	it('formatDelta always carries a sign', () => {
		expect(formatDelta(0)).toBe('±0h');
		expect(formatDelta(0.2)).toBe('±0h');
		expect(formatDelta(90)).toBe('+1h 30m');
		expect(formatDelta(-90)).toBe('-1h 30m');
		expect(formatDelta(-30)).toBe('-30m');
	});

	it('formatHoursDecimal follows the locale', () => {
		expect(formatHoursDecimal(0)).toBe('-');
		expect(formatHoursDecimal(90)).toBe('1,5h');
		expect(formatHoursDecimal(-90)).toBe('-1,5h');
		locale.set('en');
		expect(formatHoursDecimal(90)).toBe('1.5h');
	});

	it('formatDuration and formatClock', () => {
		expect(formatDuration(null)).toBe('-');
		expect(formatDuration(62)).toBe('1h 2m');
		expect(formatDuration(30)).toBe('30m');
		expect(formatClock(0)).toBe('00:00:00');
		expect(formatClock(3725)).toBe('01:02:05');
	});

	it('dates and weekday names are localized', () => {
		expect(formatDate('2026-01-05')).toBe('05.01.2026');
		expect(dayNamesShort()[0]).toMatch(/^Mo/);
		locale.set('en');
		expect(formatDate('2026-01-05')).toBe('05/01/2026');
		expect(dayNamesShort()[0]).toBe('Mon');
		expect(dayNamesShort()).toHaveLength(7);
	});

	it('domain labels are translated', () => {
		expect(formatRequestType('RequestAbsence')).toBe('Abwesenheit');
		expect(absenceTypeLabel('0')).toBe('Krankheitstag');
		locale.set('en');
		expect(formatRequestType('RequestAbsence')).toBe('Absence');
		expect(absenceTypeLabel('Vacation')).toBe('Vacation');
		expect(formatRequestType('SomethingNew')).toBe('SomethingNew');
	});

	it('parseRequestData renders absence requests', () => {
		const lines = parseRequestData('RequestAbsence', JSON.stringify({ dates: ['2026-03-02', '2026-03-03'], type: 1, isHalfDay: true, note: 'Urlaub' }));
		expect(lines).toEqual(['2 Tage: 02.03.2026 → 03.03.2026', 'Art: Urlaub (½ Tag)', 'Notiz: Urlaub']);
		expect(parseRequestData('EditPause', '15')).toEqual(['Pause: 15 Min.']);
		expect(parseRequestData('Unknown', 'raw')).toEqual(['raw']);
		const manual = JSON.stringify({ StartTime: '2026-09-28T06:00:00Z', EndTime: '2026-09-28T14:30:00Z', PauseDurationMinutes: 30, Description: 'Vergessen' });
		expect(parseRequestData('AddManualEntry', manual)).toEqual(['Beginn: 28.09.2026, 08:00', 'Ende: 28.09.2026, 16:30', 'Pause: 30 Min.', 'Notiz: Vergessen']);
		expect(parseRequestData('AddManualEntry', JSON.stringify({ StartTime: '2026-09-28T06:00:00Z', EndTime: '2026-09-28T07:00:00Z', PauseDurationMinutes: null, Description: null }))).toHaveLength(2);
		expect(formatRequestType('AddManualEntry')).toBe('Eintrag nachtragen');
	});
});

describe('formatters: locale switching (de vs en)', () => {
	afterEach(() => {
		locale.set('de');
		vi.useRealTimers();
	});

	it('date/time formats follow the UI language; times stay 24h in both', () => {
		// 06:00Z is 08:00 in Europe/Berlin (summer time)
		expect(formatTime('2026-09-28T06:00:00Z')).toBe('08:00');
		expect(formatTime('2026-01-05T13:30:00Z')).toBe('14:30');
		expect(formatDateTime('2026-01-05T13:30:00Z')).toBe('05.01.2026, 14:30');
		expect(formatDateShort('2026-01-05')).toBe('5. Jan.');
		expect(formatDateFull('2026-01-05')).toBe('5. Jan. 2026');
		expect(formatWeekLabel({ start: new Date(2026, 0, 5), end: new Date(2026, 0, 11) })).toBe('5. Jan. – 11. Jan.');
		expect(getMonthName(new Date(2026, 2, 1))).toBe('März');
		expect(formatNumber(1234.5)).toBe('1.234,5');
		locale.set('en');
		expect(formatTime('2026-01-05T13:30:00Z')).toBe('14:30');
		expect(formatDateTime('2026-01-05T13:30:00Z')).toBe('5 Jan 2026, 14:30');
		expect(formatDateShort('2026-01-05')).toBe('5 Jan');
		expect(formatDateFull(new Date(2026, 0, 5))).toBe('5 Jan 2026');
		expect(getMonthName(new Date(2026, 2, 1))).toBe('March');
		expect(formatNumber(1234.5)).toBe('1,234.5');
	});

	it('a bare YYYY-MM-DD is a local date, not UTC midnight', () => {
		expect(formatDate('2026-03-29')).toBe('29.03.2026');
		expect(formatDate(new Date(2026, 11, 31))).toBe('31.12.2026');
	});

	it('getMonthName defaults to the current month', () => {
		vi.useFakeTimers();
		vi.setSystemTime(new Date(2026, 6, 15, 12));
		expect(getMonthName()).toBe('Juli');
	});

	it('formatTimeAgo: just now, minutes, hours, days in both languages', () => {
		vi.useFakeTimers();
		const now = new Date('2026-10-07T12:00:00Z');
		vi.setSystemTime(now);
		const ago = (ms: number) => new Date(now.getTime() - ms).toISOString();
		expect(formatTimeAgo(null)).toBe('');
		expect(formatTimeAgo('')).toBe('');
		expect(formatTimeAgo(ago(30_000))).toBe('gerade eben');
		expect(formatTimeAgo(ago(5 * 60_000))).toBe('vor 5 Min.');
		expect(formatTimeAgo(ago(3 * 3600_000))).toBe('vor 3 Std.');
		expect(formatTimeAgo(ago(2 * 86400_000))).toBe('vor 2 Tagen');
		locale.set('en');
		expect(formatTimeAgo(ago(30_000))).toBe('just now');
		expect(formatTimeAgo(ago(5 * 60_000))).toBe('5 min ago');
		expect(formatTimeAgo(ago(59 * 60_000))).toBe('59 min ago');
		expect(formatTimeAgo(ago(60 * 60_000))).toBe('1 hr ago');
		expect(formatTimeAgo(ago(24 * 3600_000))).toBe('1 day ago');
	});
});

describe('formatters: durations edge cases', () => {
	it('formatDuration / formatHoursDecimal treat undefined like null', () => {
		expect(formatDuration(undefined)).toBe('-');
		expect(formatDuration(0)).toBe('-');
		expect(formatDuration(60)).toBe('1h 0m');
		expect(formatHoursDecimal(undefined)).toBe('-');
		expect(formatHoursDecimal(null)).toBe('-');
	});

	it('formatClock clamps negatives and drops fractions', () => {
		expect(formatClock(-5)).toBe('00:00:00');
		expect(formatClock(59.9)).toBe('00:00:59');
		expect(formatClock(100 * 3600)).toBe('100:00:00');
	});
});

describe('formatters: domain labels', () => {
	afterEach(() => locale.set('de'));

	it('request type labels, short and full, with unknown passthrough', () => {
		expect(formatRequestTypeFull('CsvImport')).toBe('Zeiteinträge per CSV importieren');
		expect(formatRequestTypeFull('Brand new')).toBe('Brand new');
		expect(formatRequestTypeFull(null)).toBe('Unbekannt');
		expect(formatRequestType(undefined)).toBe('Unbekannt');
		locale.set('en');
		expect(formatRequestType(null)).toBe('Unknown');
	});

	it('request status labels and badge classes', () => {
		expect(['Accepted', 'Declined', 'Pending'].map(formatRequestStatus)).toEqual(['Angenommen', 'Abgelehnt', 'Offen']);
		expect(formatRequestStatus('Withdrawn')).toBe('Zurückgezogen');
		expect(formatRequestStatus('Expired')).toBe('Expired');
		expect(formatRequestStatus(null)).toBe('Unbekannt');
		expect(['Accepted', 'Declined', 'Pending', 'Withdrawn', null].map(statusBadgeClass)).toEqual([
			'badge-success',
			'badge-error',
			'badge-warning',
			'badge-ghost',
			'badge-ghost'
		]);
	});

	it('absence types accept names and numeric enum strings', () => {
		expect(['SickDay', '0', 'Vacation', '1', 'Other', 'OtherAbsence', '2'].map(normalizeAbsenceType)).toEqual([
			'SickDay',
			'SickDay',
			'Vacation',
			'Vacation',
			'Other',
			'Other',
			'Other'
		]);
		expect(normalizeAbsenceType('3')).toBeNull();
		expect(normalizeAbsenceType(null)).toBeNull();
		expect(absenceTypeLabel('2')).toBe('Sonstiges');
		expect(absenceTypeLabel('Custom')).toBe('Custom');
		expect(absenceTypeLabel(undefined)).toBe('Unbekannt');
	});

	it('role labels', () => {
		expect(['Owner', 'Admin', 'Member'].map(roleLabel)).toEqual(['Inhaber', 'Admin', 'Mitglied']);
		expect(roleLabel('Guest')).toBe('Guest');
		expect(roleLabel(null)).toBe('');
		locale.set('en');
		expect(roleLabel('Owner')).toBe('Owner');
	});

	it('names holiday regions by key in the UI language, falling back to the server name', () => {
		const keys = ['DE', 'DE-BW', 'DE-BY', 'DE-BY-MH', 'DE-BE', 'DE-BB', 'DE-HB', 'DE-HH', 'DE-HE', 'DE-MV', 'DE-NI', 'DE-NW', 'DE-RP', 'DE-SL', 'DE-SN', 'DE-ST', 'DE-SH', 'DE-TH', 'AT', 'CH'];
		expect(holidayRegionLabel('DE-BY', 'x')).toBe('Bayern');
		locale.set('en');
		expect(holidayRegionLabel('DE-BY', 'Bayern')).toBe('Bavaria');
		expect(keys.map((k) => holidayRegionLabel(k)).every((name, i) => name && name !== keys[i])).toBe(true);
		expect(holidayRegionLabel('XX', 'Somewhere')).toBe('Somewhere');
	});

	it('getInitials', () => {
		expect(getInitials('anna', 'schmidt')).toBe('AS');
		expect(getInitials('Anna', null)).toBe('A');
		expect(getInitials(undefined, 'Schmidt')).toBe('S');
		expect(getInitials('', '')).toBe('?');
		expect(getInitials(null, undefined)).toBe('?');
	});

	it('barWidth is capped at 100 and handles a zero target', () => {
		expect(barWidth(240, 480)).toBe(50);
		expect(barWidth(600, 480)).toBe(100);
		expect(barWidth(0, 480)).toBe(0);
		expect(barWidth(30, 0)).toBe(100);
		expect(barWidth(0, 0)).toBe(0);
		expect(barWidth(30, -1)).toBe(100);
	});
});

describe('parseRequestData: remaining request shapes', () => {
	afterEach(() => locale.set('de'));

	it('empty data renders nothing', () => {
		expect(parseRequestData('RequestAbsence', null)).toEqual([]);
		expect(parseRequestData('EditPause', '')).toEqual([]);
	});

	it('edit requests (camelCase from the frontend), partial fields', () => {
		const data = JSON.stringify({ startTime: '2026-01-05T07:00:00Z', pauseDurationMinutes: 0 });
		expect(parseRequestData('EditPastEntry', data)).toEqual(['Beginn: 05.01.2026, 08:00', 'Pause: 0 Min.']);
		expect(parseRequestData('EditPastEntry', '{}')).toEqual([]);
	});

	it('initial overtime is a localized number', () => {
		expect(parseRequestData('SetInitialOvertime', '12.5')).toEqual(['Überstunden: 12,5 h']);
	});

	it('a single absence day, default and custom types', () => {
		expect(parseRequestData('RequestAbsence', JSON.stringify({ dates: ['2026-03-02'], type: 0 }))).toEqual(['Datum: 02.03.2026', 'Art: Krankheitstag']);
		// no type → Vacation (enum 1); no dates → no date line
		expect(parseRequestData('RequestAbsence', JSON.stringify({}))).toEqual(['Art: Urlaub']);
		expect(parseRequestData('RequestAbsence', JSON.stringify({ dates: [], customTypeId: 'abc', type: 2 }))).toEqual(['Art: Eigene Abwesenheitsart']);
	});

	it('malformed JSON falls back to the raw string', () => {
		expect(parseRequestData('RequestAbsence', 'not json')).toEqual(['not json']);
		expect(parseRequestData('AddManualEntry', '{broken')).toEqual(['{broken']);
	});
});
