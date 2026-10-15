import type { de } from './de';

export const en: typeof de = {
	requestType: {
		JoinOrganization: 'Join',
		EditPastEntry: 'Edit entry',
		EditPause: 'Edit pause',
		SetInitialOvertime: 'Overtime',
		WorkScheduleChange: 'Schedule',
		CsvImport: 'CSV import',
		RequestAbsence: 'Absence',
		AddManualEntry: 'Add entry'
	},
	requestTypeFull: {
		JoinOrganization: 'Join organization',
		EditPastEntry: 'Edit past time entry',
		EditPause: 'Edit pause duration',
		SetInitialOvertime: 'Set initial overtime',
		WorkScheduleChange: 'Change work schedule',
		CsvImport: 'Import time entries from CSV',
		RequestAbsence: 'Absence / vacation request',
		AddManualEntry: 'Add a forgotten time entry'
	},
	requestStatus: {
		Accepted: 'Accepted',
		Declined: 'Declined',
		Pending: 'Pending',
		Withdrawn: 'Withdrawn'
	},
	requestData: {
		start: 'Start: {value}',
		end: 'End: {value}',
		note: 'Note: {value}',
		pause: 'Pause: {value} min',
		overtime: 'Overtime: {value} h',
		date: 'Date: {value}',
		range: '{count} days: {from} → {to}',
		type: 'Type: {value}',
		customType: 'Custom absence type',
		halfDaySuffix: ' (½ day)'
	},
	absenceType: {
		SickDay: 'Sick day',
		Vacation: 'Vacation',
		Other: 'Other'
	},
	dayType: {
		holiday: 'Holiday',
		sick: 'Sick day',
		vacation: 'Vacation',
		otherAbsence: 'Other absence',
		halfDay: '(half day)'
	},
	timeAgo: {
		justNow: 'just now'
	},
	role: {
		Owner: 'Owner',
		Admin: 'Admin',
		Member: 'Member'
	},
	ruleMode: {
		Disabled: 'Disabled',
		RequiresApproval: 'Requires approval',
		Allowed: 'Allowed'
	},
	joinPolicy: {
		Disabled: 'Admins only',
		RequiresApproval: 'Requires approval',
		Allowed: 'Open'
	},
	options: {
		disabled: 'Off',
		disabledHint: 'Feature is off for members',
		approval: 'Approval',
		approvalHint: 'Members must request; an admin approves',
		allowed: 'Allowed',
		allowedHint: 'Members can do this themselves',
		adminOnly: 'Admins only',
		adminOnlyHint: 'Only admins can add members',
		joinApprovalHint: 'Join requests need admin approval',
		open: 'Open',
		openHint: 'Anyone can join freely',
		inherit: 'Inherit',
		inheritHint: 'Use the parent group’s or the organization’s value'
	},
	on: 'On',
	off: 'Off',
	rules: {
		editPastEntriesMode: {
			label: 'Change own past entries',
			description: 'Whether members correct start and end of a finished entry themselves – directly, by request or not at all.'
		},
		editPauseMode: {
			label: 'Change own pause',
			description: 'Whether members may override the automatically deducted pause of an entry.'
		},
		initialOvertimeMode: {
			label: 'Overtime carry-over',
			description: 'Whether members enter their overtime from before time tracking themselves.'
		},
		joinPolicy: {
			label: 'Joining',
			description: 'How new people get into the organization – in addition to invitations.'
		},
		workScheduleChangeMode: {
			label: 'Change own work schedule',
			description: 'Whether members create and change their target hours (schedule periods) themselves.'
		},
		csvImportMode: {
			label: 'Import entries from CSV',
			description: 'Whether members may upload time entries from a CSV file.'
		},
		absenceSelfServiceMode: {
			label: 'Enter vacation & absences',
			description: 'Whether members enter vacation and other absences directly or request them – or only admins enter them.'
		},
		autoPauseEnabled: {
			label: 'Deduct pause automatically',
			description: 'Deducts pause from long working days by the pause rules (German Working Hours Act §4).'
		},
		memberTimeEntryVisibility: {
			label: 'Admins see working times',
			description: 'Admins see every member’s tracked time; members are told so.'
		},
		require2fa: {
			label: 'Require two-factor sign-in',
			description: 'Every member must set up 2FA before using the app.'
		},
	}
};
