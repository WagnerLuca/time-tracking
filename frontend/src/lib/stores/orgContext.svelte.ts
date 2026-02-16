import { apiService } from '$lib/apiService';
import type { UserOrganizationResponse } from '$lib/types';

const ORG_KEY = 'selectedOrgId';

function createOrgContext() {
	let organizations = $state<UserOrganizationResponse[]>([]);
	let selectedOrgId = $state<number | null>(null);
	let loading = $state(true);

	const selectedOrg = $derived(
		organizations.find((o) => o.organizationId === selectedOrgId) ?? null
	);

	const selectedOrgSlug = $derived(selectedOrg?.slug ?? null);

	function init() {
		if (typeof window === 'undefined') return;
		const saved = localStorage.getItem(ORG_KEY);
		if (saved) {
			selectedOrgId = parseInt(saved);
		}
	}

	async function loadOrganizations(userId: number) {
		loading = true;
		try {
			organizations = await apiService.get<UserOrganizationResponse[]>(
				`/api/Organizations/user/${userId}`
			);
			// Validate that saved org still exists
			if (selectedOrgId && !organizations.some((o) => o.organizationId === selectedOrgId)) {
				selectedOrgId = null;
				localStorage.removeItem(ORG_KEY);
			}
			// Auto-select when user has exactly one org and none is selected
			if (!selectedOrgId && organizations.length === 1) {
				select(organizations[0].organizationId);
			}
		} catch {
			organizations = [];
		} finally {
			loading = false;
		}
	}

	function select(orgId: number | null) {
		selectedOrgId = orgId;
		if (orgId) {
			localStorage.setItem(ORG_KEY, String(orgId));
		} else {
			localStorage.removeItem(ORG_KEY);
		}
	}

	return {
		get organizations() { return organizations; },
		get selectedOrgId() { return selectedOrgId; },
		get selectedOrgSlug() { return selectedOrgSlug; },
		get selectedOrg() { return selectedOrg; },
		get loading() { return loading; },
		init,
		loadOrganizations,
		select
	};
}

export const orgContext = createOrgContext();
