import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import axios, { AxiosError } from 'axios';

/** Deleting an absence span against a fake backend: one request, one reason prompt for a closed month. */
vi.mock('$app/navigation', () => ({ goto: vi.fn() }));
vi.mock('$env/dynamic/public', () => ({ env: { PUBLIC_API_BASE_URL: 'https://api.test' } }));

const sent: { url: string; ids: number[]; reason?: string }[] = [];
let deleteAbsences: typeof import('./absences').deleteAbsences;

beforeAll(async () => {
	vi.stubGlobal('localStorage', { getItem: () => 'token', setItem: () => {}, removeItem: () => {} });
	vi.stubGlobal('window', { location: { origin: 'https://app.test', pathname: '/', search: '', href: '' } });
	axios.defaults.adapter = async (config) => {
		const reason = config.headers?.['X-Change-Reason'] as string | undefined;
		sent.push({ url: config.url ?? '', ids: JSON.parse(config.data as string).ids, reason });
		const [status, data] = reason ? [204, {}] : [409, { code: 'period.reasonRequired', month: '2026-09' }];
		const response = { status, statusText: String(status), data, headers: {}, config };
		if (status >= 400) throw new AxiosError(String(status), 'ERR_BAD_RESPONSE', config, null, response);
		return response;
	};
	({ deleteAbsences } = await import('./absences'));
});

afterAll(() => {
	vi.unstubAllGlobals();
	axios.defaults.adapter = undefined;
});

describe('deleteAbsences', () => {
	it('sends the whole span at once and asks for the closed-month reason once', async () => {
		const { confirmState } = await import('$lib/stores/confirm.svelte');
		const done = deleteAbsences('acme', [4, 5, 6]);
		await vi.waitFor(() => expect(confirmState.current?.input).toBeTruthy());
		confirmState.answer(true, 'Urlaub storniert');
		await done;
		expect(sent).toEqual([
			{ url: 'https://api.test/api/v1/organizations/acme/absences/delete', ids: [4, 5, 6], reason: undefined },
			{ url: 'https://api.test/api/v1/organizations/acme/absences/delete', ids: [4, 5, 6], reason: encodeURIComponent('Urlaub storniert') }
		]);

		// Cancelled: nothing more is sent, the span stays whole
		sent.length = 0;
		const cancelled = deleteAbsences('acme', [4, 5, 6]);
		await vi.waitFor(() => expect(confirmState.current).toBeTruthy());
		confirmState.answer(false);
		await expect(cancelled).rejects.toMatchObject({ response: { status: 409 } });
		expect(sent).toHaveLength(1);
	});
});
