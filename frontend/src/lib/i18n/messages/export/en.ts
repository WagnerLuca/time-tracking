import type { de } from './de';

export const en: typeof de = {
	title: 'Export & import',
	tabs: { export: 'Export', import: 'Import', report: 'PDF report' },
	tabsLabel: 'Section',
	loadOrgFailed: 'Could not load the organization.',
	noOrg: {
		title: 'No organization selected',
		select: 'Select organization',
		createOrJoin: 'Create or join an organization',
		importText: 'Time entries are always imported into an organization. Select one first.',
		reportText: 'PDF reports are generated per organization. Select one first.',
		exportHint: 'No organization selected – only your own time entries across all organizations can be exported. Select an organization for the daily summary.'
	},
	member: {
		label: 'Exporting for',
		myself: 'Myself',
		scopeHint:
			'Scoped to {org}. Requires export permission for this member – organization admins always have it, group admins only if data export is enabled for a shared group.'
	},
	type: {
		label: 'Export type',
		entries: 'Individual time entries',
		daily: 'Daily summary'
	},
	filters: {
		title: 'Filters',
		scope: 'Organization',
		scopeCurrent: 'Current organization ({name})',
		scopeAll: 'All organizations',
		dailyHint: 'The daily summary always uses the current organization.',
		memberHint: 'Exporting for another member always uses the current organization.',
		range: 'Time range',
		ranges: {
			week: 'This week',
			month: 'This month',
			year: 'This year',
			all: 'All time',
			custom: 'Custom range'
		},
		from: 'From',
		to: 'To',
		rangeInvalid: 'The start date must not be after the end date.'
	},
	columns: {
		title: 'Columns',
		selectAll: 'Select all',
		deselectAll: 'Deselect all',
		none: 'Select at least one column.',
		Date: 'Date',
		DayOfWeek: 'Day of week',
		StartTime: 'Start time',
		EndTime: 'End time',
		Duration: 'Duration (h)',
		Pause: 'Pause (min)',
		NetDuration: 'Net duration (h)',
		Description: 'Description',
		Organization: 'Organization',
		TargetHours: 'Target (h)',
		WorkedHours: 'Worked (h)',
		NetWorkedHours: 'Net worked (h)',
		Overtime: 'Overtime',
		CumulativeOvertime: 'Cumulative overtime',
		Status: 'Status',
		HolidayName: 'Holiday',
		AbsenceType: 'Absence type',
		AbsenceNote: 'Absence note',
		Entries: 'Time entries'
	},
	blocked: {
		member: 'Exporting for another member requires an organization.',
		daily: 'The daily summary requires an organization.'
	},
	exporting: 'Exporting…',
	download: 'Download CSV',
	downloaded: 'CSV file downloaded.',
	failed: 'Export failed.',
	payroll: {
		title: 'Payroll (all members)',
		text: 'One CSV file for payroll: target, worked, overtime, balance, absences and open corrections of a month for every active member of {org}. German format (semicolon, decimal comma).',
		month: 'Payroll month',
		year: 'Payroll year',
		failed: 'The payroll export failed.'
	},
	import: {
		disabled: 'CSV import is disabled for you in this organization.',
		title: 'Import CSV file',
		description: 'Upload a CSV file with time entries. Required columns: {required}. Optional columns: {optional}.',
		needsApproval: 'CSV import requires admin approval. Preview your file first, then request approval for the selected rows.',
		file: 'CSV file',
		choose: 'Choose a .csv file…',
		wrongType: 'Please choose a .csv file.',
		empty: 'The file is empty.',
		reading: 'Reading…',
		preview: 'Preview file',
		previewFailed: 'Could not read the CSV file.',
		previewTitle: { one: 'Preview ({count} row)', other: 'Preview ({count} rows)' },
		duplicates: { one: '{count} duplicate', other: '{count} duplicates' },
		selected: '{count} selected',
		all: 'All',
		none: 'None',
		selectAllRows: 'Select all rows',
		selectRow: 'Select row {row}',
		col: {
			date: 'Date',
			start: 'Start',
			end: 'End',
			pause: 'Pause',
			description: 'Description',
			status: 'Status'
		},
		minutes: '{value} min',
		status: { duplicate: 'Duplicate', error: 'Error', ok: 'OK' },
		importing: 'Importing…',
		importCount: { one: 'Import {count} time entry', other: 'Import {count} time entries' },
		imported: { one: 'Imported {count} time entry.', other: 'Imported {count} time entries.' },
		skipped: 'Skipped {count}.',
		failed: 'Import failed.',
		requestApproval: 'Request import approval',
		approvalHint: 'An admin must approve before the time entries are imported.'
	},
	request: {
		title: 'Request CSV import approval',
		text: { one: 'This organization requires approval for CSV imports. Submit a request to import {count} time entry.', other: 'This organization requires approval for CSV imports. Submit a request to import {count} time entries.' },
		message: 'Message',
		placeholder: 'Add a note for the admin…',
		defaultMessage: 'Request to import {count} time entries from CSV ({total} parsed)',
		submit: 'Submit request',
		submitted: 'Import request submitted. An admin will review it.',
		failed: 'Could not submit the request.'
	},
	report: {
		monthlyTitle: 'Monthly report',
		monthlyText: 'Day-by-day working hours, target, overtime and status for one calendar month.',
		yearlyTitle: 'Yearly report',
		yearlyText: 'Month-by-month summary for one calendar year, with the running cumulative overtime balance.',
		month: 'Report month',
		year: 'Report year',
		generating: 'Generating…',
		download: 'Download PDF',
		downloaded: 'PDF report downloaded.',
		monthlyFailed: 'Could not generate the monthly report.',
		yearlyFailed: 'Could not generate the yearly report.'
	}
};
