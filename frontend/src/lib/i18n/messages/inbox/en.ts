import type { de } from './de';

export const en: typeof de = {
	title: 'Requests & notices',
	intro: 'Everything waiting for you: requests to decide, entries to correct and answers to your requests.',
	allDone: 'All done – nothing is waiting for you.',
	toDashboard: 'Go to the dashboard',
	approve: {
		title: 'To decide',
		empty: 'No open requests from your team.',
		via: 'via {group}',
		accept: 'Accept',
		decline: 'Decline',
		accepted: 'Request from {name} accepted.',
		declined: 'Request from {name} declined.',
		failed: 'Could not respond to the request.'
	},
	fix: {
		title: 'To correct',
		failure: 'Timer from {date} was not stopped',
		failureHint: 'It only counts once the end time is right.',
		correct: 'Correct end time'
	},
	notices: {
		title: 'Notices',
		open: 'View',
		done: 'Done',
		doneLabel: 'Mark notice “{title}” as done',
		failed: 'Could not dismiss the notice.'
	},
	own: {
		title: 'My open requests',
		waiting: 'Sent {time} · waiting for a decision',
		withdraw: 'Withdraw',
		withdrawLabel: 'Withdraw the request "{type}"',
		withdrawing: 'Withdrawing…',
		confirmTitle: 'Withdraw this request?',
		confirmMessage: 'The request "{type}" will no longer be decided and shows as "withdrawn" to you and the approvers. This cannot be undone, but you can send it again.',
		withdrawn: 'Request withdrawn.',
		withdrawFailed: 'Could not withdraw the request.'
	},
	decisions: {
		title: 'Answers to my requests',
		by: '{status} by {name}',
		dismiss: 'Mark read',
		dismissAll: 'Mark all as read',
		failed: 'Could not mark as read.'
	},
	history: {
		title: 'Decided requests',
		intro: 'The latest requests in {org} that you may decide.'
	}
};
