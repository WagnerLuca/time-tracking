/** Strings for org invitations (members page panel + /invite accept page). Add keys to both objects. */
export const de = {
	title: 'Mitglieder einladen',
	intro: 'Die Person erhält eine E-Mail mit einem Link. Sie tritt bei, sobald sie sich mit genau dieser Adresse anmeldet oder registriert und den Link öffnet.',
	email: 'E-Mail-Adresse',
	emailInvalid: 'Bitte eine gültige E-Mail-Adresse eingeben.',
	role: 'Rolle',
	send: 'Einladen',
	sent: 'Einladung an {email} gesendet.',
	sendFailed: 'Die Einladung konnte nicht gesendet werden.',
	loadFailed: 'Die offenen Einladungen konnten nicht geladen werden.',
	pending: 'Offene Einladungen ({count})',
	expires: 'gültig bis {date}',
	revoke: 'Zurückziehen',
	revokeLabel: 'Einladung an {email} zurückziehen',
	revokeTitle: 'Einladung zurückziehen?',
	revokeMessage: 'Der Link in der E-Mail an {email} funktioniert danach nicht mehr.',
	revoked: 'Einladung zurückgezogen.',
	revokeFailed: 'Die Einladung konnte nicht zurückgezogen werden.',
	accept: {
		pageTitle: 'Einladung',
		accepting: 'Einladung wird angenommen…',
		joined: 'Willkommen bei {org}!',
		failed: 'Die Einladung konnte nicht angenommen werden.',
		missingToken: 'Der Einladungslink ist unvollständig. Öffne den Link aus der E-Mail erneut.',
		signedInAs: 'Du bist als {email} angemeldet.',
		switchAccount: 'Mit einem anderen Konto anmelden',
		askAgain: 'Bitte einen Admin der Organisation, dich erneut einzuladen.',
		toOrgs: 'Zu den Organisationen'
	}
};

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
