import { dateKey } from './dateHelpers';

/** Notification types the bell shows (the request decisions are shown via the user-notifications request list). */
export const DECISION_TYPES = ['RequestAccepted', 'RequestDeclined'];

interface NotificationLike {
	type?: string | null;
	message?: string | null;
	relatedEntityId?: number | null;
}

/**
 * Where a notification leads. FailureEntry carries the entry id (`relatedEntityId`); the day
 * comes from that entry when it is still among the open failures, else from the start time
 * in the message ("… started 30.09.2026 08:00 …", org-local). A TimerReminder is about the
 * running timer, so it leads to the timer page.
 */
export function notificationTarget(
	n: NotificationLike,
	entries: { id?: number; startTime?: string }[]
): { href: string | null; date: string | null } {
	if (n.type === 'TimerReminder') return { href: '/time', date: null };
	if (n.type !== 'FailureEntry') return { href: null, date: null };
	const id = n.relatedEntityId ?? null;
	const entry = id != null ? entries.find((e) => e.id === id) : undefined;
	let date = entry?.startTime ? dateKey(new Date(entry.startTime)) : null;
	if (!date) {
		const m = n.message?.match(/(\d{2})\.(\d{2})\.(\d{4})/);
		if (m) date = `${m[3]}-${m[2]}-${m[1]}`;
	}
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
