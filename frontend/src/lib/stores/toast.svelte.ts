import { extractErrorMessage } from '$lib/utils/errorHandler';

/**
 * Tiny toast queue for success / error feedback, rendered by <Toaster /> in the
 * root layout. Use it instead of swallowing errors or console.error-only:
 *
 *   toast.success(t('common.saved'));
 *   catch (err) { toast.error(err, t('x.saveFailed')); }
 */
export type ToastKind = 'success' | 'error' | 'info' | 'warning';

export interface ToastItem {
	id: number;
	kind: ToastKind;
	message: string;
}

let items = $state<ToastItem[]>([]);
let nextId = 1;

function show(kind: ToastKind, message: string, timeoutMs = kind === 'error' ? 7000 : 4000): number {
	const id = nextId++;
	items = [...items, { id, kind, message }];
	if (timeoutMs > 0) setTimeout(() => dismiss(id), timeoutMs);
	return id;
}

function dismiss(id: number) {
	items = items.filter((i) => i.id !== id);
}

export const toast = {
	get items() { return items; },
	show,
	dismiss,
	success: (message: string) => show('success', message),
	info: (message: string) => show('info', message),
	warning: (message: string) => show('warning', message),
	/** Accepts a caught error (message extracted from the API response) or a plain string. */
	error: (errOrMessage: unknown, fallback?: string) =>
		show('error', typeof errOrMessage === 'string' ? errOrMessage : extractErrorMessage(errOrMessage, fallback))
};
