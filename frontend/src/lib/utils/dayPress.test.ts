import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createDayPress } from './dayPress';

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
});
