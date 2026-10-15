import { describe, expect, it } from 'vitest';
import type { OrgChartResponse } from '$lib/api';
import {
	ROOT_KEY, ancestors, buildOrgChart, directPeople, groupLeads, groupRules, indexChart, initialExpanded,
	personGroups, searchChart, treeKey, visibleNodes, type BranchNode
} from './orgChart';
import { flattenGroupTree, buildGroupTree } from './groupTree';

// Gastro (1) → Küche (2) → Frühschicht (3); Büro (4) on top
const groups = [
	{ id: 1, name: 'Gastro', parentGroupId: null, canManageMembers: true },
	{ id: 2, name: 'Küche', parentGroupId: 1, canManageMembers: true },
	{ id: 3, name: 'Frühschicht', parentGroupId: 2, canManageMembers: false },
	{ id: 4, name: 'Büro', parentGroupId: null, canManageMembers: false }
];
const person = (userId: number, firstName: string, lastName: string, memberships: { groupId: number; isLead?: boolean }[] | null, present: boolean | null = null) =>
	({ userId, firstName, lastName, role: 'Member', groups: memberships, present, visibleInfo: ['Member'] });

const chart: OrgChartResponse = {
	organizationName: 'Wirtshaus',
	groups,
	people: [
		person(1, 'Gina', 'Gastro', [{ groupId: 1, isLead: true }], true),
		person(2, 'Karl', 'Koch', [{ groupId: 2 }, { groupId: 4 }], false),
		person(3, 'Frida', 'Früh', [{ groupId: 3 }], true),
		person(4, 'Bea', 'Büro', [{ groupId: 4, isLead: true }]),
		person(5, 'Otto', 'Ohne', []),
		person(6, 'Vera', 'Verborgen', null)
	]
};

const root = buildOrgChart(chart);
const node = (key: string) => indexChart(root).byKey.get(key) as BranchNode;

describe('buildOrgChart', () => {
	it('draws organization → groups → sub-groups → people, plus "no group" and "groups hidden" nodes', () => {
		expect(root.kind).toBe('org');
		expect(root.children.map((c) => c.key)).toEqual(['g4', 'g1', 'none', 'hidden']);
		const gastro = node('g1');
		expect(gastro.children.map((c) => c.key)).toEqual(['g2', 'g1/p1']);
		expect(node('g2').children.map((c) => c.key)).toEqual(['g3', 'g2/p2']);
		expect(node('none').children.map((c) => c.key)).toEqual(['none/p5']);
		expect(node('hidden').children.map((c) => c.key)).toEqual(['hidden/p6']);
	});

	it('counts distinct people per subtree and presence only where it is visible', () => {
		expect(root.count).toBe(6);
		expect(root.present).toBe(2);
		expect(node('g1').count).toBe(3);
		expect(node('g1').present).toBe(2);
		expect(node('g4').present).toBe(0); // Karl's presence is visible (not present), Bea's is not counted
		expect(node('none').present).toBeNull(); // nobody's presence visible
		expect(node('g4').count).toBe(2);
		expect(directPeople(node('g1'))).toBe(1);
	});

	it('marks leads (listed first) and people in several groups', () => {
		const buero = node('g4');
		expect(buero.children.map((c) => c.key)).toEqual(['g4/p4', 'g4/p2']);
		expect(groupLeads(buero).map((p) => p.firstName)).toEqual(['Bea']);
		const karl = indexChart(root).byKey.get('g2/p2');
		expect(karl && karl.kind === 'person' && karl.groupCount).toBe(2);
	});

	it('ignores memberships of unknown groups', () => {
		const tree = buildOrgChart({ ...chart, people: [person(9, 'X', 'Y', [{ groupId: 99 }])] });
		expect(tree.children.map((c) => c.key)).toEqual(['g4', 'g1', 'none']);
	});
});

describe('collapse and search', () => {
	it('opens everything for small organizations and only the top level above the threshold', () => {
		expect(initialExpanded(root).has('g3')).toBe(true);
		expect([...initialExpanded(root, 5)]).toEqual([ROOT_KEY]);
	});

	it('finds people and groups by every word and opens the path to them', () => {
		const found = searchChart(root, 'frida FRÜH');
		expect(found.matches).toEqual(['g3/p3']);
		expect([...found.expand]).toEqual([ROOT_KEY, 'g1', 'g2', 'g3']);
		expect(searchChart(root, 'küche').matches).toEqual(['g2']);
		expect(searchChart(root, 'koch').matches).toEqual(['g4/p2', 'g2/p2']);
		expect(searchChart(root, '  ').matches).toEqual([]);
	});

	it('lists ancestors nearest first', () => {
		expect(ancestors(indexChart(root).parent, 'g3/p3')).toEqual(['g3', 'g2', 'g1', ROOT_KEY]);
	});

	it('flattens only expanded branches', () => {
		const nodes = visibleNodes(root, new Set([ROOT_KEY, 'g1']));
		expect(nodes.map((v) => v.node.key)).toEqual(['org', 'g4', 'g1', 'g2', 'g1/p1', 'none', 'hidden']);
		expect(nodes.find((v) => v.node.key === 'g2')?.level).toBe(3);
	});
});

