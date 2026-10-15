/**
 * Promise-based replacement for window.confirm():
 *
 *   if (!(await confirmDialog({ title: t('x.deleteTitle'), message: …, confirmLabel: t('common.delete'), danger: true }))) return;
 *
 * and for window.prompt() when a short text is needed (e.g. a reason):
 *
 *   const reason = await promptDialog({ title: …, input: { label: t('…') } });   // null when cancelled
 *
 * The single <ConfirmDialog /> host in the root layout renders whatever is pending.
 */
export interface ConfirmOptions {
	title: string;
	message?: string;
	/** Defaults to "Bestätigen" / "Confirm". */
	confirmLabel?: string;
	cancelLabel?: string;
	/** Style the confirm button as destructive. */
	danger?: boolean;
	/** Ask for a text; confirming needs a non-blank one. */
	input?: { label: string; maxLength?: number };
}

interface PendingConfirm extends ConfirmOptions {
	resolve: (answer: string | null) => void;
}

let pending = $state<PendingConfirm | null>(null);

function ask(options: ConfirmOptions): Promise<string | null> {
	pending?.resolve(null);
	return new Promise((resolve) => {
		pending = { ...options, resolve };
	});
}

export async function confirmDialog(options: ConfirmOptions): Promise<boolean> {
	return (await ask({ ...options, input: undefined })) !== null;
}

/** The trimmed text, or null when cancelled. */
export function promptDialog(options: ConfirmOptions & { input: { label: string; maxLength?: number } }): Promise<string | null> {
	return ask(options);
}

export const confirmState = {
	get current() { return pending; },
	/** Confirm (with the typed text for a prompt) or cancel. */
	answer(confirmed: boolean, text = '') {
		const p = pending;
		pending = null;
		p?.resolve(confirmed ? text.trim() : null);
	}
};
