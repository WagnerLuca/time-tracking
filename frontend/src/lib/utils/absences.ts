import { absenceDayApi } from '$lib/apiClient';

/**
 * Deletes a span of absence days in one request: all or nothing, so a closed month asks for the
 * reason once (the apiClient interceptor) and cancelling leaves the whole span in place.
 */
export function deleteAbsences(orgSlug: string, ids: number[]) {
	return absenceDayApi.apiV1OrganizationsSlugAbsencesDeletePost(orgSlug, { ids });
}
