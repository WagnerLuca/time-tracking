import type { de } from './de';

export const en: typeof de = {
	email: 'Email',
	emailPlaceholder: 'you@example.com',
	password: 'Password',
	inviteHint: 'You were invited to an organization. Sign in with the invited email address or create an account with it – the invitation is accepted afterwards.',
	login: {
		pageTitle: 'Sign in',
		title: 'Sign in',
		subtitle: 'Welcome back to Time Tracking',
		submit: 'Sign in',
		submitting: 'Signing in…',
		failed: 'Sign-in failed. Please check your email and password.',
		noAccount: "Don't have an account?",
		register: 'Create one',
		forgot: 'Forgot password?'
	},
	twoFactor: {
		title: 'Two-factor authentication',
		subtitle: 'Enter the 6-digit code from your authenticator app',
		code: 'Verification code',
		backupHint: 'You can also use a backup code.',
		verify: 'Verify',
		verifying: 'Verifying…',
		failed: 'Invalid verification code. Please try again.',
		back: 'Back to sign in'
	},
	forgot: {
		pageTitle: 'Forgot password',
		title: 'Forgot your password?',
		subtitle: "Enter your email address and we'll send you a reset link.",
		submit: 'Send link',
		submitting: 'Sending…',
		sent: 'If an account exists for this address, we have sent you a reset link. Please check your inbox.',
		failed: 'The request could not be sent. Please try again later.',
		back: 'Back to sign in'
	},
	reset: {
		pageTitle: 'New password',
		title: 'Set a new password',
		subtitle: 'Choose a new password for your account.',
		newPassword: 'New password',
		submit: 'Save password',
		submitting: 'Saving…',
		done: 'Your password has been changed. You can sign in now.',
		failed: 'The password could not be reset. The link may have expired.',
		missingToken: 'This link is incomplete. Please request a new one.',
		requestNew: 'Request a new link',
		toLogin: 'Go to sign in'
	},
	verify: {
		pageTitle: 'Confirm email',
		verifying: 'Confirming your email address…',
		success: 'Your email address is confirmed.',
		failed: 'The email address could not be confirmed. The link may have expired or was already used.',
		missingToken: 'This confirmation link is incomplete.',
		toApp: 'Go to the app',
		toLogin: 'Go to sign in'
	},
	register: {
		pageTitle: 'Register',
		title: 'Create account',
		subtitle: 'Get started with Time Tracking',
		firstName: 'First name',
		firstNamePlaceholder: 'Jane',
		lastName: 'Last name',
		lastNamePlaceholder: 'Doe',
		passwordHint: 'At least {min} characters, with upper- and lowercase letters, a digit and a special character.',
		confirmPassword: 'Confirm password',
		mismatch: 'Passwords do not match.',
		submit: 'Create account',
		submitting: 'Creating account…',
		failed: 'Registration failed. Please try again.',
		haveAccount: 'Already have an account?',
		login: 'Sign in',
		verifySent: 'Account created. We have sent you an email – please confirm your address using the link in it.'
	}
};
