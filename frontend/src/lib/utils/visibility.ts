import { t } from '$lib/i18n';

/**
 * The member visibility matrix (backend: `MemberVisibility`). Rows are kinds of information,
 * columns "in the viewer's own groups" / "outside". The backend decides what is returned; these
 * helpers only drive the settings UI, its preview sentence and which sections a page shows.
 */
export const VISIBILITY_KINDS = [
	'Member', 'Email', 'Groups', 'Presence', 'Vacation', 'SickDays', 'OtherAbsences',
	'AbsenceNotes', 'VacationBalance', 'WorkSchedule', 'TrackedTime',
	'Appointments', 'AppointmentNotes'
] as const;
export type VisibilityKind = (typeof VISIBILITY_KINDS)[number];
export type Column = 'inGroups' | 'outsideGroups';

export interface MatrixRow {
	kind?: string | null;
	inGroups?: boolean;
	outsideGroups?: boolean;
}

export interface GroupMatrixRow {
	kind?: string | null;
	inGroups?: boolean | null;
	outsideGroups?: boolean | null;
}

/** The org's (or an effective) value of one cell; unknown rows count as hidden. */
export function cell(rows: MatrixRow[] | null | undefined, kind: VisibilityKind, column: Column): boolean {
	return rows?.find((r) => r.kind === kind)?.[column] ?? false;
}

/** A group's override of one cell: true allow, false deny, null inherit. */
export function groupCell(rows: GroupMatrixRow[] | null | undefined, kind: VisibilityKind, column: Column): boolean | null {
	return rows?.find((r) => r.kind === kind)?.[column] ?? null;
}

/**
 * What members see about a person in these groups (mirrors the backend's `Combine`): per cell a
 * group override replaces the org default, the most permissive overriding group wins, and nothing
 * is visible while the person ("Member") is hidden.
 */
export function effectiveMatrix(org: MatrixRow[] | null | undefined, groups: (GroupMatrixRow[] | null | undefined)[]): Record<Column, VisibilityKind[]> {
	const column = (c: Column) => {
		const visible = VISIBILITY_KINDS.filter((kind) => {
			const overrides = groups.map((g) => groupCell(g, kind, c)).filter((v): v is boolean => v !== null);
			return overrides.length > 0 ? overrides.some(Boolean) : cell(org, kind, c);
		});
		return visible.includes('Member') ? visible : [];
	};
	return { inGroups: column('inGroups'), outsideGroups: column('outsideGroups') };
}

function list(kinds: VisibilityKind[]): string {
	return kinds.length === 0 ? t('visibility.preview.nothing') : kinds.map((k) => t(`visibility.short.${k}`)).join(', ');
}

/**
 * „Ein Mitglied von Team Küche sieht über Kolleg:innen der eigenen Gruppen: …; über alle anderen: …“.
 * In-group colleagues are assumed to be in `group` (its overrides apply), everyone else to have no override.
 */
export function previewSentence(org: MatrixRow[] | null | undefined, group?: { name?: string | null; visibility?: GroupMatrixRow[] | null } | null): string {
	const inGroups = effectiveMatrix(org, group ? [group.visibility] : []).inGroups;
	const outside = effectiveMatrix(org, []).outsideGroups;
	const who = group?.name ? t('visibility.preview.ofGroup', { group: group.name }) : t('visibility.preview.member');
	return t('visibility.preview.sentence', { who, inGroups: list(inGroups), outside: list(outside) });
}

/** Tri-state select value of a group cell: 'inherit' | 'allow' | 'deny'. */
export function overrideValue(v: boolean | null): 'inherit' | 'allow' | 'deny' {
	return v === null ? 'inherit' : v ? 'allow' : 'deny';
}

export function parseOverride(value: string): boolean | null {
	return value === 'allow' ? true : value === 'deny' ? false : null;
}

/** Whether the org detail shows this kind of information about the member (self/admins: everything). */
export function canSee(member: { visibleInfo?: string[] | null } | null | undefined, kind: VisibilityKind): boolean {
	return member?.visibleInfo?.includes(kind) ?? false;
}
