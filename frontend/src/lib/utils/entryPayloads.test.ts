import { afterEach, describe, expect, it } from 'vitest';
import { locale } from '$lib/stores/locale.svelte';
import { defaultEntryTimes, historyFieldLabel, historyKindLabel, historyValue, newEntryPayload, warningMessages } from './entryPayloads';

describe('newEntryPayload', () => {
	const form = { start: '2026-09-28T09:00', end: '2026-09-28T17:30', description: '  Workshop ', pause: 30 };

	it('converts local form times to UTC instants and trims the note', () => {
		expect(newEntryPayload(form, true)).toEqual({
			startTime: '2026-09-28T07:00:00.000Z',
			endTime: '2026-09-28T15:30:00.000Z',
			description: 'Workshop',
			pauseDurationMinutes: 30
		});
	});

	it('leaves the pause to the pause rules when empty or not allowed', () => {
		expect(newEntryPayload({ ...form, pause: '' }, true)).not.toHaveProperty('pauseDurationMinutes');
		expect(newEntryPayload({ ...form, pause: null }, true)).not.toHaveProperty('pauseDurationMinutes');
		expect(newEntryPayload(form, false)).not.toHaveProperty('pauseDurationMinutes');
		expect(newEntryPayload({ ...form, description: ' ' }, false).description).toBeNull();
	});

	it('clamps a negative pause to zero', () => {
		expect(newEntryPayload({ ...form, pause: -5 }, true).pauseDurationMinutes).toBe(0);
	});

	it('defaults to a 9-to-5 day', () => {
		expect(defaultEntryTimes('2026-09-28')).toEqual({ start: '2026-09-28T09:00', end: '2026-09-28T17:00' });
	});
});

describe('entry warnings and history labels', () => {
	afterEach(() => locale.set('de'));

	it('translates the Working Time Act warnings and drops unknown ones', () => {
		expect(warningMessages(['dailyMax', 'somethingNew'])).toEqual(['Arbeitszeitgesetz: An diesem Tag wurde mehr als 10 Stunden gearbeitet.']);
		locale.set('en');
		expect(warningMessages(['restPeriod'])).toEqual(['Working Time Act: less than 11 hours of rest before this entry.']);
		expect(warningMessages(null)).toEqual([]);
	});

	it('formats recorded history values', () => {
		expect(historyValue('StartTime', '2026-09-28T07:00:00Z')).toBe('28.09.2026, 09:00');
		expect(historyValue('PauseDurationMinutes', '30')).toBe('30 min');
		expect(historyValue('IsFailure', 'true')).toBe('Ja');
		expect(historyValue('Description', null)).toBe('leer');
		expect(historyFieldLabel('EndTime')).toBe('Ende');
		expect(historyFieldLabel('Unknown')).toBe('Unknown');
		expect(historyKindLabel('ApprovedRequest')).toBe('Per Antrag übernommen');
	});
});
