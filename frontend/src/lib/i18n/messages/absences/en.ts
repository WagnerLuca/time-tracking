import type { de } from './de';

export const en: typeof de = {
	title: 'Absences',
	notFound: 'Organization not found.',
	loadOrgFailed: 'Could not load the organization.',
	loadPermsFailed: 'Could not load your permissions.',
	loadTypesFailed: 'Could not load absence types.',
	refreshFailed: 'Could not refresh vacation balances.',
	loadHolidaysFailed: 'Could not load holidays.',
	loadFailed: 'Could not load absences.',
	intro: {
		admin: 'Calendar overview of absences and vacation days across all members. Right-click (or long-press) a day to add an absence.',
		request: 'Calendar overview of absences visible to you. Right-click (or long-press) a day to request an absence – a group or org admin approves it.',
		self: 'Calendar overview of absences visible to you. Right-click (or long-press) a day to add your own absence.',
		readOnly: 'Calendar overview of absences visible to you.'
	},
	noOthers: "Your organization doesn't show you other people's absences – you only see your own here.",
	deleteTitle: 'Delete absence?',
	deleteLabel: 'Delete absence',
	deleted: 'Absence deleted.',
	deleteFailed: 'Could not delete the absence.',
	saved: 'Absence saved.',
	prevMonth: 'Previous month',
	nextMonth: 'Next month',
	today: 'Today',
	show: 'Show',
	allMembers: 'All members',
	group: 'Group',
	allGroups: 'All groups',
	clearFilter: 'Clear',
	youSuffix: ' (you)',
	requestAbsence: 'Request absence',
	addAbsence: 'Add absence',
	emptyMonth: 'No absences or holidays this month.',
	emptyDay: 'Nobody is absent on this day.',
	halfDay: 'Half day',
	days: { one: '1 day', other: '{count} days' },
	legend: {
		vacationHint: 'Vacation colours vary by person'
	},
	balance: {
		title: 'Vacation days balance',
		taken: 'Taken',
		planned: 'Planned',
		none: 'No members have vacation days configured.',
		noneSelected: 'The selected member has no vacation days configured.',
		barTitle: '{taken} days taken, {planned} days planned',
		used: '{taken} of {total} days taken',
		plannedSuffix: ' · {planned} planned',
		left: '{remaining} left'
	},
	list: {
		title: 'Absence list',
		year: 'Year',
		member: 'Member',
		type: 'Type',
		allTypes: 'All types',
		empty: 'No absences found for {year}.',
		halfDay: '½ day',
		entries: { one: '1 entry', other: '{count} entries' },
		totalDays: { one: '(1 day total)', other: '({count} days total)' }
	},
	dialog: {
		existing: 'Absences on this day',
		requestDivider: 'Request absence',
		addDivider: 'Add new absence',
		requestHint: 'This will be sent as a request and needs approval by your group admin or an organization admin.',
		member: 'Member',
		selectMember: 'Select member…',
		memberRequired: 'Select a member.',
		from: 'From',
		to: 'To (optional)',
		toBeforeFrom: 'The end date must not be before the start date.',
		type: 'Type',
		note: 'Note (optional)',
		notePlaceholder: "e.g. Doctor's appointment",
		halfDay: 'Half day',
		workdaysHint: 'Only working days of the work schedule count (Mon–Fri without one), holidays do not.',
		sendRequest: 'Send request',
		add: 'Add absence',
		requestSent: {
			one: 'Absence request for 1 day sent for approval.',
			other: 'Absence request for {count} calendar days sent for approval; only your working days count.'
		},
		requestFailed: 'Could not send the request.',
		addFailed: 'Could not add the absence.',
		credit: {
			FillToTarget: 'Hours are filled up to the expected daily target.',
			FullCredit: 'The full daily target is credited.',
			None: 'Hours are kept as tracked – no credit.'
		}
	}
};
