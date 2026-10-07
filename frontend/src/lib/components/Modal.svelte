<script lang="ts">
	import type { Snippet } from 'svelte';
	import { t } from '$lib/i18n';

	/**
	 * Accessible modal on the native <dialog>: showModal() gives focus trapping,
	 * inert background and Esc-to-close for free; the title labels the dialog.
	 * Content is only rendered while open, so forms start fresh every time.
	 */
	interface Props {
		open: boolean;
		onclose: () => void;
		/** Heading, also used as the dialog's accessible name. */
		title?: string;
		children: Snippet;
		/** Optional footer (buttons), rendered in the modal-action row. */
		actions?: Snippet;
		maxWidth?: string;
	}
	let { open, onclose, title, children, actions, maxWidth = '28rem' }: Props = $props();

	let dialog = $state<HTMLDialogElement>();
	const titleId = $props.id();

	$effect(() => {
		if (!dialog) return;
		if (open && !dialog.open) dialog.showModal();
		else if (!open && dialog.open) dialog.close();
	});

	// Esc and backdrop clicks close the <dialog> natively; tell the parent so its state follows
	function handleClose() {
		if (open) onclose();
	}
</script>

<dialog bind:this={dialog} class="modal modal-bottom sm:modal-middle" aria-modal="true" aria-labelledby={title ? titleId : undefined} onclose={handleClose}>
	<div class="modal-box max-h-[90vh]" style="max-width: {maxWidth}">
		{#if open}
			{#if title}
				<div class="flex items-start justify-between gap-3 mb-4">
					<h3 id={titleId} class="text-lg font-bold m-0">{title}</h3>
					<button type="button" class="btn btn-ghost btn-sm btn-circle -mt-1 -mr-2" aria-label={t('common.close')} onclick={onclose}>✕</button>
				</div>
			{/if}
			{@render children()}
			{#if actions}
				<div class="modal-action">{@render actions()}</div>
			{/if}
		{/if}
	</div>
	<form method="dialog" class="modal-backdrop">
		<button tabindex="-1" aria-label={t('common.close')}></button>
	</form>
</dialog>
