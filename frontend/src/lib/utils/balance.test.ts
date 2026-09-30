import { describe, expect, it } from 'vitest';
import { balanceToMinutes } from './balance';

describe('balanceToMinutes', () => {
	it('converts hours to rounded minutes', () => {
		const b = balanceToMinutes({
			cumulativeOvertimeHours: -53.67,
			initialOvertimeHours: 1.5,
			today: { targetHours: 8, workedHours: 0, overtimeHours: -8 },
			week: { targetHours: 24, workedHours: 13.95, overtimeHours: -10.05 },
			month: { targetHours: 176, workedHours: 149.6, overtimeHours: -26.4 },
			failureEntryCount: 1
		});
		expect(b.cumulative).toBe(-3220);
		expect(b.initial).toBe(90);
		expect(b.today).toEqual({ worked: 0, target: 480, delta: -480 });
		expect(b.week).toEqual({ worked: 837, target: 1440, delta: -603 });
		expect(b.month.delta).toBe(-1584);
		expect(b.failureEntryCount).toBe(1);
	});

	it('treats missing fields as zero', () => {
		const b = balanceToMinutes({ today: {}, week: {}, month: {} });
		expect(b.cumulative).toBe(0);
		expect(b.week).toEqual({ worked: 0, target: 0, delta: 0 });
		expect(b.failureEntryCount).toBe(0);
	});
});
