import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createDayPress, dayPress } from './dayPress';

const touch = (x = 0, y = 0) => ({ pointerType: 'touch', clientX: x, clientY: y });
const mouse = { pointerType: 'mouse', clientX: 0, clientY: 0 };

describe('createDayPress', () => {
	beforeEach(() => vi.useFakeTimers());
	afterEach(() => vi.useRealTimers());

	function setup(withOpen = true) {
		const onselect = vi.fn();
		const onopen = vi.fn();
		const press = createDayPress(() => ({ onselect, onopen: withOpen ? onopen : undefined }));
		return { press, onselect, onopen };
	}

	it('left click selects, right click opens', () => {
		const { press, onselect, onopen } = setup();
		press.pointerdown(mouse);
		expect(press.click()).toBe(false);
		expect(onselect).toHaveBeenCalledOnce();
		expect(press.contextmenu()).toBe(true);
		expect(onopen).toHaveBeenCalledOnce();
		expect(onselect).toHaveBeenCalledOnce();
	});

	it('a short tap selects; a long press opens once and swallows the trailing click and contextmenu', () => {
		const { press, onselect, onopen } = setup();
		press.pointerdown(touch());
		vi.advanceTimersByTime(200);
		press.pointerend();
		press.click();
		expect(onselect).toHaveBeenCalledOnce();
		expect(onopen).not.toHaveBeenCalled();

		press.pointerdown(touch());
		vi.advanceTimersByTime(500);
		expect(onopen).toHaveBeenCalledOnce();
		expect(press.contextmenu()).toBe(true); // Android's own long-press event
		press.pointerend();
		expect(press.click()).toBe(true);
		expect(onopen).toHaveBeenCalledOnce();
		expect(onselect).toHaveBeenCalledOnce();
	});

	it('a native contextmenu before the timer opens once, the timer does not fire again', () => {
		const { press, onopen } = setup();
		press.pointerdown(touch());
		vi.advanceTimersByTime(300);
		press.contextmenu();
		vi.advanceTimersByTime(500);
		expect(onopen).toHaveBeenCalledOnce();
	});

	it('moving (scrolling) cancels the long press', () => {
		const { press, onopen } = setup();
		press.pointerdown(touch(0, 0));
		press.pointermove(touch(0, 30));
		vi.advanceTimersByTime(600);
		expect(onopen).not.toHaveBeenCalled();
	});

	it('without onopen the browser keeps its context menu', () => {
		const { press } = setup(false);
		expect(press.contextmenu()).toBe(false);
	});

	it('without onopen a long press is just a tap', () => {
		const { press, onselect, onopen } = setup(false);
		press.pointerdown(touch());
		vi.advanceTimersByTime(1000);
		expect(onopen).not.toHaveBeenCalled();
		expect(press.click()).toBe(false);
		expect(onselect).toHaveBeenCalledOnce();
	});

	it('a mouse held down never long-presses', () => {
		const { press, onopen } = setup();
		press.pointerdown(mouse);
		vi.advanceTimersByTime(1000);
		expect(onopen).not.toHaveBeenCalled();
	});

	it('honours a custom delay', () => {
		const onopen = vi.fn();
		const press = createDayPress(() => ({ onselect: vi.fn(), onopen, delay: 800 }));
		press.pointerdown(touch());
		vi.advanceTimersByTime(799);
		expect(onopen).not.toHaveBeenCalled();
		vi.advanceTimersByTime(1);
		expect(onopen).toHaveBeenCalledOnce();
	});

	it('finger jitter within the tolerance keeps the long press', () => {
		const { press, onopen } = setup();
		press.pointerdown(touch(100, 100));
		press.pointermove(touch(106, 108)); // exactly 10 px
		vi.advanceTimersByTime(500);
		expect(onopen).toHaveBeenCalledOnce();
	});

	it('pointercancel (browser took over for scrolling) cancels the long press', () => {
		const { press, onopen } = setup();
		press.pointerdown(touch());
		vi.advanceTimersByTime(400);
		press.pointerend();
		vi.advanceTimersByTime(500);
		expect(onopen).not.toHaveBeenCalled();
	});

	it('the next press after a long press starts fresh', () => {
		const { press, onselect, onopen } = setup();
		press.pointerdown(touch());
		vi.advanceTimersByTime(500);
		press.pointerend();
		// the trailing click was never delivered; a new tap must still select
		press.pointerdown(touch());
		press.pointerend();
		expect(press.click()).toBe(false);
		expect(onselect).toHaveBeenCalledOnce();
		expect(onopen).toHaveBeenCalledOnce();
	});

	it('keyboard ContextMenu / Shift+F10 (contextmenu without a press) opens', () => {
		const { press, onopen, onselect } = setup();
		expect(press.contextmenu()).toBe(true);
		expect(onopen).toHaveBeenCalledOnce();
		// Enter/Space arrive as a plain click afterwards and still select
		expect(press.click()).toBe(false);
		expect(onselect).toHaveBeenCalledOnce();
	});
});

