import type { de } from './de';

export const en: typeof de = {
	chip: 'Appointment',
	allDay: 'All day',
	add: '+ Appointment',
	addFor: 'Add an appointment on {date} for {name}',
	view: {
		label: 'View',
		month: 'Month',
		week: 'Week'
	},
	week: {
		label: 'CW {week}',
		prev: 'Previous week',
		next: 'Next week',
		hideWeekend: 'Hide weekend',
		member: 'Member',
		noGroup: 'No group',
		you: '(you)',
		empty: 'Nobody to show.',
		loadFailed: 'The appointments could not be loaded.'
	},
	dialog: {
		titleNew: 'New appointment',
		titleEdit: 'Edit appointment',
		forName: 'For {name}',
		date: 'Date',
		from: 'From',
		to: 'To',
		allDay: 'All day',
		note: 'Note',
		notePlaceholder: 'e.g. doctor',
		hint: 'Appointments do not count as working time, but they count as covered for the core working hours. Past appointments stay editable.',
		absenceInstead: 'Add an absence instead',
		save: 'Save',
		saved: 'Appointment saved.',
		saveFailed: 'The appointment could not be saved.',
		deleteTitle: 'Delete appointment?',
		deleted: 'Appointment deleted.',
		deleteFailed: 'The appointment could not be deleted.',
		timesMissing: 'Enter a start and an end (end after start) or choose “All day”.'
	}
};
