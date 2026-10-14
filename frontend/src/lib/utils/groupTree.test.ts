import { describe, expect, it } from 'vitest';
import { buildGroupTree, chain, inheritedFrom, parentOptions, subtreeIds } from './groupTree';

// Gastro (1) → Küche (2) → Frühschicht (3); Service (4) below Gastro; Büro (5) on top
const groups = [
	{ id: 3, name: 'Frühschicht', parentGroupId: 2, mode: null as string | null },
	{ id: 1, name: 'Gastro', parentGroupId: null, mode: 'Allowed' },
	{ id: 2, name: 'Küche', parentGroupId: 1, mode: null },
	{ id: 5, name: 'Büro', parentGroupId: null, mode: null },
	{ id: 4, name: 'Service', parentGroupId: 1, mode: 'Disabled' }
];

describe('buildGroupTree', () => {
	it('nests sub-groups below their parent, sorted by name, with depths', () => {
		const tree = buildGroupTree(groups);
		expect(tree.map((n) => n.group.name)).toEqual(['Büro', 'Gastro']);
		const gastro = tree[1];
		expect(gastro.children.map((n) => [n.group.name, n.depth])).toEqual([['Küche', 2], ['Service', 2]]);
		expect(gastro.children[0].children.map((n) => [n.group.name, n.depth])).toEqual([['Frühschicht', 3]]);
	});

	it('treats an unknown parent as top level and survives a cycle', () => {
		const tree = buildGroupTree([
			{ id: 1, name: 'A', parentGroupId: 2 },
			{ id: 2, name: 'B', parentGroupId: 1 },
			{ id: 3, name: 'C', parentGroupId: 99 }
		]);
		expect(tree.map((n) => n.group.name)).toContain('C');
		expect(tree.length).toBeGreaterThanOrEqual(2);
	});
});

describe('chain / subtreeIds', () => {
	it('walks up and down the tree', () => {
		expect(chain(groups, 3).map((g) => g.id)).toEqual([3, 2, 1]);
		expect(chain(groups, 99)).toEqual([]);
		expect([...subtreeIds(groups, 1)].sort()).toEqual([1, 2, 3, 4]);
		expect([...subtreeIds(groups, 5)]).toEqual([5]);
	});
});

describe('parentOptions', () => {
	const names = (selfId: number | null) => parentOptions(groups, selfId).map((o) => o.group.name);

	it('offers every group with room below it for a new group', () => {
		expect(names(null)).toEqual(['Büro', 'Gastro', 'Küche', 'Service']);   // not Frühschicht: level 3
	});

	it('excludes the group itself and its subtree, and parents that would make it too deep', () => {
		expect(names(2)).toEqual(['Büro', 'Gastro']);          // Küche brings two levels: only top-level parents
		expect(names(1)).toEqual([]);                          // Gastro has three levels: nowhere
		expect(names(3)).toEqual(['Büro', 'Gastro', 'Küche', 'Service']);
		expect(parentOptions(groups, null).find((o) => o.group.name === 'Küche')?.depth).toBe(2);
	});
});

describe('inheritedFrom', () => {
	it('takes the nearest ancestor that sets the value, else the organization', () => {
		expect(inheritedFrom(groups, 3, (g) => g.mode)).toEqual({ source: 'Gastro', value: 'Allowed' });
		expect(inheritedFrom(groups, 2, (g) => g.mode)).toEqual({ source: 'Gastro', value: 'Allowed' });
		expect(inheritedFrom(groups, 1, (g) => g.mode)).toEqual({ source: null, value: null });
		const middle = groups.map((g) => (g.id === 2 ? { ...g, mode: 'RequiresApproval' } : g));
		expect(inheritedFrom(middle, 3, (g) => g.mode)).toEqual({ source: 'Küche', value: 'RequiresApproval' });
	});
});
