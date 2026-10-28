<script lang="ts">
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';

	const alertClass: Record<string, string> = {
		success: 'alert-success',
		error: 'alert-error',
		info: 'alert-info',
		warning: 'alert-warning'
	};

	// Toasts sit at the top on phones (clear of the bottom bar) and bottom-right from 40rem:
	// they enter, leave and are swiped away towards that same edge.
	const edge = () => (matchMedia('(width >= 40rem)').matches ? 1 : -1);
	const reducedMotion = () => matchMedia('(prefers-reduced-motion: reduce)').matches;

	let drag: { id: number; el: HTMLElement; y: number; at: number; dir: number; moved: number } | null = null;

	function onpointerdown(e: PointerEvent, id: number) {
		// One finger only; the ✕ button keeps its click; mice click, they don't swipe
		if (drag || e.pointerType === 'mouse' || (e.target as Element).closest('button')) return;
		const el = e.currentTarget as HTMLElement;
		el.setPointerCapture(e.pointerId);
		drag = { id, el, y: e.clientY, at: performance.now(), dir: edge(), moved: 0 };
		toast.pause('drag');
	}

	function onpointermove(e: PointerEvent) {
		if (!drag) return;
		const raw = e.clientY - drag.y;
		// Towards the edge 1:1, the other way with friction instead of a hard stop
		drag.moved = Math.sign(raw) === drag.dir ? raw : raw * 0.15;
		drag.el.style.transition = 'none';
		drag.el.style.translate = `0 ${drag.moved}px`;
	}

	function onpointerup() {
		if (!drag) return;
		const { id, el, at, dir, moved } = drag;
		drag = null;
		toast.resume('drag');
		el.style.transition = '';
		const towardsEdge = Math.sign(moved) === dir ? Math.abs(moved) : 0;
		// A quick flick is enough: distance or velocity (px/ms)
		if (towardsEdge > 40 || towardsEdge / (performance.now() - at) > 0.11) {
			el.dataset.swiped = '';
			el.style.translate = `0 ${dir * 150}%`;
			el.style.opacity = '0';
			setTimeout(() => toast.dismiss(id), 150);
		} else {
			el.style.translate = '';
		}
	}

	/** Leaves towards its edge — unless a swipe already took it there. */
	function leave(node: HTMLElement) {
		if ('swiped' in node.dataset) return { duration: 0 };
		if (reducedMotion()) return { duration: 100, css: (k: number) => `opacity: ${k}` };
		const dir = edge();
		return { duration: 150, css: (k: number) => `opacity: ${k}; translate: 0 ${(1 - k) * dir * 60}%; scale: ${0.96 + k * 0.04}` };
	}
</script>

<div
	class="toast"
	role="status"
	aria-live="polite"
	onpointerenter={(e) => e.pointerType === 'mouse' && toast.pause('hover')}
	onpointerleave={(e) => e.pointerType === 'mouse' && toast.resume('hover')}
	onfocusin={() => toast.pause('focus')}
	onfocusout={() => toast.resume('focus')}
>
	{#each toast.items as item (item.id)}
		<div
			class="alert {alertClass[item.kind]} shadow-lg text-sm py-2 pr-2 whitespace-normal"
			role={item.kind === 'error' ? 'alert' : undefined}
			onpointerdown={(e) => onpointerdown(e, item.id)}
			{onpointermove}
			{onpointerup}
			onpointercancel={onpointerup}
			out:leave
		>
			<span class="flex-1">{item.message}</span>
			<button type="button" class="btn btn-ghost btn-xs btn-circle" aria-label={t('common.dismiss')} onclick={() => toast.dismiss(item.id)}>✕</button>
		</div>
	{/each}
</div>
