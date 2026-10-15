import type { de } from './de';

export const en: typeof de = {
	offline: {
		title: 'No connection – data will load as soon as you are back online.',
		hint: 'Actions that need the server are locked until then.'
	},
	update: {
		available: 'New version available',
		reload: 'Reload'
	},
	install: {
		title: 'Install the app',
		text: 'Install Time Tracking on your home screen: its own window, quick start, notifications.',
		button: 'Install app',
		installed: 'The app is installed on this device.',
		ios: 'On iPhone/iPad: tap "Share" in Safari, then choose "Add to Home Screen".',
		manual: 'Open the browser menu and choose "Install app" or "Add to Home screen".'
	},
	push: {
		title: 'Notifications on this device',
		text: 'Reminders, requests and core-hours notices as push notifications, even when the app is closed.',
		enable: 'Enable notifications',
		disable: 'Turn off on this device',
		on: 'This device receives notifications.',
		enabled: 'Notifications enabled.',
		disabled: 'Notifications turned off on this device.',
		failed: 'Notifications could not be enabled.',
		denied: 'Notifications are blocked for this site. Allow them in the browser or app settings.',
		unsupported: 'This browser does not support push notifications. On iPhone/iPad they only work once the app is added to the home screen.',
		notConfigured: 'Push notifications are not set up on this server.',
		categories: 'What should arrive? (applies to all your devices)',
		reminders: 'Reminders',
		remindersHint: 'Timer still running, timer stopped automatically',
		requests: 'Requests',
		requestsHint: 'New requests to decide, decisions on your requests',
		coreHours: 'Core hours',
		coreHoursHint: 'Core working hours missed',
		organization: 'Organization',
		organizationHint: 'Organization rules changed',
		saveFailed: 'The setting could not be saved.'
	}
};
