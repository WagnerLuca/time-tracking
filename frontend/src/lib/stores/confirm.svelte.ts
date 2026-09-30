/**
 * Promise-based replacement for window.confirm():
 *
 *   if (!(await confirmDialog({ title: t('x.deleteTitle'), message: …, confirmLabel: t('common.delete'), danger: true }))) return;
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
}

interface PendingConfirm extends ConfirmOptions {
	resolve: (confirmed: boolean) => void;
}

let pending = $state<PendingConfirm | null>(null);

export function confirmDialog(options: ConfirmOptions): Promise<boolean> {
	pending?.resolve(false);
	return new Promise((resolve) => {
		pending = { ...options, resolve };
	});
}

export const confirmState = {
	get current() { return pending; },
	answer(confirmed: boolean) {
		const p = pending;
		pending = null;
		p?.resolve(confirmed);
	}
};
