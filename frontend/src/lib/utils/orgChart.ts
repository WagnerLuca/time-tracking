/**
 * The Organigramm (issue #66). The backend (`GET organizations/{slug}/org-chart`) returns the groups
 * with their parents and every person the viewer may see with their direct memberships; these helpers
 * turn that into the tree that is drawn (organization → groups → sub-groups → people), count it,
 * search it and flatten it for keyboard navigation. Visibility is the backend's job: whoever is not
 * in the response is not drawn and not counted.
 */
import type { GroupPermissionsResponse, OrgChartGroupResponse, OrgChartPersonResponse, OrgChartResponse, OrganizationDetailResponse } from '$lib/api';
import { buildGroupTree, inheritedFrom, type GroupNode } from './groupTree';

/** Above this many people the groups start collapsed (search expands the path to a person). */
export const COLLAPSE_THRESHOLD = 30;

export const ROOT_KEY = 'org';
/** Pseudo groups: members without a group, members whose groups the viewer may not see. */
export const NO_GROUP_KEY = 'none';
export const HIDDEN_GROUPS_KEY = 'hidden';

export interface PersonNode {
	kind: 'person';
	key: string;
	person: OrgChartPersonResponse;
	/** Lead of the group this node sits in. */
	isLead: boolean;
	/** The person also appears under other groups. */
	groupCount: number;
}

export interface BranchNode {
	kind: 'org' | 'group' | 'none' | 'hidden';
	key: string;
	group: OrgChartGroupResponse | null;
	children: ChartNode[];
	/** Distinct people in the whole subtree. */
	count: number;
	/** Present people in the subtree; null when the viewer sees nobody's presence there. */
	present: number | null;
}

export type ChartNode = BranchNode | PersonNode;

export const groupKey = (id: number) => `g${id}`;
const personKey = (parent: string, userId: number) => `${parent}/p${userId}`;

export const fullName = (p: OrgChartPersonResponse) => `${p.firstName ?? ''} ${p.lastName ?? ''}`.trim();

/** Leads first, then by last and first name. */
function personOrder(a: PersonNode, b: PersonNode): number {
	return Number(b.isLead) - Number(a.isLead)
		|| (a.person.lastName ?? '').localeCompare(b.person.lastName ?? '')
		|| (a.person.firstName ?? '').localeCompare(b.person.firstName ?? '');
}

function branch(kind: BranchNode['kind'], key: string, group: OrgChartGroupResponse | null, children: ChartNode[]): BranchNode {
	const people = new Map<number, OrgChartPersonResponse>();
	const collect = (n: ChartNode) => {
		if (n.kind === 'person') people.set(n.person.userId!, n.person);
		else n.children.forEach(collect);
	};
	children.forEach(collect);
	const withPresence = [...people.values()].filter((p) => p.present != null);
	return {
		kind, key, group, children,
		count: people.size,
		present: withPresence.length ? withPresence.filter((p) => p.present).length : null
	};
}

/** The whole chart as one tree; groups are sorted by name, sub-groups before the group's own people. */
export function buildOrgChart(chart: OrgChartResponse): BranchNode {
	const groups = chart.groups ?? [];
	const people = chart.people ?? [];
	const byGroup = new Map<number, PersonNode[]>();
	const noGroup: PersonNode[] = [];
	const hidden: PersonNode[] = [];
	const known = new Set(groups.map((g) => g.id));
	for (const person of people) {
		const memberships = (person.groups ?? []).filter((m) => known.has(m.groupId));
		if (person.groups == null) hidden.push({ kind: 'person', key: personKey(HIDDEN_GROUPS_KEY, person.userId!), person, isLead: false, groupCount: 0 });
		else if (memberships.length === 0) noGroup.push({ kind: 'person', key: personKey(NO_GROUP_KEY, person.userId!), person, isLead: false, groupCount: 0 });
		for (const m of memberships) {
			const list = byGroup.get(m.groupId!) ?? [];
			list.push({ kind: 'person', key: personKey(groupKey(m.groupId!), person.userId!), person, isLead: !!m.isLead, groupCount: memberships.length });
			byGroup.set(m.groupId!, list);
		}
	}
	const toNode = (n: GroupNode<OrgChartGroupResponse>): BranchNode =>
		branch('group', groupKey(n.group.id!), n.group, [...n.children.map(toNode), ...(byGroup.get(n.group.id!) ?? []).sort(personOrder)]);
	const children: ChartNode[] = buildGroupTree(groups).map(toNode);
	if (noGroup.length) children.push(branch('none', NO_GROUP_KEY, null, noGroup.sort(personOrder)));
	if (hidden.length) children.push(branch('hidden', HIDDEN_GROUPS_KEY, null, hidden.sort(personOrder)));
	return branch('org', ROOT_KEY, null, children);
}

