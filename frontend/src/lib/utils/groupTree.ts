/**
 * Sub-groups (backend: `GroupTree`, issue #59). The backend resolves and enforces everything;
 * these helpers only draw the tree, offer valid parents and explain where an inherited value
 * comes from ("geerbt von Abteilung Gastro: Erlaubt").
 */

/** Levels including the top level (mirrors `GroupTree.MaxDepth`). */
export const MAX_GROUP_DEPTH = 3;

export interface TreeGroup {
	id?: number;
	name?: string | null;
	parentGroupId?: number | null;
}

export interface GroupNode<T extends TreeGroup> {
	group: T;
	depth: number;
	children: GroupNode<T>[];
}

/** id → parent id, ignoring parents that are not in the list and cutting cycles. */
function parentMap(groups: TreeGroup[]): Map<number, number> {
	const ids = new Set(groups.map((g) => g.id));
	const parent = new Map<number, number>();
	for (const g of groups) if (g.id != null && g.parentGroupId != null && ids.has(g.parentGroupId)) parent.set(g.id, g.parentGroupId);
	for (const g of groups) {
		const seen = new Set([g.id]);
		for (let cur = g.id!; parent.has(cur); cur = parent.get(cur)!) {
			if (seen.has(parent.get(cur))) { parent.delete(cur); break; }
			seen.add(parent.get(cur));
		}
	}
	return parent;
}

/** The groups as a forest, siblings sorted by name; a group whose parent is unknown is a root. */
export function buildGroupTree<T extends TreeGroup>(groups: T[]): GroupNode<T>[] {
	const parent = parentMap(groups);
	const byName = (a: GroupNode<T>, b: GroupNode<T>) => (a.group.name ?? '').localeCompare(b.group.name ?? '');
	const build = (g: T, depth: number): GroupNode<T> => ({
		group: g,
		depth,
		children: groups.filter((c) => parent.get(c.id!) === g.id).map((c) => build(c, depth + 1)).sort(byName)
	});
	return groups.filter((g) => !parent.has(g.id!)).map((g) => build(g, 1)).sort(byName);
}

/** The group, then its parent, up to the top. */
export function chain<T extends TreeGroup>(groups: T[], id: number): T[] {
	const parent = parentMap(groups);
	const result: T[] = [];
	for (let cur: number | undefined = id; cur != null; cur = parent.get(cur)) {
		const g = groups.find((x) => x.id === cur);
		if (!g) break;
		result.push(g);
	}
	return result;
}

/** The group and all its descendants. */
export function subtreeIds(groups: TreeGroup[], id: number): Set<number> {
	const parent = parentMap(groups);
	const result = new Set([id]);
	let grew = true;
	while (grew) {
		grew = false;
		for (const [child, p] of parent) if (result.has(p) && !result.has(child)) { result.add(child); grew = true; }
	}
	return result;
}

/**
 * Groups that may become the parent of `selfId` (null = a new group): never the group itself or
 * one of its sub-groups, and only where the moved subtree still fits into three levels.
 * Returned in tree order with their depth (for indenting the options).
 */
export function parentOptions<T extends TreeGroup>(groups: T[], selfId: number | null): { group: T; depth: number }[] {
	const excluded = selfId == null ? new Set<number>() : subtreeIds(groups, selfId);
	const level = (id: number) => chain(groups, id).length;
	// levels the moved group brings along (1 for a new group or one without sub-groups)
	const own = selfId == null ? 1 : Math.max(...[...excluded].map(level)) - level(selfId) + 1;
	const tree = buildGroupTree(groups);
	const result: { group: T; depth: number }[] = [];
	const walk = (nodes: GroupNode<T>[]) => {
		for (const n of nodes) {
			if (excluded.has(n.group.id!)) continue;
			if (n.depth + own <= MAX_GROUP_DEPTH) result.push({ group: n.group, depth: n.depth });
			walk(n.children);
		}
	};
	walk(tree);
	return result;
}

export interface Inherited<V> {
	/** The ancestor group the value comes from; null = the organization default. */
	source: string | null;
	/** The ancestor's value; null when it comes from the organization (the caller supplies the default). */
	value: V | null;
}

/**
 * Where a setting left on "inherit" gets its value: the nearest ancestor (parent first) that sets
 * it, else the organization. `read` returns a group's own value (null/undefined = inherit).
 */
export function inheritedFrom<T extends TreeGroup, V>(groups: T[], id: number, read: (g: T) => V | null | undefined): Inherited<V> {
	for (const g of chain(groups, id).slice(1)) {
		const v = read(g);
		if (v != null) return { source: g.name ?? '', value: v };
	}
	return { source: null, value: null };
}
