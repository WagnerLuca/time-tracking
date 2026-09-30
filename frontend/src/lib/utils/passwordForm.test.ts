import { describe, expect, it } from 'vitest';
import { MIN_PASSWORD_LENGTH, newPasswordProblem } from './passwordForm';

describe('newPasswordProblem', () => {
	it('requires the minimum length first', () => {
		expect(newPasswordProblem('short', 'short')).toBe('tooShort');
		expect(newPasswordProblem('x'.repeat(MIN_PASSWORD_LENGTH - 1), '')).toBe('tooShort');
	});

	it('requires both fields to match', () => {
		expect(newPasswordProblem('Password123', 'Password124')).toBe('mismatch');
	});

	it('accepts a long enough, repeated password', () => {
		expect(newPasswordProblem('x'.repeat(MIN_PASSWORD_LENGTH), 'x'.repeat(MIN_PASSWORD_LENGTH))).toBeNull();
	});
});
