import { afterEach, describe, expect, it } from 'vitest';
import { locale } from '$lib/stores/locale.svelte';
import {
	absenceTypeLabel,
	dayNamesShort,
	formatClock,
	formatDate,
	formatDelta,
	formatDuration,
	formatHours,
	formatHoursDecimal,
	formatRequestType,
	parseRequestData
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
	});
});
