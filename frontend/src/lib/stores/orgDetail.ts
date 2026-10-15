import { organizationsApi } from '$lib/apiClient';
import { auth } from '$lib/stores/auth.svelte';
import type { OrganizationDetailResponse, UpdateOrganizationRequest } from '$lib/api';

/**
 * Organization details cached per user + slug, so moving between the org pages
 * doesn't refetch them. Pages pass `fresh: true` after their own mutations and
 * call `invalidateOrgDetail(slug)` after changes that alter the details elsewhere.
 * ponytail: 60 s TTL as the backstop for changes made by other people/tabs.
 */
const TTL_MS = 60_000;
const cache = new Map<string, { at: number; promise: Promise<OrganizationDetailResponse> }>();

const keyOf = (slug: string) => `${auth.user?.id ?? ''}:${slug}`;

export function getOrgDetail(slug: string, { fresh = false } = {}): Promise<OrganizationDetailResponse> {
	const key = keyOf(slug);
	const hit = cache.get(key);
	if (hit && !fresh && Date.now() - hit.at < TTL_MS) return hit.promise;
	const promise = organizationsApi.apiV1OrganizationsSlugGet(slug).then((r) => r.data);
	const entry = { at: Date.now(), promise };
	cache.set(key, entry);
	// Never cache a failure
	promise.catch(() => { if (cache.get(key) === entry) cache.delete(key); });
	return promise;
}

/** Drop one org's cached details (all if no slug is given). */
export function invalidateOrgDetail(slug?: string | null) {
	if (!slug) cache.clear();
	else for (const key of cache.keys()) if (key.endsWith(`:${slug}`)) cache.delete(key);
}

/**
 * Saves the "Allgemein" section: the time settings under the current slug first (only when changed),
 * then the profile (a slug change last). The cached details are dropped as soon as the settings are
 * saved, so a failing second call leaves no stale time zone or day end behind elsewhere.
 */
export async function saveOrgGeneral(
	slug: string,
	current: Pick<OrganizationDetailResponse, 'timeZone' | 'businessDayEnd'>,
	time: { timeZone: string; businessDayEnd: string },
	profile: UpdateOrganizationRequest
) {
	if (time.timeZone !== current.timeZone || time.businessDayEnd !== current.businessDayEnd) {
		await organizationsApi.apiV1OrganizationsSlugSettingsPut(slug, time);
		invalidateOrgDetail(slug);
	}
	await organizationsApi.apiV1OrganizationsSlugPut(slug, profile);
	invalidateOrgDetail(slug);
}
