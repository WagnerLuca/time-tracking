import { describe, expect, it, vi } from 'vitest';
import { fetchAllPages } from './pagination';

describe('fetchAllPages', () => {
	it('follows hasMore with the running offset', async () => {
		const data = Array.from({ length: 450 }, (_, i) => i);
		const fetchPage = vi.fn(async (limit: number, offset: number) => ({
			items: data.slice(offset, offset + limit),
			hasMore: offset + limit < data.length
		}));
		expect(await fetchAllPages(fetchPage)).toEqual(data);
		expect(fetchPage.mock.calls).toEqual([[200, 0], [200, 200], [200, 400]]);
	});

	it('stops on a single page and on an empty page that still claims hasMore', async () => {
		expect(await fetchAllPages(async () => ({ items: [1, 2], hasMore: false }))).toEqual([1, 2]);
		const broken = vi.fn(async () => ({ items: [], hasMore: true }));
		expect(await fetchAllPages(broken)).toEqual([]);
		expect(broken).toHaveBeenCalledTimes(1);
	});
});
