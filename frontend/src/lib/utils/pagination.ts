/** The API clamps every list endpoint's `limit` to this. */
export const MAX_PAGE_SIZE = 200;

export interface Page<T> {
	items?: T[] | null;
	hasMore?: boolean;
}

/**
 * Fetch every item of an offset-paginated list endpoint by following `hasMore`.
 * `fetchPage(limit, offset)` returns one page, e.g.
 * `(l, o) => absenceDayApi.apiV1OrganizationsSlugAbsencesGet(slug, id, from, to, l, o).then((r) => r.data)`.
 */
export async function fetchAllPages<T>(
	fetchPage: (limit: number, offset: number) => Promise<Page<T>>,
	pageSize = MAX_PAGE_SIZE
): Promise<T[]> {
	const all: T[] = [];
	for (;;) {
		const page = await fetchPage(pageSize, all.length);
		const items = page.items ?? [];
		all.push(...items);
		// An empty page with hasMore would loop forever — stop there too
		if (!page.hasMore || items.length === 0) return all;
	}
}
