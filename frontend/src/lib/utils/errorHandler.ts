/**
 * Shared error message extraction for API catch blocks.
 *
 * Replaces all `catch (err: any) { error = err.response?.data?.message || '...'; }` patterns
 * with a type-safe `catch (err) { error = extractErrorMessage(err, 'Failed to ...'); }`.
 */
import { t, type MessageKey } from '$lib/i18n';
import { de } from '$lib/i18n/de';
import { lookup } from '$lib/i18n/translate';
import { formatMonth } from './closedPeriods';

/** The stable `code` of a backend ProblemDetails (e.g. "auth.sessionEnded"), or null. */
export function getErrorCode(err: unknown): string | null {
	const code = (err as { response?: { data?: { code?: unknown } } } | null)?.response?.data?.code;
	return typeof code === 'string' && code ? code : null;
}

/** The translated text for a backend error code, or null when the code is unknown. */
export function translateErrorCode(code: string | null | undefined, month?: unknown): string | null {
	if (!code) return null;
	const key = `errors.${code}`;
	// Closed periods name their month ('YYYY-MM', ProblemDetails extension `month`)
	const params = typeof month === 'string' && /^\d{4}-\d{2}$/.test(month) ? { month: formatMonth(month) } : undefined;
	return typeof lookup(de, key) === 'string' ? t(key as MessageKey, params) : null;
}

/** The closed month ('YYYY-MM') a refused write named, or null. */
export function getErrorMonth(err: unknown): string | null {
	const month = (err as { response?: { data?: { month?: unknown } } } | null)?.response?.data?.month;
	return typeof month === 'string' ? month : null;
}

/**
 * Extract a human-readable error message from an unknown error object.
 *
 * Checks (in order):
 * 0. A known backend error `code` (ProblemDetails extension), translated via `messages/errors.ts`
 * 1. Axios-style `err.response.data.message` (string from backend ProblemDetails/JSON)
 * 2. Axios-style `err.response.data` if it's a plain string (some endpoints return raw text)
 * 3. Native `Error.message`
 * 4. The provided fallback string
 *
 * @example
 *   catch (err) { error = extractErrorMessage(err, 'Failed to start timer.'); }
 */
export function extractErrorMessage(err: unknown, fallback: string = t('common.unexpectedError')): string {
	if (err && typeof err === 'object' && 'response' in err) {
		const res = (err as { response?: { status?: number; data?: Record<string, unknown> | string } }).response;
		if (res?.data) {
			if (typeof res.data === 'object') {
				// Org requires 2FA the caller has not set up (RequireOrgRole filter)
				if (isTwoFactorRequired(err)) return t('common.twoFactorRequired');
				const translated = translateErrorCode(getErrorCode(err), getErrorMonth(err));
				if (translated) return translated;
				// Backend ServiceResult / custom JSON: { message: "..." }
				if ('message' in res.data && typeof res.data.message === 'string') {
					return res.data.message;
				}
				// ASP.NET Core ProblemDetails: { title, detail, errors }
				if ('detail' in res.data && typeof res.data.detail === 'string') {
					return res.data.detail;
				}
				// Model validation errors: { errors: { Field: ["msg1", ...] } }
				if ('errors' in res.data && typeof res.data.errors === 'object' && res.data.errors) {
					const errors = res.data.errors as Record<string, string[]>;
					const messages = Object.entries(errors)
						.flatMap(([, msgs]) => msgs);
					if (messages.length > 0) return messages.join(' ');
				}
				if ('title' in res.data && typeof res.data.title === 'string') {
					return res.data.title;
				}
			}
			if (typeof res.data === 'string' && res.data.length > 0) {
				return res.data;
			}
		}
		// If we have a status but no parseable body, include the status code
		if (res?.status) {
			return t('common.httpStatus', { message: fallback, status: res.status });
		}
	}
	if (err instanceof Error) return err.message;
	return fallback;
}

/**
 * Get the HTTP status code from an Axios error, or null if not available.
 *
 * @example
 *   catch (err) {
 *     if (getErrorStatus(err) === 404) { error = 'Not found.'; }
 *     else { error = extractErrorMessage(err, 'Failed to load.'); }
 *   }
 */
export function getErrorStatus(err: unknown): number | null {
	if (err && typeof err === 'object' && 'response' in err) {
		const res = (err as { response?: { status?: number } }).response;
		if (typeof res?.status === 'number') return res.status;
	}
	return null;
}

/** 403 from an org that requires 2FA while the caller has none (ProblemDetails title "TwoFactorRequired"). */
export function isTwoFactorRequired(err: unknown): boolean {
	const res = (err as { response?: { status?: number; data?: { title?: unknown } } } | null)?.response;
	return res?.status === 403 && res.data?.title === 'TwoFactorRequired';
}
