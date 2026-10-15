import type { de } from './de';

export const en: typeof de = {
	fallbackName: 'Organization',
	notFound: 'Organization not found.',
	loadFailed: 'Could not load the organization.',
	reloadFailed: 'Could not reload the organization.',
	scheduleFailed: 'Could not load your work schedule.',
	permissionsFailed: 'Could not load your permissions.',
	absenceTypesFailed: 'Could not load the absence types.',
	markReadFailed: 'Could not mark the notification as read.',
	intro: 'Your work schedule, overtime, vacation and absences in this organization.',
	visibilityWarning: 'The admins of this organization can see your tracked working hours.',
	rulesChangedBefore: 'The organization rules have changed. See',
	rulesChangedLink: 'Settings',
	rulesChangedAfter: ' for details.',
	myAbsences: 'My absences',
	edit: {
		name: 'Name',
		slug: 'Slug (URL)',
		description: 'Description',
		website: 'Website',
		nameRequired: 'Enter a name.',
		slugRequired: 'Enter a slug.',
		slugInvalid: 'Use lowercase letters, digits and hyphens only.',
		saved: 'Organization saved.',
		saveFailed: 'Could not save the organization.'
	},
	general: {
		title: 'General',
		intro: "The organization's name, slug, description, website and time reference. Only admins see this section.",
		profile: 'Profile',
		time: 'Time',
		timeIntro: 'Time zone and business day end apply to every day, report and the automatic timer stop.',
		danger: 'Danger zone',
		deleteIntro: 'Deletes the organization. Every member loses access at once; this cannot be undone.',
		ownerOnly: 'Only the owner can delete the organization.',
		typeName: 'Type the name "{name}" to confirm'
	},
	delete: {
		confirm: 'Delete organization',
		deleted: 'Organization deleted.',
		failed: 'Could not delete the organization.'
	}
};
