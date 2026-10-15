<script lang="ts">
	// The manifest's "Teamkalender" shortcut: the team calendar of the selected organization
	import { goto } from '$app/navigation';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import Skeleton from '$lib/components/Skeleton.svelte';

	$effect(() => {
		if (orgContext.loading) return;
		const slug = orgContext.selectedOrgSlug;
		goto(slug ? `/organizations/${slug}/absences` : '/organizations', { replaceState: true });
	});
</script>

<Skeleton />
