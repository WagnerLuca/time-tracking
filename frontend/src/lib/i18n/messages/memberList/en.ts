import type { de } from './de';

export const en: typeof de = {
	pageTitle: 'Members',
	notFound: 'Organization not found.',
	loadFailed: 'Could not load the organization.',
	weekFailed: "Could not load this week's hours.",
	introAdmin: 'Open a member for details, schedule editing and absence history.',
	introMember: 'Open a member to see their profile.',
	heading: 'Members ({count})',
	headingFiltered: 'Members ({count} of {total})',
	filterLabel: 'Filter by group',
	allGroups: 'All groups',
	noGroup: 'No group',
	you: 'You',
	vacationTitle: 'Vacation: {used} of {total} days used',
	vacationLeft: { one: '{days} vacation day left', other: '{days} vacation days left' },
	trackedTitle: 'Tracked this week',
	thisWeek: '{hours} this week',
	entries: { one: '{count} entry', other: '{count} entries' },
	weeklyTargetTitle: 'Weekly target'
};
