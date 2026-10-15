import type { de } from './de';

export const en: typeof de = {
	start: 'Start',
	end: 'End',
	note: 'Note',
	notePlaceholder: 'Note (optional)',
	pauseMinutes: 'Pause (min)',
	endAfterStart: 'End must be after start.',
	running: 'Running',
	needsCorrection: 'Needs correction',
	needsCorrectionHint: 'The timer ran past the business day end and was stopped automatically. It counts as 0 until the end time is corrected.',
	source: {
		terminal: 'Terminal',
		terminalNamed: 'Terminal {name}',
		terminalRoute: 'Started at {start}, stopped at {stop}',
		manual: 'Added by hand',
		import: 'Imported',
		admin: 'Entered by an admin'
	},
	pauseBadge: '−{minutes} min pause',
	addPauseBadge: '+ Pause',
	editPause: 'Edit pause',
	requestPauseEdit: 'Request pause change',
	addPause: 'Add pause',
	requestPause: 'Request pause',
	editEntry: 'Edit time entry',
	requestEdit: 'Request change',
	deleteEntry: 'Delete time entry',
	deleteTitle: 'Delete time entry?',
	deleteMessage: 'The time entry is removed permanently.',
	saved: 'Time entry saved.',
	deleted: 'Time entry deleted.',
	saveFailed: 'Could not save the time entry.',
	deleteFailed: 'Could not delete the time entry.',
	pauseAuto: 'automatic',
	pauseAutoHint: "Leave empty: the organization's pause rules apply.",
	manual: {
		add: 'Add missed entry',
		addAdmin: 'Add entry',
		title: 'Add a forgotten entry for {date}',
		titleAdmin: 'Add an entry for {date}',
		needsApprovalHint: 'In this organization an added entry needs approval. It is sent as a request.',
		created: 'Time entry added.',
		requested: 'Request sent. The entry appears once an admin approves it.',
		failed: 'The entry could not be added.',
		endRequired: 'Please enter an end time.',
		disabled: 'Adding past entries is disabled in this organization.',
		overlap: 'The entry overlaps an existing entry.'
	},
	warnings: {
		dailyMax: 'Working Time Act: more than 10 hours were worked on this day.',
		restPeriod: 'Working Time Act: less than 11 hours of rest before this entry.'
	},
	history: {
		title: 'History',
		loading: 'Loading history…',
		empty: 'No changes recorded yet.',
		failed: 'Could not load the history.',
		by: '{name}',
		system: 'System',
		kind: {
			Created: 'Created',
			Updated: 'Changed',
			Deleted: 'Deleted',
			ApprovedRequest: 'Applied from a request'
		},
		field: {
			StartTime: 'Start',
			EndTime: 'End',
			PauseDurationMinutes: 'Pause',
			Description: 'Note',
			OrganizationId: 'Organization',
			IsFailure: 'Needs correction'
		},
		empty_value: 'empty',
		minutes: '{value} min'
	},
	request: {
		titleEdit: 'Request time entry change',
		titlePause: 'Request pause change',
		intro: 'Enter the new values. An admin will review and apply them.',
		newStart: 'New start',
		newEnd: 'New end',
		newPause: 'New pause (minutes)',
		message: 'Message for the admin (optional)',
		submit: 'Submit request',
		submitted: 'Request submitted. An admin will review it.',
		submitFailed: 'Could not submit the request.'
	}
};
