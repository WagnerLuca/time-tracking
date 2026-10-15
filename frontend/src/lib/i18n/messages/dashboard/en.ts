import type { de } from './de';

export const en: typeof de = {
	quick: {
		label: 'Quick actions',
		addDay: '+ Add a forgotten day',
		absence: '+ Vacation or absence',
		teamCalendar: 'Who is in this week?'
	},
	pageTitle: 'Dashboard',
	greeting: 'Hello, {name}',
	timer: 'Timer',
	running: 'Timer running',
	notRunning: 'No timer running',
	forOrg: 'for {org}',
	startsFor: 'Starts for {org}',
	startsWithoutOrg: 'Starts without organization',
	noteAndWeek: 'Note & week',
	noOrg: {
		title: 'No organization yet',
		text: 'Targets, overtime, absences and reports work inside an organization – create your own or join an existing one.',
		create: 'Create organization',
		join: 'Join organization'
	},
	selectOrg: 'Select an organization in the menu above to see targets and balances.',
	noSchedule: {
		title: 'No work schedule set.',
		text: 'Targets and overtime need a work schedule with your weekly hours.',
		action: 'Set up work schedule'
	},
	statsFailed: 'Could not load your statistics.',
	today: 'Today',
	todayProgress: 'Today’s progress',
	weekBalance: 'Week balance',
	weekOfTarget: '{worked} of {target} so far',
	workedThisWeek: 'worked this week',
	cumulativeBalance: 'Cumulative balance',
	asOfToday: 'As of today, incl. initial overtime',
	needsSchedule: 'Needs a work schedule',
	vacation: {
		label: 'Vacation left',
		ofTotal: 'of {total} days',
		details: '{taken} taken · {planned} planned',
		open: 'Vacation left: {remaining} of {total} days – open absences'
	},
	thisWeek: 'This week',
	openTimer: 'Open timer',
	openHistory: 'Open history',
	total: 'Total: {value}',
	weekRange: '{from}–{to}',
	teamCalendar: 'Team calendar',
	team: {
		title: 'My team',
		presentCount: '{present} of {total} present',
		since: 'since {time}',
		notPresent: 'not clocked in',
		noDetails: 'No details shared',
		balance: 'Total balance',
		failures: 'Unresolved failure entries: {count}',
		coreHours: 'Core working hours missed today',
		allGroups: 'All groups',
		group: 'Group',
		search: 'Search team',
		searchPlaceholder: 'Search name …',
		empty: 'No team members.',
		noMatch: 'Nobody matches the selection.',
		loadFailed: 'Could not load the team overview.'
	}
};
