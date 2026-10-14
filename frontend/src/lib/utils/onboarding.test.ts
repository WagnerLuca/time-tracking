import { afterEach, describe, expect, it, vi } from 'vitest';
import { adminSteps, dismissChecklist, isDismissed, markSeen, memberSteps, wasSeen } from './onboarding';

afterEach(() => vi.unstubAllGlobals());

describe('onboarding checklists', () => {
	it('ticks the member steps from the facts and links each to its page', () => {
		const steps = memberSteps('acme', { hasSchedule: true, hasEntries: false, sawTeamCalendar: false });
		expect(steps.map((s) => [s.id, s.done, s.href])).toEqual([
			['schedule', true, '/organizations/acme'],
			['timer', false, '/time'],
			['teamCalendar', false, '/organizations/acme/absences']
		]);
	});

	it('ticks the admin steps', () => {
		expect(adminSteps('acme', { hasInvited: false, sawRules: true, hasHolidays: true }).map((s) => s.done)).toEqual([false, true, true]);
	});

	it('remembers seen pages and dismissals per user and organization', () => {
		const map = new Map<string, string>();
		vi.stubGlobal('localStorage', { getItem: (k: string) => map.get(k) ?? null, setItem: (k: string, v: string) => void map.set(k, v) });
		expect(wasSeen(1, 'acme', 'rules')).toBe(false);
		markSeen(1, 'acme', 'rules');
		expect(wasSeen(1, 'acme', 'rules')).toBe(true);
		expect(wasSeen(2, 'acme', 'rules')).toBe(false);
		dismissChecklist(1, 'acme', 'member');
		expect(isDismissed(1, 'acme', 'member')).toBe(true);
		expect(isDismissed(1, 'acme', 'admin')).toBe(false);
	});

	it('survives blocked storage', () => {
		const boom = () => { throw new Error('SecurityError'); };
		vi.stubGlobal('localStorage', { getItem: boom, setItem: boom });
		expect(() => markSeen(1, 'acme', 'teamCalendar')).not.toThrow();
		expect(isDismissed(1, 'acme', 'member')).toBe(false);
	});
});
