import { notificationsApi, requestsApi, timeTrackingApi } from '$lib/apiClient';
import type { NotificationResponse, OrgRequestResponse, TimeEntryResponse } from '$lib/api';
import { decisionNotificationIds, inboxNotices, openOwnRequests } from '$lib/utils/notifications';

/**
 * Everything that waits for the user, shared by the bell (count) and the /inbox page:
 * requests to approve, decisions on own requests, own open requests, open failure
 * entries and notices (core hours, reminders, settings changes). One loader, so the
 * badge and the page never disagree.
 */
function createInbox() {
	let toApprove = $state<OrgRequestResponse[]>([]);
	let decisions = $state<OrgRequestResponse[]>([]);
	let ownOpen = $state<OrgRequestResponse[]>([]);
	let notifications = $state<NotificationResponse[]>([]);
	let failures = $state<TimeEntryResponse[]>([]);
	let loaded = $state(false);
	let loadedAt = 0;

	const notices = $derived(inboxNotices(notifications, failures.map((f) => f.id)));
	const count = $derived(toApprove.length + decisions.length + notices.length + failures.length);

	async function load() {
		loadedAt = Date.now();
		const [approve, decided, mine, notifs, fails] = await Promise.allSettled([
			requestsApi.apiV1OrganizationsNotificationsGet(),
			requestsApi.apiV1OrganizationsUserNotificationsGet(),
			requestsApi.apiV1OrganizationsMyRequestsGet(undefined, 50),
			notificationsApi.apiV1NotificationsGet(true, 50),
			timeTrackingApi.apiV1TimeTrackingFailuresGet()
		]);
		toApprove = approve.status === 'fulfilled' ? (approve.value.data.requests ?? []) : [];
		decisions = decided.status === 'fulfilled' ? (decided.value.data.requests ?? []) : [];
		ownOpen = mine.status === 'fulfilled' ? openOwnRequests(mine.value.data.items ?? []) : [];
		notifications = notifs.status === 'fulfilled' ? (notifs.value.data.items ?? []).filter((n) => !n.isRead) : [];
		failures = fails.status === 'fulfilled' ? fails.value.data : [];
		loaded = true;
	}

	/** Reload unless the last load is younger than `maxAgeMs` (navigation refreshes are throttled). */
	function refresh(maxAgeMs = 30_000) {
		if (Date.now() - loadedAt >= maxAgeMs) return load();
	}

	async function markRead(ids: number[]) {
		notifications = notifications.filter((n) => n.id == null || !ids.includes(n.id));
		await Promise.all(ids.map((id) => notificationsApi.apiV1NotificationsIdReadPut(id)));
	}

	/** Hides decisions on own requests; the matching "accepted/declined" notifications are the same news. */
	async function dismissDecisions(requestIds: number[]) {
		await requestsApi.apiV1OrganizationsUserNotificationsMarkSeenPost(requestIds);
		decisions = decisions.filter((r) => !requestIds.includes(r.id!));
		const decisionIds = decisionNotificationIds(notifications, requestIds);
		if (decisionIds.length > 0) await markRead(decisionIds);
	}

	/** Takes back an own open request (only while pending; the server refuses a decided one). */
	async function withdraw(req: OrgRequestResponse) {
		await requestsApi.apiV1OrganizationsSlugRequestsIdWithdrawPost(req.organizationSlug!, req.id!);
		ownOpen = ownOpen.filter((r) => r.id !== req.id);
	}

	function reset() {
		toApprove = [];
		decisions = [];
		ownOpen = [];
		notifications = [];
		failures = [];
		loaded = false;
		loadedAt = 0;
	}

	return {
		get toApprove() { return toApprove; },
		get decisions() { return decisions; },
		get ownOpen() { return ownOpen; },
		get notices() { return notices; },
		get failures() { return failures; },
		get count() { return count; },
		get loaded() { return loaded; },
		load,
		refresh,
		markRead,
		dismissDecisions,
		withdraw,
		reset
	};
}

export const inbox = createInbox();
