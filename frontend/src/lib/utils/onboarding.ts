/**
 * First-steps checklists on the dashboard (issue #62). Steps are ticked from real data
 * (a schedule exists, an entry exists, …); "opened the page" steps and the dismissal are
 * remembered per user and organization in localStorage (a lost flag only re-shows the card).
 */
export type Checklist = 'member' | 'admin';
export interface Step {
	id: 'schedule' | 'timer' | 'teamCalendar' | 'invite' | 'rules' | 'holidays';
	done: boolean;
	href: string;
}

export function memberSteps(slug: string, f: { hasSchedule: boolean; hasEntries: boolean; sawTeamCalendar: boolean }): Step[] {
	return [
		{ id: 'schedule', done: f.hasSchedule, href: `/organizations/${slug}` },
		{ id: 'timer', done: f.hasEntries, href: '/time' },
		{ id: 'teamCalendar', done: f.sawTeamCalendar, href: `/organizations/${slug}/absences` }
	];
}

export function adminSteps(slug: string, f: { hasInvited: boolean; sawRules: boolean; hasHolidays: boolean }): Step[] {
	return [
		{ id: 'invite', done: f.hasInvited, href: `/organizations/${slug}/members` },
		{ id: 'rules', done: f.sawRules, href: `/organizations/${slug}/settings` },
		{ id: 'holidays', done: f.hasHolidays, href: `/organizations/${slug}/holidays` }
	];
}

const key = (userId: number, slug: string, what: string) => `tt-onboarding:${userId}:${slug}:${what}`;

function read(k: string): boolean {
	try {
		return localStorage.getItem(k) === '1';
	} catch {
		return false;
	}
}

function write(k: string) {
	try {
		localStorage.setItem(k, '1');
	} catch {
		// Storage blocked: the card just shows again next time
	}
}

/** Remember that a page of a checklist step was opened ('teamCalendar', 'rules'). */
export const markSeen = (userId: number, slug: string, what: 'teamCalendar' | 'rules') => write(key(userId, slug, `seen-${what}`));
export const wasSeen = (userId: number, slug: string, what: 'teamCalendar' | 'rules') => read(key(userId, slug, `seen-${what}`));
export const dismissChecklist = (userId: number, slug: string, list: Checklist) => write(key(userId, slug, `dismissed-${list}`));
export const isDismissed = (userId: number, slug: string, list: Checklist) => read(key(userId, slug, `dismissed-${list}`));
/** The card was shown unfinished once: finishing it later earns the one-time "all done" moment. */
export const markOpen = (userId: number, slug: string, list: Checklist) => write(key(userId, slug, `open-${list}`));
export const wasOpen = (userId: number, slug: string, list: Checklist) => read(key(userId, slug, `open-${list}`));
