import { beforeEach, describe, expect, it, vi } from 'vitest';

const api = vi.hoisted(() => ({
	admin: vi.fn(),
	user: vi.fn(),
	mine: vi.fn(),
	seen: vi.fn(),
	list: vi.fn(),
	read: vi.fn(),
	failures: vi.fn()
}));
vi.mock('$lib/apiClient', () => ({
	requestsApi: {
		apiV1OrganizationsNotificationsGet: api.admin,
		apiV1OrganizationsUserNotificationsGet: api.user,
		apiV1OrganizationsMyRequestsGet: api.mine,
		apiV1OrganizationsUserNotificationsMarkSeenPost: api.seen
	},
	notificationsApi: { apiV1NotificationsGet: api.list, apiV1NotificationsIdReadPut: api.read },
	timeTrackingApi: { apiV1TimeTrackingFailuresGet: api.failures }
}));

import { inbox } from './inbox.svelte';

const ok = <T>(data: T) => Promise.resolve({ data });

describe('inbox store', () => {
	beforeEach(() => {
		vi.clearAllMocks();
		inbox.reset();
		api.admin.mockReturnValue(ok({ requests: [{ id: 1 }] }));
		api.user.mockReturnValue(ok({ requests: [{ id: 5, status: 'Accepted' }] }));
		api.mine.mockReturnValue(ok({ items: [{ id: 6, status: 'Pending' }, { id: 7, status: 'Declined' }] }));
		api.list.mockReturnValue(ok({ items: [
			{ id: 10, type: 'CoreHoursViolation', isRead: false },
			{ id: 11, type: 'FailureEntry', relatedEntityId: 3, isRead: false },
			{ id: 12, type: 'RequestAccepted', relatedEntityId: 5, isRead: false }
		] }));
		api.failures.mockReturnValue(ok([{ id: 3 }]));
		api.read.mockReturnValue(ok(null));
		api.seen.mockReturnValue(ok(null));
	});

	it('loads every kind of pending work and counts it once', async () => {
		await inbox.load();
		expect(inbox.loaded).toBe(true);
		expect(inbox.toApprove.map((r) => r.id)).toEqual([1]);
		expect(inbox.ownOpen.map((r) => r.id)).toEqual([6]);
		expect(inbox.notices.map((n) => n.id)).toEqual([10]); // failure notice is listed as its open entry
		expect(inbox.count).toBe(1 + 1 + 1 + 1);
	});

	it('treats failed sources as empty', async () => {
		api.admin.mockReturnValue(Promise.reject(new Error('403')));
		api.failures.mockReturnValue(Promise.reject(new Error('down')));
		await inbox.load();
		expect(inbox.toApprove).toEqual([]);
		expect(inbox.failures).toEqual([]);
		expect(inbox.notices.map((n) => n.id)).toEqual([10, 11]);
	});

	it('throttles refreshes', async () => {
		await inbox.load();
		await inbox.refresh();
		expect(api.admin).toHaveBeenCalledTimes(1);
		await inbox.refresh(0);
		expect(api.admin).toHaveBeenCalledTimes(2);
	});

	it('marks notices read and dismisses decisions with their notifications', async () => {
		await inbox.load();
		await inbox.markRead([10]);
		expect(api.read).toHaveBeenCalledWith(10);
		expect(inbox.notices).toEqual([]);
		await inbox.dismissDecisions([5]);
		expect(api.seen).toHaveBeenCalledWith([5]);
		expect(api.read).toHaveBeenCalledWith(12);
		expect(inbox.decisions).toEqual([]);
	});
});
