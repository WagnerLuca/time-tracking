import { afterEach, describe, expect, it } from 'vitest';
import { locale } from '$lib/stores/locale.svelte';
import { extractErrorMessage, getErrorCode, getErrorStatus, isTwoFactorRequired, translateErrorCode } from './errorHandler';

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
		expect(getErrorCode(problem({ code: '' }))).toBeNull();
		expect(getErrorCode(problem({ code: 42 }))).toBeNull();
		expect(getErrorCode(problem('plain text'))).toBeNull();
		expect(getErrorCode(new Error('boom'))).toBeNull();
		expect(getErrorCode(null)).toBeNull();
	});

	it('a known code beats the backend message', () => {
		expect(extractErrorMessage(problem({ code: 'auth.sessionEnded', message: 'raw english' }), 'x')).toBe(
			'Deine Sitzung ist beendet. Bitte melde dich erneut an.'
		);
	});

	it('body precedence: message → detail → validation errors → title', () => {
		expect(extractErrorMessage(problem({ message: 'm', detail: 'd', title: 't' }), 'x')).toBe('m');
		expect(extractErrorMessage(problem({ message: 7, detail: 'd', title: 't' }), 'x')).toBe('d');
		expect(
			extractErrorMessage(problem({ title: 'One or more validation errors occurred.', errors: { Name: ['Name is required.'], Email: ['Bad email.', 'Too long.'] } }), 'x')
		).toBe('Name is required. Bad email. Too long.');
		expect(extractErrorMessage(problem({ title: 'Conflict', errors: {} }), 'x')).toBe('Conflict');
		expect(extractErrorMessage(problem({ title: 'Conflict', errors: null }), 'x')).toBe('Conflict');
	});

	it('a plain-text body is used as is', () => {
		expect(extractErrorMessage(problem('Entry overlaps another entry'), 'x')).toBe('Entry overlaps another entry');
	});

	it('an unparseable body or empty response appends the HTTP status to the fallback', () => {
		expect(extractErrorMessage(problem({ unrelated: true }, 409), 'Speichern fehlgeschlagen')).toBe('Speichern fehlgeschlagen (HTTP 409)');
		expect(extractErrorMessage(problem('', 502), 'F')).toBe('F (HTTP 502)');
		expect(extractErrorMessage(problem(undefined, 404), 'F')).toBe('F (HTTP 404)');
	});

	it('the 2FA-required 403 gets its own explanation; other 403s do not', () => {
		const twoFa = problem({ title: 'TwoFactorRequired' }, 403);
		expect(isTwoFactorRequired(twoFa)).toBe(true);
		expect(extractErrorMessage(twoFa, 'x')).toMatch(/^Diese Organisation verlangt die Zwei-Faktor/);
		expect(isTwoFactorRequired(problem({ title: 'TwoFactorRequired' }, 401))).toBe(false);
		expect(isTwoFactorRequired(problem({ title: 'Forbidden' }, 403))).toBe(false);
		expect(isTwoFactorRequired(null)).toBe(false);
		expect(extractErrorMessage(problem({ title: 'Forbidden' }, 403), 'x')).toBe('Forbidden');
	});

	it('network errors (axios error without response) use the error message', () => {
		const network = Object.assign(new Error('Network Error'), { response: undefined, code: 'ERR_NETWORK' });
		expect(extractErrorMessage(network, 'x')).toBe('Network Error');
		expect(extractErrorMessage(new Error('boom'), 'x')).toBe('boom');
	});

	it('non-errors use the fallback, defaulting to the translated generic message', () => {
		expect(extractErrorMessage('a string', 'F')).toBe('F');
		expect(extractErrorMessage(undefined)).toBe('Ein unerwarteter Fehler ist aufgetreten.');
		expect(extractErrorMessage({ response: {} })).toBe('Ein unerwarteter Fehler ist aufgetreten.');
		locale.set('en');
		expect(extractErrorMessage(42)).toBe('An unexpected error occurred.');
	});
});

describe('getErrorStatus', () => {
	it('returns the numeric status or null', () => {
		expect(getErrorStatus(problem({}, 404))).toBe(404);
		expect(getErrorStatus({ response: { status: '404' } })).toBeNull();
		expect(getErrorStatus({ response: undefined })).toBeNull();
		expect(getErrorStatus(new Error('x'))).toBeNull();
		expect(getErrorStatus(null)).toBeNull();
	});
});
