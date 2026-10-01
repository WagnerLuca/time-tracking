import { afterEach, describe, expect, it } from 'vitest';
import { locale } from '$lib/stores/locale.svelte';
import { extractErrorMessage, getErrorCode, translateErrorCode } from './errorHandler';

const problem = (data: unknown, status = 400) => ({ response: { status, data } });

describe('extractErrorMessage', () => {
	afterEach(() => locale.set('de'));

	it('translates a known code in the current locale', () => {
		const err = problem({ code: 'auth.sessionEnded', detail: 'Your session has ended. Please sign in again.' }, 401);
		expect(extractErrorMessage(err, 'x')).toBe('Deine Sitzung ist beendet. Bitte melde dich erneut an.');
		locale.set('en');
		expect(extractErrorMessage(err, 'x')).toBe('Your session has ended. Please sign in again.');
	});

	it('falls back to detail for an unknown code, then to the fallback', () => {
		expect(extractErrorMessage(problem({ code: 'brand.new', detail: 'Server says no' }), 'x')).toBe('Server says no');
		expect(extractErrorMessage(problem({ code: 'brand.new' }, 500), 'Fallback')).toBe('Fallback (HTTP 500)');
		expect(extractErrorMessage(null, 'Fallback')).toBe('Fallback');
	});

	it('does not treat a namespace as a translation', () => {
		expect(translateErrorCode('auth')).toBeNull();
		expect(translateErrorCode(undefined)).toBeNull();
	});

	it('reads the code off an axios error', () => {
		expect(getErrorCode(problem({ code: 'invitation.wrongEmail' }))).toBe('invitation.wrongEmail');
		expect(getErrorCode(problem('plain text'))).toBeNull();
		expect(getErrorCode(new Error('boom'))).toBeNull();
	});
});
