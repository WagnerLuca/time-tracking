import type { de } from './de';

export const en: typeof de = {
	periods: {
		title: 'Schedule periods',
		add: '+ Add period',
		approvalNote: "Changing schedule periods needs approval. Ask an admin of your organization.",
		disabledNote: 'Schedule periods are managed by the admins of this organization.',
		validFrom: 'Valid from',
		validTo: 'Valid to',
		weeklyHours: 'Weekly hours',
		distribute: 'Distribute evenly (Mon–Fri)',
		dailyTargets: 'Daily targets (hours)',
		fromRequired: 'Choose a start date.',
		toBeforeFrom: 'The end must be on or after the start.',
		weeklyRange: 'Enter 0–{max} hours.',
		dailyRange: 'Daily targets must be between 0 and {max} hours.',
		replaceHint: 'A period starting on this date exists already — saving replaces it.',
		submitAdd: 'Add period',
		submitReplace: 'Replace period',
		empty: 'No schedule periods yet.',
		active: 'Active',
		delete: 'Delete period',
		deleteTitle: 'Delete schedule period?',
		added: 'Schedule period added.',
		updated: 'Schedule period updated.',
		deleted: 'Schedule period deleted.',
		loadFailed: 'Could not load the schedule periods.',
		saveFailed: 'Could not save the schedule period.',
		deleteFailed: 'Could not delete the schedule period.'
	},
	overtime: {
		title: 'Initial overtime',
		description: 'Carry-over overtime from before time tracking started.',
		hoursLabel: 'Hours (negative for a deficit)',
		invalid: 'Enter a number of hours.',
		range: 'Enter a value between -{max} and {max}.',
		approvalNote: 'Changing this value needs approval. Ask an admin of your organization.',
		disabledNote: 'This value is managed by the admins of this organization.',
		saved: 'Initial overtime saved.',
		saveFailed: 'Could not save the initial overtime.'
	},
	active: {
		title: 'Active schedule',
		weeklyHours: 'Weekly hours',
		fromPeriods: 'These values come from the schedule periods.',
		none: 'No schedule configured.'
	}
};
