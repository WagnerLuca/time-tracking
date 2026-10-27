import type { de } from './de';

export const en: typeof de = {
	common: {
		forbidden: 'You do not have permission to do that.',
		unauthorized: 'Please sign in.',
		badRequest: 'The request is invalid.',
		notFound: 'Not found.',
		conflict: 'That conflicts with existing data.',
		unexpected: 'An unexpected error occurred.'
	},
	org: {
		notFound: 'Organization not found.',
		notMember: 'You are not a member of this organization.',
		personnelNumberTaken: 'Another member already has this personnel number.',
		userNotMember: 'This person is not a member of the organization.',
		memberNotFound: 'Member not found.',
		twoFactorRequired: 'This organization requires two-factor authentication. Set it up in your settings to continue.',
		adminRequired: 'Only organization admins can do that.',
		ownerRequired: 'Only the organization owner can do that.',
		accountantRequired: 'Only accounting and organization admins can do that.',
		slugRequired: 'The organization is missing.',
		featureDisabled: 'This feature is disabled in the organization.',
		featureRequiresApproval: 'This needs approval in this organization. Please submit a request.',
		slugTaken: 'This short name is already taken.',
		ownerRoleFixed: "The owner's role cannot be changed. Transfer ownership first.",
		ownerCannotLeave: 'The owner cannot leave. Delete the organization or transfer ownership first.',
		enableOwnTwoFactorFirst: 'Enable two-factor authentication for your own account first.',
		unknownTimeZone: 'Unknown time zone.',
		coreHoursOrder: 'Core working hours need a valid start and end time that differ (an end before the start runs past midnight).',
		coreHoursBackfillDays: 'You can check 1 to 31 past days.',
		carryOverExpiry: 'Please enter a valid expiry date (day and month).',
		carryOverMaxDays: 'The carry-over cap must be between 0 and 365 days.',
		carryOverSince: 'The carry-over start year must be between 2000 and the current year.',
		carryOverMode: 'Unknown carry-over rule.'
	},
	vacation: {
		entitlementRange: 'The vacation entitlement must be between 0 and 365 days.',
		adjustmentRange: 'The adjustment must be between −365 and 365 days.',
		noteTooLong: 'The note may have at most 500 characters.'
	},
	user: {
		notFound: 'User not found.'
	},
	auth: {
		invalidCredentials: 'Invalid email or password.',
		accountDeactivated: 'This account has been deactivated.',
		sessionEnded: 'Your session has ended. Please sign in again.',
		invalidRefreshToken: 'Your session has expired. Please sign in again.',
		emailInUse: 'This email address is already in use.',
		wrongCurrentPassword: 'The current password is incorrect.',
		wrongPassword: 'The password is incorrect.',
		currentPasswordRequired: 'Enter your current password to change your email address.',
		passwordTooShort: 'The password must be at least 8 characters long.',
		passwordNeedsUppercase: 'The password needs at least one uppercase letter.',
		passwordNeedsLowercase: 'The password needs at least one lowercase letter.',
		passwordNeedsDigit: 'The password needs at least one digit.',
		passwordNeedsSpecial: 'The password needs at least one special character.',
		mailCooldown: 'An email was just sent. Please wait a few minutes.',
		invalidLink: 'This link is invalid or has expired.',
		emailAlreadyConfirmed: 'Your email address is already confirmed.',
		twoFactorTokenExpired: 'The sign-in has expired. Please sign in again.',
		twoFactorLocked: 'Too many wrong codes. Try again in 15 minutes.',
		twoFactorInvalidCode: 'The code is invalid.',
		twoFactorAlreadyEnabled: 'Two-factor authentication is already enabled.',
		twoFactorSetupRequired: 'Start the two-factor setup first.',
		twoFactorNotEnabled: 'Two-factor authentication is not enabled.'
	},
	account: {
		ownsOrganizations: 'You still own organizations. Transfer or delete them first.',
		twoFactorRequiredByOrgs: 'Organizations you belong to require two-factor authentication. Leave them first, or ask an admin to turn the requirement off.'
	},
	entry: {
		startTooEarly: 'The start time must be in the year 2000 or later.',
		startInFuture: 'The start time cannot be in the future.',
		endBeforeStart: 'The end must be after the start.',
		tooLong: 'An entry can be at most 24 hours long.',
		notFound: 'Entry not found.',
		running: 'A running entry cannot be edited. Stop it first.',
		editDisabled: 'Editing past entries is disabled in this organization.',
		editRequiresApproval: 'Editing past entries needs approval. Please submit a request.',
		overlap: 'The entry overlaps an existing entry.',
		manualInFuture: 'An added entry must lie in the past.',
		addDisabled: 'Adding past entries is disabled in this organization.'
	},
	timer: {
		alreadyRunning: 'A timer is already running.',
		notRunning: 'No timer is running.'
	},
	range: {
		tooEarly: 'Dates must be in the year 2000 or later.',
		endBeforeStart: 'The end date must not be before the start date.',
		tooLong: 'A range can cover at most two years.'
	},
	report: {
		invalidMonth: 'The month must be between 1 and 12.'
	},
	export: {
		forbidden: "You may not export this member's data.",
		orgRequired: 'Select an organization for this export.',
		payrollAdminOnly: 'Only admins and accounting can export payroll data.',
		hoursHiddenFromAdmins: "In this organization admins do not see members' working hours (settings). The accounting role does."
	},
	import: {
		disabled: 'CSV import is disabled in this organization.',
		requiresApproval: 'CSV import needs approval. Please submit a request.',
		empty: 'The CSV file needs a header row and at least one data row.',
		missingColumns: 'The CSV file needs the columns Date, Start Time and End Time.',
		tooManyRows: 'An import can hold at most 1000 rows.',
		unknownFormat: 'Unknown import format.',
		formatMismatch: 'The file does not have the columns of the selected format.',
		severalEmployees: 'The file contains data of several people. Import one person per file.'
	},
	request: {
		duplicate: 'An identical request is already pending.',
		alreadyResponded: 'This request has already been answered.',
		notFound: 'Request not found.',
		cannotRespond: 'You may not answer this request.',
		cannotApply: 'The request can no longer be applied.',
		alreadyMember: 'You are already a member of this organization.',
		joinClosed: 'This organization does not accept join requests. Ask an admin for an invitation.',
		featureDisabled: 'This feature is disabled in the organization.',
		notNeeded: 'You can do this directly, without a request.',
		notWithdrawable: 'Only open requests can be withdrawn – this one was already decided or withdrawn.'
	},
	absence: {
		notFound: 'Absence not found.',
		adminOwned: 'This absence was entered or approved by an admin. Ask an admin to remove it.',
		exists: 'An absence already exists on this date.',
		invalidRange: 'Choose a range of at most one year with at least one working day.',
		disabled: 'Adding absences is disabled in this organization. Ask an admin.',
		requiresApproval: 'Absences need approval in this organization. Please submit a request.',
		unknownType: 'Unknown or inactive absence type.'
	},
	appointment: {
		times: 'Enter a start and an end (end after start) or choose “All day”.',
		noteTooLong: 'The note can be at most 200 characters long.',
		date: 'The date is invalid.',
		notFound: 'Appointment not found.',
		forbidden: 'You are not allowed to edit this appointment.'
	},
	absenceType: {
		nameTaken: 'An absence type with this name already exists.',
		notFound: 'Absence type not found.'
	},
	invitation: {
		invalidRole: 'Invite as member or admin; ownership is transferred separately.',
		ownerOnly: 'Only the owner can invite admins.',
		alreadyMember: 'This person is already a member.',
		notFound: 'Invitation not found.',
		invalid: 'This invitation is invalid or has expired.',
		wrongEmail: 'This invitation was sent to a different email address. Sign in with that address to accept it.'
	},
	schedule: {
		notFound: 'Work schedule not found.',
		overlap: 'This period overlaps an existing work schedule. Only one may apply at a time.'
	},
	overtime: {
		initialDisabled: 'Initial overtime is disabled in this organization.',
		initialRequiresApproval: 'Initial overtime needs approval. Please submit a request.'
	},
	holiday: {
		exists: 'A holiday already exists on this date.',
		notFound: 'Holiday not found.'
	},
	pauseRule: {
		exists: 'A pause rule with this threshold already exists.',
		notFound: 'Pause rule not found.'
	},
	group: {
		notFound: 'Group not found.',
		coreHoursTolerance: 'The tolerance must be between 0 and 120 minutes.',
		alreadyMember: 'This person is already in the group.',
		memberNotFound: 'Member not found in the group.',
		parentNotFound: 'Parent group not found.',
		parentOrTopLevel: 'Choose either a parent group or the top level, not both.',
		cycle: 'A group cannot be moved into itself or one of its sub-groups.',
		tooDeep: 'Groups can be nested at most 3 levels deep.',
		hasSubGroups: 'This group has sub-groups. Choose whether to move them up one level or delete them with it.',
		unknownCapability: 'Unknown group admin capability.'
	},
	visibility: {
		unknownKind: 'Unknown kind of information.'
	},
	apiKey: {
		notFound: 'API key not found.'
	},
	terminal: {
		invalidKey: 'The API key is missing or invalid.',
		unknownCard: 'Unknown card.',
		cardBlocked: 'This card is blocked.',
		tooManyUnknown: 'Too many unknown cards were scanned. Try again later.',
		scannedAtOutOfRange: "The scan time lies outside the organization's offline window or more than 2 minutes ahead.",
		doubleScanRange: 'The double-scan window must be between 5 and 600 seconds.',
		offlineRange: 'The offline window must be between 1 and 168 hours.',
		unknownLimitRange: 'The unknown-card budget must be between 5 and 200 scans per 10 minutes.',
		outOfOrder: "This scan is older than the member's last punch.",
		timerElsewhere: 'A timer is running outside this organization. Stop it in the app first.',
		cardTaken: 'This card is already registered.',
		cardRequired: 'Enter a card ID or pick a scanned card.',
		cardNotFound: 'Card not found.',
		scanNotFound: 'Scan not found.'
	},
	period: {
		closed: 'The period {month} is closed. Only organization admins can change it.',
		reasonRequired: 'The period {month} is closed. Give a reason for the change.',
		notEnded: 'Only months that have ended can be closed.',
		tooManyMonths: 'Close at most 24 months at once.',
		notClosed: 'This month is not closed.'
	},
	push: {
		notConfigured: 'Push notifications are not set up on this server.',
		unsupportedService: 'This push service is not supported.',
		invalidSubscription: 'The notification subscription is invalid.',
		otherAccount: 'This device already receives notifications for another account.'
	},
	network: {
		offline: 'No connection. Try again once you are back online.'
	},
	accounting: {
		invalidYear: 'The year must be between 2000 and 2100.',
		ownerOnly: 'Only the owner can change the accounting role of admins or of yourself.'
	}
};
