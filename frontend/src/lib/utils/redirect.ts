/**
 * A `?next=` value that is safe to navigate to after login/registration: a same-origin
 * path only (no `//host`, no `/\host`), and never back to /login or /register.
 */
export function safeNext(value: string | null | undefined): string | null {
	if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) return null;
	if (value.startsWith('/login') || value.startsWith('/register')) return null;
	return value;
}

/** `path` with `?next=` appended when there is one, so login ↔ register keep the target. */
export function withNext(path: string, next: string | null): string {
	return next ? `${path}?next=${encodeURIComponent(next)}` : path;
}
