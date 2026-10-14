import type { TeamMemberResponse } from '$lib/api';

/** The dashboard team overview shows a search field above this many members. */
export const TEAM_SEARCH_THRESHOLD = 12;

const fullName = (m: TeamMemberResponse) => `${m.firstName ?? ''} ${m.lastName ?? ''}`.trim();

/** Present members first, then by name. */
export function sortTeam(members: TeamMemberResponse[]): TeamMemberResponse[] {
	return [...members].sort((a, b) => Number(b.present === true) - Number(a.present === true) || fullName(a).localeCompare(fullName(b)));
}

/** Members of the group (null = all) whose name contains the query (case-insensitive). */
export function filterTeam(members: TeamMemberResponse[], groupId: number | null, query: string): TeamMemberResponse[] {
	const q = query.trim().toLocaleLowerCase();
	return members.filter((m) => (groupId === null || (m.groupIds ?? []).includes(groupId)) && (!q || fullName(m).toLocaleLowerCase().includes(q)));
}
