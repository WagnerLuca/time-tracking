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

	/* Phones (below 40rem, where it is a bottom sheet): drag the handle/title down to close.
	   The finger is followed 1:1, upwards it rubber-bands, and on release the flick's
	   velocity is projected (Apple's decay) to decide, then handed into the slide out.
	   Only the handle row drags, so the content keeps scrolling natively. */
	let box = $state<HTMLDivElement>();
	let drag: { id: number; startY: number; y: number; t: number; v: number } | null = null;
	const isSheet = () => !matchMedia('(width >= 40rem)').matches;
	const rubberband = (over: number, size: number) => (over * size * 0.55) / (size + 0.55 * Math.abs(over));

	function dragStart(e: PointerEvent) {
		if (drag || !box || !isSheet() || (e.target as Element).closest('button')) return;
		(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
		drag = { id: e.pointerId, startY: e.clientY, y: e.clientY, t: e.timeStamp, v: 0 };
		box.style.transition = 'none';
	}

	function dragMove(e: PointerEvent) {
		if (!drag || e.pointerId !== drag.id || !box) return;
		const dt = e.timeStamp - drag.t;
		if (dt > 0) drag.v = (e.clientY - drag.y) / dt; // px/ms, from the latest samples
		drag.y = e.clientY;
		drag.t = e.timeStamp;
		const dy = e.clientY - drag.startY;
		box.style.translate = `0 ${dy >= 0 ? dy : -rubberband(-dy, box.offsetHeight)}px`;
	}

	function dragEnd(e: PointerEvent) {
		if (!drag || e.pointerId !== drag.id || !box) return;
		const dy = Math.max(0, drag.y - drag.startY);
		const v = e.type === 'pointercancel' || e.timeStamp - drag.t > 100 ? 0 : drag.v; // a pause before lifting is no flick
		drag = null;
		const h = box.offsetHeight;
		const projected = dy + (v * 0.998) / (1 - 0.998); // where the flick would come to rest
		if (projected > h / 2) {
			const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
			// Keep the finger's speed: the rest of the way takes as long as that speed needs (100–300 ms)
			const ms = reduce ? 0 : Math.round(Math.min(300, Math.max(100, v > 0 ? (h - dy) / v : 300)));
			box.style.transition = `translate ${ms}ms var(--ease-out)`;
			box.style.translate = '0 100%';
			setTimeout(onclose, ms);
		} else {
			box.style.transition = 'translate 300ms var(--ease-drawer)';
			box.style.translate = '';
		}
	}

	// Closed (by any route): drop the drag's inline styles so the next opening starts clean
	$effect(() => {
		if (!open && box) {
			box.style.translate = '';
			box.style.transition = '';
		}
	});
</script>

<dialog bind:this={dialog} class="modal modal-bottom sm:modal-middle" aria-modal="true" aria-labelledby={title ? titleId : undefined} onclose={handleClose}>
	<div bind:this={box} class="modal-box max-h-[90dvh]" style="max-width: {maxWidth}">
		{#if open}
			<div class="sheet-drag" role="presentation" onpointerdown={dragStart} onpointermove={dragMove} onpointerup={dragEnd} onpointercancel={dragEnd}>
				<div class="sheet-handle" aria-hidden="true"></div>
				{#if title}
					<div class="flex items-start justify-between gap-3 mb-4">
						<h3 id={titleId} class="text-lg font-bold m-0">{title}</h3>
						<button type="button" class="btn btn-ghost btn-sm btn-circle -mt-1 -mr-2" aria-label={t('common.close')} onclick={onclose}>✕</button>
					</div>
				{/if}
			</div>
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
