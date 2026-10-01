import { dateKey } from './dateHelpers';

/**
 * Where a "FailureEntry" notification leads. The notification carries no entry id,
 * only the start time in its message ("… started 30.09.2026 08:00 …", org-local);
 * that day plus the matching still-open failure entry gives the History deep link.
 */
export function failureNotificationTarget(
	message: string | null | undefined,
	organizationId: number | null | undefined,
	failures: { id?: number; startTime?: string; organizationId?: number | null }[]
): { href: string; date: string | null } {
	const m = message?.match(/(\d{2})\.(\d{2})\.(\d{4})/);
	if (!m) return { href: '/history', date: null };
	const date = `${m[3]}-${m[2]}-${m[1]}`;
	const entry = failures.find(
		(f) => f.startTime && dateKey(new Date(f.startTime)) === date && (organizationId == null || f.organizationId === organizationId)
	);
	return { href: entry ? `/history?date=${date}&entry=${entry.id}` : `/history?date=${date}`, date };
}
