import { dateKey } from './dateHelpers';

/** Notification types the bell shows (the request decisions are shown via the user-notifications request list). */
export const DECISION_TYPES = ['RequestAccepted', 'RequestDeclined'];

interface NotificationLike {
	type?: string | null;
	message?: string | null;
	relatedEntityId?: number | null;
}

/** The first dd.MM.yyyy date in a notification message as YYYY-MM-DD. */
function messageDate(message?: string | null): string | null {
	const m = message?.match(/(\d{2})\.(\d{2})\.(\d{4})/);
	return m ? `${m[3]}-${m[2]}-${m[1]}` : null;
}

/**
 * A CoreHoursViolation notice: its day and, when it is about someone else (`relatedEntityId`
 * is the affected member), their name from the message ("Tom Weber did not keep … on 03.03.2026 …").
 */
export function coreHoursNotice(n: NotificationLike, currentUserId: number | null | undefined): { date: string | null; name: string | null } {
	const own = n.relatedEntityId == null || n.relatedEntityId === currentUserId;
	return {
		date: messageDate(n.message),
		name: own ? null : (n.message?.match(/^(.+?) did not keep the core working hours/)?.[1] ?? null)
	};
}

/**
 * Where a notification leads. FailureEntry carries the entry id (`relatedEntityId`); the day
 * comes from that entry when it is still among the open failures, else from the start time
 * in the message ("… started 30.09.2026 08:00 …", org-local). A TimerReminder is about the
 * running timer, so it leads to the timer page. A CoreHoursViolation leads to the day in
 * History for the member themselves, to the member's page (in `orgSlug(organizationId)`) for admins.
 */
export function notificationTarget(
	n: NotificationLike & { organizationId?: number | null },
	entries: { id?: number; startTime?: string }[],
	currentUserId?: number | null,
	orgSlug?: (orgId: number) => string | null | undefined
): { href: string | null; date: string | null } {
	if (n.type === 'TimerReminder') return { href: '/time', date: null };
	if (n.type === 'CoreHoursViolation') {
		const date = messageDate(n.message);
		const slug = n.organizationId != null ? orgSlug?.(n.organizationId) : null;
		if (n.relatedEntityId != null && n.relatedEntityId !== currentUserId)
			return { href: slug ? `/organizations/${slug}/members/${n.relatedEntityId}` : null, date };
		return { href: date ? `/history?date=${date}` : '/history', date };
	}
	if (n.type !== 'FailureEntry') return { href: null, date: null };
	const id = n.relatedEntityId ?? null;
	const entry = id != null ? entries.find((e) => e.id === id) : undefined;
	const date = entry?.startTime ? dateKey(new Date(entry.startTime)) : messageDate(n.message);
	if (!date) return { href: '/history', date: null };
	return { href: id != null ? `/history?date=${date}&entry=${id}` : `/history?date=${date}`, date };
}

/** Unread notifications the bell lists: request decisions are left out (they show as requests). */
export function bellNotifications<T extends NotificationLike & { isRead?: boolean }>(items: T[]): T[] {
	return items.filter((n) => !n.isRead && !DECISION_TYPES.includes(n.type ?? ''));
}

/** Decision notifications belonging to the given request ids (marked read together with the request). */
export function decisionNotificationIds(items: (NotificationLike & { id?: number; isRead?: boolean })[], requestIds: number[]): number[] {
	return items
		.filter((n) => !n.isRead && DECISION_TYPES.includes(n.type ?? '') && n.relatedEntityId != null && requestIds.includes(n.relatedEntityId))
		.map((n) => n.id!)
		.filter((id) => id != null);
}

/**
 * Notices for the inbox: unread, no request decisions (they show as requests), and no
 * FailureEntry notice whose entry is still open — the inbox lists the open entry itself.
 */
export function inboxNotices<T extends NotificationLike & { isRead?: boolean }>(items: T[], openFailureIds: (number | undefined)[]): T[] {
	return bellNotifications(items).filter((n) => !(n.type === 'FailureEntry' && n.relatedEntityId != null && openFailureIds.includes(n.relatedEntityId)));
}

/** The caller's own requests that still wait for a decision, newest first. */
export function openOwnRequests<T extends { status?: string | null; createdAt?: string | null }>(items: T[]): T[] {
	return items.filter((r) => r.status === 'Pending').sort((a, b) => (b.createdAt ?? '').localeCompare(a.createdAt ?? ''));
}