describe('treeKey', () => {
	const expanded = new Set([ROOT_KEY, 'g1']);
	const nodes = visibleNodes(root, expanded);
	it('moves up and down, home and end', () => {
		expect(treeKey('ArrowDown', 'g4', nodes, expanded)).toEqual({ focus: 'g1' });
		expect(treeKey('ArrowUp', 'g4', nodes, expanded)).toEqual({ focus: 'org' });
		expect(treeKey('ArrowUp', 'org', nodes, expanded)).toEqual({});
		expect(treeKey('ArrowDown', 'hidden', nodes, expanded)).toEqual({});
		expect(treeKey('Home', 'g2', nodes, expanded)).toEqual({ focus: 'org' });
		expect(treeKey('End', 'g2', nodes, expanded)).toEqual({ focus: 'hidden' });
	});
	it('opens, enters, closes and leaves branches', () => {
		expect(treeKey('ArrowRight', 'g4', nodes, expanded)).toEqual({ toggle: 'g4' });
		expect(treeKey('ArrowRight', 'g1', nodes, expanded)).toEqual({ focus: 'g2' });
		expect(treeKey('ArrowRight', 'g1/p1', nodes, expanded)).toEqual({});
		expect(treeKey('ArrowLeft', 'g1', nodes, expanded)).toEqual({ toggle: 'g1' });
		expect(treeKey('ArrowLeft', 'g1/p1', nodes, expanded)).toEqual({ focus: 'g1' });
		expect(treeKey('ArrowLeft', 'org', visibleNodes(root, new Set()), new Set())).toEqual({});
	});
	it('opens details on Enter/Space, every sibling on *, ignores other keys', () => {
		expect(treeKey('Enter', 'g2', nodes, expanded)).toEqual({ open: 'g2' });
		expect(treeKey(' ', 'g1/p1', nodes, expanded)).toEqual({ open: 'g1/p1' });
		expect(treeKey('*', 'g4', nodes, expanded)?.expandSiblings).toEqual(['g4', 'g1', 'none', 'hidden']);
		expect(treeKey('a', 'g4', nodes, expanded)).toBeNull();
		expect(treeKey('ArrowDown', 'nope', nodes, expanded)).toBeNull();
	});
});

describe('personGroups', () => {
	it('lists direct groups (leads marked) and their ancestors "via" the direct group', () => {
		const frida = chart.people![2];
		expect(personGroups(frida, groups).map((g) => [g.group.name, g.isLead, g.via])).toEqual([
			['Frühschicht', false, null], ['Küche', false, 'Frühschicht'], ['Gastro', false, 'Frühschicht']
		]);
		const gina = chart.people![0];
		expect(personGroups(gina, groups).map((g) => [g.group.name, g.isLead])).toEqual([['Gastro', true]]);
		expect(personGroups(chart.people![5], groups)).toEqual([]);
	});
});

describe('groupRules', () => {
	const perms = [
		{ groupId: 1, groupName: 'Gastro', editPauseMode: 'Disabled', coreHoursMode: 'Own', coreHoursStart: '10:00', coreHoursEnd: '14:00' },
		{ groupId: 2, groupName: 'Küche', parentGroupId: 1, editPauseMode: null, csvImportMode: 'Allowed', coreHoursMode: 'Off' },
		{ groupId: 3, groupName: 'Frühschicht', parentGroupId: 2 }
	];
	const org = { editPastEntriesMode: 'RequiresApproval', editPauseMode: 'Allowed', csvImportMode: 'Disabled', coreHoursEnabled: true, coreHoursStart: '09:00', coreHoursEnd: '15:00' };
	const byKey = (id: number) => Object.fromEntries(groupRules(perms, id, org).map((r) => [r.key, r]));

	it('takes the own value, else the nearest ancestor, else the organization', () => {
		const frueh = byKey(3);
		expect(frueh.editPauseMode).toEqual({ key: 'editPauseMode', value: 'Disabled', inheritedFrom: 'Gastro' });
		expect(frueh.csvImportMode).toEqual({ key: 'csvImportMode', value: 'Allowed', inheritedFrom: 'Küche' });
		expect(frueh.editPastEntriesMode).toEqual({ key: 'editPastEntriesMode', value: 'RequiresApproval', inheritedFrom: null });
		expect(frueh.coreHours).toEqual({ key: 'coreHours', value: 'Off', inheritedFrom: 'Küche' });
		expect(byKey(1).coreHours).toEqual({ key: 'coreHours', value: '10:00–14:00' });
		expect(byKey(2).csvImportMode.inheritedFrom).toBeUndefined();
		expect(groupRules([], 9, { ...org, coreHoursEnabled: false }).find((r) => r.key === 'coreHours')?.value).toBe('Off');
		expect(groupRules([], 9, null)[0].value).toBeNull();
	});
});

describe('flattenGroupTree', () => {
	it('lists groups in tree order with depths', () => {
		expect(flattenGroupTree(buildGroupTree(groups)).map((o) => [o.group.name, o.depth])).toEqual([
			['Büro', 1], ['Gastro', 1], ['Küche', 2], ['Frühschicht', 3]
		]);
	});
});
