<script lang="ts">
	import { goto } from '$app/navigation';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import ExportPage from '$lib/components/ExportPage.svelte';

	// Old deep link: export now lives under Organisation → Export
	$effect(() => {
		if (orgContext.selectedOrgSlug) goto(`/organizations/${orgContext.selectedOrgSlug}/export`, { replaceState: true });
	});
</script>

{#if !orgContext.loading && !orgContext.selectedOrgSlug}
	<ExportPage />
{/if}
