/**
 * Shared error message extraction for API catch blocks.
 *
 * Replaces all `catch (err: any) { error = err.response?.data?.message || '...'; }` patterns
 * with a type-safe `catch (err) { error = extractErrorMessage(err, 'Failed to ...'); }`.
 */

/**
 * Extract a human-readable error message from an unknown error object.
 *
 * Checks (in order):
 * 1. Axios-style `err.response.data.message` (string from backend ProblemDetails/JSON)
 * 2. Axios-style `err.response.data` if it's a plain string (some endpoints return raw text)
 * 3. Native `Error.message`
 * 4. The provided fallback string
 *
 * @example
 *   catch (err) { error = extractErrorMessage(err, 'Failed to start timer.'); }
 */
export function extractErrorMessage(err: unknown, fallback = 'An unexpected error occurred.'): string {
	if (err && typeof err === 'object' && 'response' in err) {
		const res = (err as { response?: { status?: number; data?: Record<string, unknown> | string } }).response;
		if (res?.data) {
			if (typeof res.data === 'object') {
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
			return `${fallback} (HTTP ${res.status})`;
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
