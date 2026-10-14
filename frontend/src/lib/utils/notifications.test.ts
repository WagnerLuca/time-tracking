import { describe, expect, it } from 'vitest';
import { bellNotifications, coreHoursNotice, decisionNotificationIds, inboxNotices, notificationTarget, openOwnRequests } from './notifications';

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

describe('core hours notices', () => {
	const message = 'Tom Weber did not keep the core working hours on 03.03.2026 (09:00-15:00): late start, 20 min not covered.';
	const slugs = (id: number) => (id === 1 ? 'acme' : null);

	it('lead the member to the day and admins to the member', () => {
		expect(notificationTarget({ type: 'CoreHoursViolation', relatedEntityId: 7, organizationId: 1, message }, [], 7, slugs)).toEqual({
			href: '/history?date=2026-03-03',
			date: '2026-03-03'
		});
		expect(notificationTarget({ type: 'CoreHoursViolation', relatedEntityId: 7, organizationId: 1, message }, [], 2, slugs).href).toBe('/organizations/acme/members/7');
		expect(notificationTarget({ type: 'CoreHoursViolation', relatedEntityId: 7, organizationId: 9, message }, [], 2, slugs).href).toBeNull();
		expect(notificationTarget({ type: 'CoreHoursViolation', relatedEntityId: 7, message: 'x' }, [], 7).href).toBe('/history');
	});

	it('name the member only for others', () => {
		expect(coreHoursNotice({ relatedEntityId: 7, message }, 7)).toEqual({ date: '2026-03-03', name: null });
		expect(coreHoursNotice({ relatedEntityId: 7, message }, 2)).toEqual({ date: '2026-03-03', name: 'Tom Weber' });
		expect(coreHoursNotice({ relatedEntityId: 7, message: 'reworded' }, 2)).toEqual({ date: null, name: null });
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

describe('inbox helpers', () => {
	it('drops decisions, read items and failure notices whose entry is listed as open', () => {
		const items = [
			{ id: 1, type: 'FailureEntry', relatedEntityId: 7, isRead: false },
			{ id: 2, type: 'FailureEntry', relatedEntityId: 8, isRead: false },
			{ id: 3, type: 'RequestAccepted', relatedEntityId: 5, isRead: false },
			{ id: 4, type: 'CoreHoursViolation', isRead: false },
			{ id: 5, type: 'SettingsChanged', isRead: true }
		];
		expect(inboxNotices(items, [7]).map((n) => n.id)).toEqual([2, 4]);
	});

	it('keeps only pending own requests, newest first', () => {
		const reqs = [
			{ id: 1, status: 'Pending', createdAt: '2026-10-01T08:00:00Z' },
			{ id: 2, status: 'Accepted', createdAt: '2026-10-03T08:00:00Z' },
			{ id: 3, status: 'Pending', createdAt: '2026-10-02T08:00:00Z' },
			{ id: 4, status: 'Pending' }
		];
		expect(openOwnRequests(reqs).map((r) => r.id)).toEqual([3, 1, 4]);
	});
});
