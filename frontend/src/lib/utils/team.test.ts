import { describe, expect, it } from 'vitest';
import type { TeamMemberResponse } from '$lib/api';
import { filterTeam, sortTeam } from './team';

const m = (userId: number, firstName: string, present: boolean | null, groupIds: number[] = []): TeamMemberResponse =>
	({ userId, firstName, lastName: 'Test', present, groupIds });

const team = [m(1, 'Zoe', false, [1]), m(2, 'Ben', true, [2]), m(3, 'Anna', null, [1, 2]), m(4, 'Ömer', true, [1])];

describe('team overview helpers', () => {
	it('sorts present members first, then by name (hidden presence counts as not present)', () => {
		expect(sortTeam(team).map((x) => x.firstName)).toEqual(['Ben', 'Ömer', 'Anna', 'Zoe']);
		expect(team[0].firstName).toBe('Zoe'); // not mutated
	});

	it('filters by group and by a case-insensitive name search', () => {
		expect(filterTeam(team, null, '').map((x) => x.userId)).toEqual([1, 2, 3, 4]);
		expect(filterTeam(team, 2, '').map((x) => x.userId)).toEqual([2, 3]);
		expect(filterTeam(team, null, '  öM ').map((x) => x.userId)).toEqual([4]);
		expect(filterTeam(team, 1, 'test').map((x) => x.userId)).toEqual([1, 3, 4]);
		expect(filterTeam([{ userId: 5, firstName: 'X', lastName: 'Y' }], 1, '')).toEqual([]);
	});
});
