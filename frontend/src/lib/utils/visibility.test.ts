import { afterEach, describe, expect, it } from 'vitest';
import { locale } from '$lib/stores/locale.svelte';
import {
	VISIBILITY_KINDS, canSee, cell, effectiveMatrix, groupCell, overrideValue, parseOverride, previewSentence,
	type GroupMatrixRow, type MatrixRow
} from './visibility';

/** Org matrix: listed kinds as [in groups, outside], everything else hidden. */
function org(cells: Record<string, [boolean, boolean]>): MatrixRow[] {
	return VISIBILITY_KINDS.map((kind) => ({ kind, inGroups: cells[kind]?.[0] ?? false, outsideGroups: cells[kind]?.[1] ?? false }));
}

function group(cells: Record<string, [boolean | null, boolean | null]>): GroupMatrixRow[] {
	return VISIBILITY_KINDS.map((kind) => ({ kind, inGroups: cells[kind]?.[0] ?? null, outsideGroups: cells[kind]?.[1] ?? null }));
}

const DEFAULT = org({ Member: [true, true], Email: [true, true], Groups: [true, true], AbsenceNotes: [true, true] });

describe('visibility matrix helpers', () => {
	afterEach(() => locale.set('de'));

	it('reads cells; unknown rows are hidden / inherit', () => {
		expect(cell(DEFAULT, 'Email', 'inGroups')).toBe(true);
		expect(cell(DEFAULT, 'Vacation', 'outsideGroups')).toBe(false);
		expect(cell(null, 'Member', 'inGroups')).toBe(false);
		expect(groupCell(group({ SickDays: [true, false] }), 'SickDays', 'outsideGroups')).toBe(false);
		expect(groupCell(undefined, 'SickDays', 'inGroups')).toBeNull();
	});

	it('combines like the backend: override replaces the org default, most permissive group wins', () => {
		const base = org({ Member: [true, true], Vacation: [true, true] });
		expect(effectiveMatrix(base, []).inGroups).toEqual(['Member', 'Vacation']);
		expect(effectiveMatrix(base, [group({ Vacation: [false, null] })]).inGroups).toEqual(['Member']);
		expect(effectiveMatrix(base, [group({ Vacation: [false, null] })]).outsideGroups).toEqual(['Member', 'Vacation']);
		expect(effectiveMatrix(base, [group({ Vacation: [false, null] }), group({ Vacation: [true, null] }), group({})]).inGroups)
			.toEqual(['Member', 'Vacation']);
		expect(effectiveMatrix(base, [group({ SickDays: [null, true] })]).outsideGroups).toEqual(['Member', 'Vacation', 'SickDays']);
	});

	it('shows nothing about a hidden person', () => {
		const m = effectiveMatrix(org({ Member: [true, false], Vacation: [true, true] }), [null]);
		expect(m.inGroups).toEqual(['Member', 'Vacation']);
		expect(m.outsideGroups).toEqual([]);
		expect(effectiveMatrix(DEFAULT, [group({ Member: [false, null] })]).inGroups).toEqual([]);
	});

	it('preview sentence names the group and lists the visible kinds per column', () => {
		const matrix = org({ Member: [true, true], Vacation: [true, false] });
		expect(previewSentence(matrix, { name: 'Team Küche', visibility: group({ SickDays: [true, null] }) })).toBe(
			'Ein Mitglied von „Team Küche“ sieht über Kolleg:innen der eigenen Gruppen: Name, Urlaub, Krankheitstage; über alle anderen: Name.'
		);
		expect(previewSentence(org({}), null)).toBe('Ein Mitglied sieht über Kolleg:innen der eigenen Gruppen: nichts; über alle anderen: nichts.');
		locale.set('en');
		expect(previewSentence(matrix, { name: 'Kitchen', visibility: [] })).toBe(
			'A member of “Kitchen” sees about colleagues in their own groups: name, vacation; about everyone else: name.'
		);
	});

	it('maps group cells to the tri-state select and back', () => {
		for (const v of [true, false, null]) expect(parseOverride(overrideValue(v))).toBe(v);
		expect(overrideValue(null)).toBe('inherit');
		expect(parseOverride('anything')).toBeNull();
	});

	it('canSee reads the kinds the backend returned for a member', () => {
		expect(canSee({ visibleInfo: ['Member', 'Vacation'] }, 'Vacation')).toBe(true);
		expect(canSee({ visibleInfo: ['Member'] }, 'TrackedTime')).toBe(false);
		expect(canSee({ visibleInfo: null }, 'Member')).toBe(false);
		expect(canSee(null, 'Member')).toBe(false);
	});
});
