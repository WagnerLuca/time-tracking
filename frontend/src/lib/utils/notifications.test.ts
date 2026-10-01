import { describe, expect, it } from 'vitest';
import { bellNotifications, decisionNotificationIds, notificationTarget } from './notifications';

describe('notificationTarget', () => {
	const message = 'Your timer started 30.09.2026 08:15 ran past the end of the business day and was stopped.';
	const failures = [
		{ id: 7, startTime: '2026-09-29T06:00:00Z' },
		{ id: 9, startTime: '2026-09-30T06:15:00Z' }
	];

	it('deep-links to the day of the related failure entry', () => {
		expect(notificationTarget({ type: 'FailureEntry', relatedEntityId: 7, message }, failures)).toEqual({
			href: '/history?date=2026-09-29&entry=7',
			date: '2026-09-29'
		});
	});

	it('takes the day from the message when the entry is no longer open', () => {
		expect(notificationTarget({ type: 'FailureEntry', relatedEntityId: 42, message }, failures).href).toBe('/history?date=2026-09-30&entry=42');
		expect(notificationTarget({ type: 'FailureEntry', message }, failures).href).toBe('/history?date=2026-09-30');
		expect(notificationTarget({ type: 'FailureEntry', message: 'no date here' }, failures)).toEqual({ href: '/history', date: null });
	});

	it('sends timer reminders to the timer and other types nowhere', () => {
		expect(notificationTarget({ type: 'TimerReminder', relatedEntityId: 9 }, failures).href).toBe('/time');
		expect(notificationTarget({ type: 'SettingsChanged' }, failures).href).toBeNull();
	});
});

describe('bell notifications', () => {
	const items = [
		{ id: 1, type: 'FailureEntry', isRead: false },
		{ id: 2, type: 'RequestAccepted', relatedEntityId: 50, isRead: false },
		{ id: 3, type: 'RequestDeclined', relatedEntityId: 51, isRead: false },
		{ id: 4, type: 'SettingsChanged', isRead: true }
	];

	it('leaves out read ones and request decisions', () => {
		expect(bellNotifications(items).map((n) => n.id)).toEqual([1]);
	});

	it('finds the decision notifications of dismissed requests', () => {
		expect(decisionNotificationIds(items, [51, 99])).toEqual([3]);
	});
});
