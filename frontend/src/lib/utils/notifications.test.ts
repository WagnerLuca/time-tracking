import { describe, expect, it } from 'vitest';
import { failureNotificationTarget } from './notifications';

describe('failureNotificationTarget', () => {
	const message = 'Your timer started 30.09.2026 08:15 ran past the end of the business day and was stopped.';
	const failures = [
		{ id: 7, startTime: '2026-09-29T06:00:00Z', organizationId: 1 },
		{ id: 9, startTime: '2026-09-30T06:15:00Z', organizationId: 1 }
	];

	it('deep-links to the day and the matching failure entry', () => {
		expect(failureNotificationTarget(message, 1, failures)).toEqual({ href: '/history?date=2026-09-30&entry=9', date: '2026-09-30' });
	});

	it('falls back to the day, then to /history', () => {
		expect(failureNotificationTarget(message, 2, failures).href).toBe('/history?date=2026-09-30');
		expect(failureNotificationTarget('no date here', 1, failures)).toEqual({ href: '/history', date: null });
	});
});
