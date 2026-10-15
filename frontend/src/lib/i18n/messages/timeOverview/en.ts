import type { de } from './de';

export const en: typeof de = {
	pageTitle: 'Team time overview',
	title: 'Team time overview',
	back: 'Back to {org}',
	fallbackOrg: 'Organization',
	orgFailed: 'Could not load the organization.',
	noPermission: 'You may not view time entries. An admin or a group setting must grant visibility.',
	loadFailed: 'Could not load the time overview.',
	entriesFailed: 'Could not load the time entries.',
	prevWeek: 'Previous week',
	nextWeek: 'Next week',
	noMembers: 'No members found.',
	noEntries: 'No entries this week.',
	running: 'Running',
	pause: '−{minutes} min pause',
	toFix: '{count} to fix',
	toFixTitle: 'Timers stopped automatically at the end of the business day. Not counted until the member corrects them.',
	cols: {
		member: 'Member',
		role: 'Role',
		target: 'Target',
		tracked: 'Tracked',
		net: 'Net',
		entries: 'Entries',
		status: 'Status'
	}
};
