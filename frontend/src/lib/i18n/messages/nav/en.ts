import type { de } from './de';

export const en: typeof de = {
	brand: 'Time Tracking',
	brandHome: 'Time Tracking – home',
	home: 'Dashboard',
	timer: 'Time',
	requests: 'Requests',
	more: 'More',
	bottom: 'Main navigation',
	skipToContent: 'Skip to content',
	history: 'History',
	export: 'Export',
	organization: 'Organization',
	teamCalendar: 'Team calendar',
	teamShort: 'Team',
	settings: 'Settings',
	signOut: 'Sign out',
	menu: 'Menu',
	openMenu: 'Open menu',
	userMenu: 'User menu',
	language: 'Language',
	switchLanguage: 'Switch language',
	timerRunning: 'Timer running for {elapsed} – open timer',
	org: {
		switch: 'Switch organization',
		current: 'Current organization: {name}',
		none: 'No organization',
		manage: 'Manage organizations',
		create: 'Create or join an organization'
	},
	notifications: {
		title: 'Notifications',
		open: 'Open notifications',
		openCount: 'Open notifications ({count} new)',
		myUpdates: 'My request updates',
		failureTitle: 'Timer stopped automatically',
		failureStarted: 'Started {date} – correct its end time',
		failureCorrect: 'Correct its end time',
		alerts: 'Notices',
		reminderTitle: 'Timer still running',
		reminderText: 'Your timer is still running and will be stopped automatically at the end of the business day. Stop it now?',
		settingsTitle: 'Organization settings changed',
		coreHoursTitle: 'Core working hours missed',
		coreHoursOwn: 'Your tracked time on {date} does not cover the core working hours.',
		coreHoursOther: '{name} did not keep the core working hours on {date}.',
		coreHoursLate: '(checked late)',
		dismissAll: 'Dismiss all',
		pending: 'Pending requests',
		empty: 'No notifications',
		by: 'by {name}',
		via: 'via {group}',
		status: 'Status',
		user: 'User',
		organization: 'Organization',
		routedVia: 'Routed via group',
		created: 'Created',
		responded: 'Responded',
		respondedBy: 'Responded by',
		message: 'Message',
		details: 'Request details',
		accept: 'Accept',
		decline: 'Decline',
		dismiss: 'Dismiss notification',
		accepted: 'Request accepted.',
		declined: 'Request declined.',
		respondFailed: 'Could not respond to the request.',
		dismissFailed: 'Could not dismiss the notification.',
		loadFailed: 'Could not load notifications.'
	},
	twoFactor: {
		title: 'Two-factor authentication required',
		text: 'This organization requires two-factor authentication. Set up 2FA in your account settings to continue.',
		goToSettings: 'Go to settings'
	},
	failure: {
		title: {
			one: '1 timer was not stopped',
			other: '{count} timers were not stopped'
		},
		text: {
			one: 'and ended automatically at the end of the business day. It does not count toward your hours until you correct the end time.',
			other: 'and ended automatically at the end of the business day. They do not count toward your hours until you correct the end times.'
		},
		correct: 'Correct now'
	}
};
