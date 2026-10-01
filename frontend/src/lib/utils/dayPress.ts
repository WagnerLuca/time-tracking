/**
 * Calendar day interaction: a plain click (or Enter/Space on the button) selects
 * the day, a right click / long press on touch opens the day's dialog. The
 * keyboard's ContextMenu key and Shift+F10 fire a native `contextmenu` event on
 * the focused element, so they open the dialog through the same handler.
 */
export interface DayPressOptions {
	onselect: () => void;
	/** Omitted → right click keeps the browser's own menu. */
	onopen?: () => void;
	/** Long-press duration on touch (ms). */
	delay?: number;
}

/** Finger movement (px) after which a press counts as a scroll, not a long press. */
const MOVE_TOLERANCE = 10;

interface PointerLike {
	pointerType: string;
	clientX: number;
	clientY: number;
}

/** DOM-free core of the `dayPress` action, so the dispatch rules are unit-testable. */
export function createDayPress(getOptions: () => DayPressOptions) {
	let timer: ReturnType<typeof setTimeout> | undefined;
	let start = { x: 0, y: 0 };
	/** The current touch already opened the dialog: swallow its trailing click/contextmenu. */
	let longPressed = false;

	const cancel = () => {
		clearTimeout(timer);
		timer = undefined;
	};

	return {
		pointerdown(e: PointerLike) {
			longPressed = false;
			cancel();
			if (e.pointerType !== 'touch' || !getOptions().onopen) return;
			start = { x: e.clientX, y: e.clientY };
			timer = setTimeout(() => {
				timer = undefined;
				longPressed = true;
				getOptions().onopen?.();
			}, getOptions().delay ?? 500);
		},
		pointermove(e: PointerLike) {
			if (timer && Math.hypot(e.clientX - start.x, e.clientY - start.y) > MOVE_TOLERANCE) cancel();
		},
		/** pointerup / pointercancel (the browser took over for scrolling). */
		pointerend: cancel,
		/** Returns true when the event was consumed (caller prevents the default). */
		click(): boolean {
			if (longPressed) {
				longPressed = false;
				return true;
			}
			getOptions().onselect();
			return false;
		},
		/** Returns true when the event was consumed (caller prevents the default). */
		contextmenu(): boolean {
			const { onopen } = getOptions();
			if (!onopen) return false;
			// Android fires contextmenu on a long press too — open only once
			if (longPressed) return true;
			if (timer) {
				cancel();
				longPressed = true;
			}
			onopen();
			return true;
		}
	};
}

/** Svelte action: `<button use:dayPress={{ onselect, onopen }}>`. */
export function dayPress(node: HTMLElement, options: DayPressOptions) {
	let current = options;
	const press = createDayPress(() => current);
	const listeners: [string, (e: Event) => void][] = [
		['pointerdown', (e) => press.pointerdown(e as PointerEvent)],
		['pointermove', (e) => press.pointermove(e as PointerEvent)],
		['pointerup', press.pointerend],
		['pointercancel', press.pointerend],
		['click', (e) => { if (press.click()) e.preventDefault(); }],
		['contextmenu', (e) => { if (press.contextmenu()) e.preventDefault(); }]
	];
	for (const [type, fn] of listeners) node.addEventListener(type, fn);
	// No iOS callout / text selection while holding a day
	node.style.setProperty('-webkit-touch-callout', 'none');
	node.style.userSelect = 'none';
	return {
		update(next: DayPressOptions) {
			current = next;
		},
		destroy() {
			press.pointerend();
			for (const [type, fn] of listeners) node.removeEventListener(type, fn);
		}
	};
}