/** Every branch key → its node, and every node key → its parent key. */
export function indexChart(root: BranchNode): { byKey: Map<string, ChartNode>; parent: Map<string, string> } {
	const byKey = new Map<string, ChartNode>();
	const parent = new Map<string, string>();
	const walk = (n: ChartNode, p: string | null) => {
		byKey.set(n.key, n);
		if (p) parent.set(n.key, p);
		if (n.kind !== 'person') n.children.forEach((c) => walk(c, n.key));
	};
	walk(root, null);
	return { byKey, parent };
}

/** The branches open at first: everything for small organizations, only the top level above the threshold. */
export function initialExpanded(root: BranchNode, threshold = COLLAPSE_THRESHOLD): Set<string> {
	if (root.count > threshold) return new Set([ROOT_KEY]);
	const result = new Set<string>();
	const walk = (n: ChartNode) => {
		if (n.kind === 'person') return;
		result.add(n.key);
		n.children.forEach(walk);
	};
	walk(root);
	return result;
}

/** The ancestors of a node (nearest first), up to the root. */
export function ancestors(parent: Map<string, string>, key: string): string[] {
	const result: string[] = [];
	for (let cur = parent.get(key); cur; cur = parent.get(cur)) result.push(cur);
	return result;
}

/**
 * Search people and groups by name (case-insensitive, every word must match): the matching node
 * keys in tree order and the branches to open so each match is on screen.
 */
export function searchChart(root: BranchNode, query: string): { matches: string[]; expand: Set<string> } {
	const words = query.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
	const matches: string[] = [];
	const expand = new Set<string>();
	if (!words.length) return { matches, expand };
	const walk = (n: ChartNode, path: string[]) => {
		const name = n.kind === 'person' ? fullName(n.person) : (n.group?.name ?? '');
		if (n.kind !== 'org' && name && words.every((w) => name.toLocaleLowerCase().includes(w))) {
			matches.push(n.key);
			path.forEach((k) => expand.add(k));
		}
		if (n.kind !== 'person') n.children.forEach((c) => walk(c, [...path, n.key]));
	};
	walk(root, []);
	return { matches, expand };
}

export interface VisibleNode {
	node: ChartNode;
	/** 1 = the organization. */
	level: number;
	parentKey: string | null;
}

/** The nodes a tree view shows (children of collapsed branches left out), in order — for arrow-key navigation. */
export function visibleNodes(root: BranchNode, expanded: Set<string>): VisibleNode[] {
	const result: VisibleNode[] = [];
	const walk = (n: ChartNode, level: number, parentKey: string | null) => {
		result.push({ node: n, level, parentKey });
		if (n.kind !== 'person' && expanded.has(n.key)) n.children.forEach((c) => walk(c, level + 1, n.key));
	};
	walk(root, 1, null);
	return result;
}

export type TreeKeyResult = { focus?: string; toggle?: string; open?: string; expandSiblings?: string[] } | null;

/**
 * The WAI-ARIA tree view keys: ↑/↓ previous/next visible node, → opens a closed branch or moves to
 * its first child, ← closes an open branch or moves to the parent, Home/End, Enter/Space opens the
 * details, * opens every sibling branch. Returns what to do (null = key not handled).
 */
export function treeKey(key: string, current: string, nodes: VisibleNode[], expanded: Set<string>): TreeKeyResult {
	const i = nodes.findIndex((v) => v.node.key === current);
	if (i < 0) return null;
	const here = nodes[i];
	const isBranch = here.node.kind !== 'person' && here.node.children.length > 0;
	switch (key) {
		case 'ArrowDown': return i + 1 < nodes.length ? { focus: nodes[i + 1].node.key } : {};
		case 'ArrowUp': return i > 0 ? { focus: nodes[i - 1].node.key } : {};
		case 'Home': return { focus: nodes[0].node.key };
		case 'End': return { focus: nodes[nodes.length - 1].node.key };
		case 'ArrowRight':
			if (!isBranch) return {};
			return expanded.has(current) ? { focus: nodes[i + 1]?.node.key } : { toggle: current };
		case 'ArrowLeft':
			if (isBranch && expanded.has(current)) return { toggle: current };
			return here.parentKey ? { focus: here.parentKey } : {};
		case 'Enter':
		case ' ': return { open: current };
		case '*':
			return { expandSiblings: nodes.filter((v) => v.parentKey === here.parentKey && v.node.kind !== 'person' && v.node.children.length > 0).map((v) => v.node.key) };
		default: return null;
	}
}