describe('dayPress action', () => {
	beforeEach(() => vi.useFakeTimers());
	afterEach(() => vi.useRealTimers());

	/** EventTarget stands in for the element: the action only needs listeners and `style`. */
	function mount(options: { onselect: () => void; onopen?: () => void }) {
		const style = { setProperty: vi.fn(), userSelect: '' };
		const node = Object.assign(new EventTarget(), { style });
		const action = dayPress(node as unknown as HTMLElement, options);
		const fire = (type: string, props: Record<string, unknown> = {}) => {
			const e = Object.assign(new Event(type, { cancelable: true }), props);
			node.dispatchEvent(e);
			return e.defaultPrevented;
		};
		return { node, style, action, fire };
	}

	it('disables the iOS callout and text selection', () => {
		const { style } = mount({ onselect: vi.fn() });
		expect(style.setProperty).toHaveBeenCalledWith('-webkit-touch-callout', 'none');
		expect(style.userSelect).toBe('none');
	});

	it('wires click and contextmenu, preventing the default only when consumed', () => {
		const onselect = vi.fn();
		const onopen = vi.fn();
		const { fire } = mount({ onselect, onopen });
		expect(fire('click')).toBe(false);
		expect(onselect).toHaveBeenCalledOnce();
		expect(fire('contextmenu')).toBe(true);
		expect(onopen).toHaveBeenCalledOnce();
	});

	it('wires the touch long press and swallows its trailing click', () => {
		const onselect = vi.fn();
		const onopen = vi.fn();
		const { fire } = mount({ onselect, onopen });
		fire('pointerdown', { pointerType: 'touch', clientX: 0, clientY: 0 });
		fire('pointermove', { pointerType: 'touch', clientX: 2, clientY: 2 });
		vi.advanceTimersByTime(500);
		expect(onopen).toHaveBeenCalledOnce();
		fire('pointerup');
		expect(fire('click')).toBe(true);
		expect(onselect).not.toHaveBeenCalled();

		fire('pointerdown', { pointerType: 'touch', clientX: 0, clientY: 0 });
		fire('pointercancel');
		vi.advanceTimersByTime(500);
		expect(onopen).toHaveBeenCalledOnce();
	});

	it('update() swaps the callbacks; without onopen the native menu stays', () => {
		const first = vi.fn();
		const second = vi.fn();
		const { action, fire } = mount({ onselect: first, onopen: vi.fn() });
		action.update({ onselect: second });
		fire('click');
		expect(first).not.toHaveBeenCalled();
		expect(second).toHaveBeenCalledOnce();
		expect(fire('contextmenu')).toBe(false);
	});

	it('destroy() removes the listeners and cancels a pending long press', () => {
		const onselect = vi.fn();
		const onopen = vi.fn();
		const { action, fire } = mount({ onselect, onopen });
		fire('pointerdown', { pointerType: 'touch', clientX: 0, clientY: 0 });
		action.destroy();
		vi.advanceTimersByTime(1000);
		fire('click');
		fire('contextmenu');
		expect(onopen).not.toHaveBeenCalled();
		expect(onselect).not.toHaveBeenCalled();
	});
});
