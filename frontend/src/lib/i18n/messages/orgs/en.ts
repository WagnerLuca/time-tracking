import type { de } from './de';

export const en: typeof de = {
	title: 'Organizations',
	loading: 'Loading organizations…',
	loadFailed: 'Could not load organizations.',
	pendingLoadFailed: 'Could not load your pending join requests.',
	newOrg: '+ New organization',
	members: { one: '{count} member', other: '{count} members' },
	selected: 'Selected',
	welcome: {
		title: 'Get started with an organization',
		text: 'Time entries, work schedules, absences and reports belong to an organization – your company, team or club. Create your own or join an existing one.',
		createTitle: 'Create an organization',
		createText: 'You become its owner and can invite members and configure rules.',
		create: 'Create organization'
	},
	mine: 'My organizations',
	others: 'Other organizations',
	join: {
		title: 'Join an organization',
		text: "Enter the organization's ID (the part after /organizations/ in its link). Depending on its settings an admin may have to approve.",
		label: 'Organization ID',
		placeholder: 'my-company',
		invalid: 'Only lowercase letters, numbers and hyphens.',
		submit: 'Join',
		request: 'Request to join',
		joining: 'Joining…',
		joined: 'Joined the organization.',
		requested: 'Request sent. An admin has to approve it.',
		alreadyMember: 'You are already a member of this organization.',
		failed: 'Could not join the organization.',
		pending: 'Request pending',
		inviteOnly: 'Invite only'
	},
	create: {
		pageTitle: 'New organization',
		title: 'Create organization',
		back: '← Back to organizations',
		name: 'Name',
		namePlaceholder: 'My organization',
		nameRequired: 'Name is required.',
		slug: 'ID (slug)',
		slugHint: 'Used in links. Only lowercase letters, numbers and hyphens. Derived from the name if left empty.',
		slugMissing: 'Enter an ID (the name contains no usable characters).',
		description: 'Description',
		descriptionPlaceholder: 'A short description of the organization…',
		website: 'Website',
		websiteInvalid: 'Enter a full URL starting with https://.',
		submit: 'Create organization',
		created: 'Organization "{name}" created.',
		failed: 'Could not create the organization.'
	},
	select: {
		pageTitle: 'Select organization',
		title: 'Choose organization',
		subtitle: 'Which organization would you like to work with?',
		none: "You're not a member of any organization yet.",
		createOrJoin: 'Create or join an organization',
		withoutOrg: 'Continue without organization'
	},
	tabs: {
		label: 'Organization',
		back: '← Back to organizations',
		general: 'General',
		schedule: 'My schedule',
		members: 'Members',
		groups: 'Groups',
		chart: 'Org chart',
		absences: 'Absences',
		holidays: 'Holidays',
		settings: 'Rules & visibility',
		rules: 'Rules',
		terminal: 'Terminal',
		export: 'Export'
	}
};
