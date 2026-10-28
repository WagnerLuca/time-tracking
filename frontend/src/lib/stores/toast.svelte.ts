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

/** More than this and the oldest goes: a stack is read at a glance or not at all. */
export const MAX_TOASTS = 3;

let items = $state<ToastItem[]>([]);
let nextId = 1;

// Auto-dismiss timers that can be paused: while the pointer or focus is on the
// stack (someone is reading) and while the tab is hidden (nobody is).
const timers = new Map<number, { remaining: number; startedAt: number; handle?: ReturnType<typeof setTimeout> }>();
let pausedBy = new Set<string>();

function arm(id: number) {
	const timer = timers.get(id);
	if (!timer || pausedBy.size) return;
	timer.startedAt = Date.now();
	timer.handle = setTimeout(() => dismiss(id), timer.remaining);
}

function show(kind: ToastKind, message: string, timeoutMs = kind === 'error' ? 7000 : 4000): number {
	const id = nextId++;
	items = [...items, { id, kind, message }];
	if (timeoutMs > 0) {
		timers.set(id, { remaining: timeoutMs, startedAt: 0 });
		arm(id);
	}
	while (items.length > MAX_TOASTS) dismiss(items[0].id);
	return id;
}

function dismiss(id: number) {
	clearTimeout(timers.get(id)?.handle);
	timers.delete(id);
	items = items.filter((i) => i.id !== id);
}

/** Stops every timer until each `reason` that paused it has resumed. */
function pause(reason: string) {
	if (!pausedBy.size) {
		for (const timer of timers.values()) {
			clearTimeout(timer.handle);
			timer.remaining = Math.max(0, timer.remaining - (Date.now() - timer.startedAt));
		}
	}
	pausedBy = new Set(pausedBy).add(reason);
}

function resume(reason: string) {
	if (!pausedBy.has(reason)) return;
	pausedBy.delete(reason);
	if (!pausedBy.size) for (const id of timers.keys()) arm(id);
}

if (typeof document !== 'undefined') {
	document.addEventListener('visibilitychange', () => (document.hidden ? pause('hidden') : resume('hidden')));
}

export const toast = {
	get items() { return items; },
	show,
	dismiss,
	pause,
	resume,
	success: (message: string) => show('success', message),
	info: (message: string) => show('info', message),
	warning: (message: string) => show('warning', message),
	/** Accepts a caught error (message extracted from the API response) or a plain string. */
	error: (errOrMessage: unknown, fallback?: string) =>
		// A read refused offline needs no toast: the offline banner says it all
		(errOrMessage as { quiet?: boolean } | null)?.quiet
			? 0
			: show('error', typeof errOrMessage === 'string' ? errOrMessage : extractErrorMessage(errOrMessage, fallback))
};
