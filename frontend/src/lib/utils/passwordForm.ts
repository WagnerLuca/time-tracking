/** The backend's minimum (RegisterRequest / ResetPasswordRequest). */
export const MIN_PASSWORD_LENGTH = 8;

/** Validation of a "new password + repeat" pair; null when it can be submitted. */
export function newPasswordProblem(password: string, confirm: string): 'tooShort' | 'mismatch' | null {
	if (password.length < MIN_PASSWORD_LENGTH) return 'tooShort';
	if (password !== confirm) return 'mismatch';
	return null;
}
