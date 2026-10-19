import type { de } from './de';

export const en: typeof de = {
	carry: {
		fromLastYear: { one: 'incl. {days} day from {year}', other: 'incl. {days} days from {year}' },
		fromLastYearUntil: { one: 'incl. {days} day from {year}, expires on {date}', other: 'incl. {days} days from {year}, expire on {date}' },
		expired: { one: '{days} day expired', other: '{days} days expired' },
		adjustment: 'Adjustment {days} days',
		adjustmentWithNote: 'Adjustment {days} days ({note})'
	},
	year: {
		open: 'Edit years',
		title: 'Vacation of {name}',
		year: 'Year',
		entitlement: 'Entitlement',
		entitlementHint: 'Days in this year. A year without its own value takes the latest earlier one, otherwise the days per year.',
		inherited: 'No value of its own: follows the previous year or the days per year ({days} days). Leaving it empty changes nothing.',
		own: "This year's own value.",
		clear: 'Remove own value',
		cleared: 'The {year} entitlement follows the previous year again.',
		adjustment: 'Adjustment (+/− days)',
		adjustmentHint: 'E.g. leftover days from the previous system or a credit. Negative takes days away.',
		note: 'Note on the adjustment',
		notePlaceholder: 'e.g. taken over from the old system',
		balance: 'Balance {year}',
		carriedIn: 'Carried over',
		expired: 'Expired',
		used: 'Taken + planned',
		remaining: 'Remaining',
		available: 'Available',
		updated: 'Last changed {date} by {name}',
		saved: 'Vacation {year} saved.',
		saveFailed: 'The vacation could not be saved.',
		loadFailed: 'The vacation could not be loaded.',
		entitlementRange: 'Please enter 0–365 days.',
		adjustmentRange: 'Please enter −365 to 365 days.',
		yearRange: 'Please enter a year between 2000 and 2100.',
		noteTooLong: 'At most 500 characters.'
	}
};
