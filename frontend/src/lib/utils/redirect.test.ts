import { describe, expect, it } from 'vitest';
import { safeNext, withNext } from './redirect';

describe('safeNext', () => {
	it('keeps same-origin paths with their query', () => {
		expect(safeNext('/invite?token=abc')).toBe('/invite?token=abc');
	});

	it('rejects other origins and auth pages', () => {
		for (const v of ['https://evil.example', '//evil.example', '/\\evil.example', '/login?next=/x', '/register', '', null]) {
			expect(safeNext(v)).toBeNull();
		}
	});

	it('withNext encodes the target', () => {
		expect(withNext('/register', '/invite?token=a&b')).toBe('/register?next=%2Finvite%3Ftoken%3Da%26b');
		expect(withNext('/register', null)).toBe('/register');
	});
});
