import type { de } from './de';

export const en: typeof de = {
	title: 'Invite members',
	intro: 'The person gets an email with a link. They join once they sign in or register with exactly this address and open the link.',
	email: 'Email address',
	emailInvalid: 'Please enter a valid email address.',
	role: 'Role',
	send: 'Invite',
	sent: 'Invitation sent to {email}.',
	sendFailed: 'The invitation could not be sent.',
	loadFailed: 'Could not load the pending invitations.',
	pending: 'Pending invitations ({count})',
	expires: 'valid until {date}',
	revoke: 'Revoke',
	revokeLabel: 'Revoke the invitation to {email}',
	revokeTitle: 'Revoke invitation?',
	revokeMessage: 'The link in the email to {email} will stop working.',
	revoked: 'Invitation revoked.',
	revokeFailed: 'The invitation could not be revoked.',
	accept: {
		pageTitle: 'Invitation',
		accepting: 'Accepting the invitation…',
		joined: 'Welcome to {org}!',
		failed: 'The invitation could not be accepted.',
		missingToken: 'The invitation link is incomplete. Open the link from the email again.',
		signedInAs: 'You are signed in as {email}.',
		switchAccount: 'Sign in with another account',
		askAgain: 'Ask an admin of the organization to invite you again.',
		toOrgs: 'To organizations'
	}
};