export interface PersonGroup {
	group: OrgChartGroupResponse;
	isLead: boolean;
	/** Inherited membership through this (direct) sub-group. */
	via: string | null;
}

/** A person's groups for the card: direct ones (leads marked), then the ancestors as "über …". */
export function personGroups(person: OrgChartPersonResponse, groups: OrgChartGroupResponse[]): PersonGroup[] {
	const byId = new Map(groups.map((g) => [g.id!, g]));
	const direct = (person.groups ?? []).filter((m) => byId.has(m.groupId!))
		.map((m) => ({ group: byId.get(m.groupId!)!, isLead: !!m.isLead, via: null }))
		.sort((a, b) => (a.group.name ?? '').localeCompare(b.group.name ?? ''));
	const seen = new Set(direct.map((d) => d.group.id));
	const inherited: PersonGroup[] = [];
	for (const d of direct) {
		const visited = new Set([d.group.id]);
		for (let p = d.group.parentGroupId; p != null && byId.has(p) && !visited.has(p); p = byId.get(p)!.parentGroupId) {
			visited.add(p);
			if (seen.has(p)) continue;
			seen.add(p);
			inherited.push({ group: byId.get(p)!, isLead: false, via: d.group.name ?? '' });
		}
	}
	return [...direct, ...inherited];
}

/** The direct leads of a group among the people shown. */
export function groupLeads(node: BranchNode): OrgChartPersonResponse[] {
	return node.children.filter((c): c is PersonNode => c.kind === 'person' && c.isLead).map((c) => c.person);
}

/** People directly in a branch (not in its sub-groups). */
export function directPeople(node: BranchNode): number {
	return node.children.filter((c) => c.kind === 'person').length;
}

export const GROUP_RULE_KEYS = ['editPastEntriesMode', 'editPauseMode', 'csvImportMode', 'workScheduleChangeMode', 'initialOvertimeMode', 'absenceSelfServiceMode'] as const;
export type GroupRuleKey = (typeof GROUP_RULE_KEYS)[number] | 'coreHours';

export interface GroupRule {
	key: GroupRuleKey;
	/** A rule mode ("Allowed", …), or for core hours "Off" / "09:00–15:00". */
	value: string | null;
	/** Where an inherited value comes from: a parent group's name, or null = the organization; undefined = the group's own setting. */
	inheritedFrom?: string | null;
}

/**
 * "Für diese Gruppe gilt: …" — the group's effective rule modes and core hours: its own override,
 * else the nearest ancestor's, else the organization default (the backend's `GroupTree.Effective`).
 */
export function groupRules(perms: GroupPermissionsResponse[], groupId: number, org: Partial<OrganizationDetailResponse> | null | undefined): GroupRule[] {
	const groups = perms.map((p) => ({ ...p, id: p.groupId, name: p.groupName }));
	const self = groups.find((g) => g.id === groupId);
	const resolve = <V>(read: (g: (typeof groups)[number]) => V | null | undefined, orgValue: V | null): { value: V | null; inheritedFrom?: string | null } => {
		const own = self ? read(self) : null;
		if (own != null) return { value: own };
		const inherited = inheritedFrom(groups, groupId, read);
		return inherited.source ? { value: inherited.value, inheritedFrom: inherited.source } : { value: orgValue, inheritedFrom: null };
	};
	const window = (mode: string | null | undefined, start?: string | null, end?: string | null) =>
		mode === 'Own' ? `${start ?? ''}–${end ?? ''}` : mode ? 'Off' : null;
	return [
		...GROUP_RULE_KEYS.map((key) => ({ key, ...resolve((g) => g[key], (org?.[key] as string | null | undefined) ?? null) })),
		{ key: 'coreHours' as const, ...resolve((g) => window(g.coreHoursMode, g.coreHoursStart, g.coreHoursEnd), org?.coreHoursEnabled ? window('Own', org.coreHoursStart, org.coreHoursEnd) : 'Off') }
	];
}
