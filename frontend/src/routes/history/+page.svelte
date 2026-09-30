<script lang="ts">
	import { page } from '$app/state';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import MemberCalendar from '$lib/components/MemberCalendar.svelte';
	import LoadingSpinner from '$lib/components/LoadingSpinner.svelte';

	// Deep link from the failure banner: /history?date=YYYY-MM-DD&entry=<id>
	const focusDate = $derived(page.url.searchParams.get('date'));
	const focusEntryId = $derived.by(() => {
		const id = Number(page.url.searchParams.get('entry'));
		return Number.isInteger(id) && id > 0 ? id : null;
	});
</script>

<svelte:head>
	<title>History - Time Tracking</title>
</svelte:head>

<div class="max-w-4xl mx-auto">
	<h1 class="text-2xl font-bold text-base-content mb-4 m-0">History</h1>

	{#if orgContext.selectedOrgSlug && auth.user?.id}
		{#key `${orgContext.selectedOrgSlug}-${auth.user.id}`}
			<MemberCalendar
				orgSlug={orgContext.selectedOrgSlug}
				orgId={orgContext.selectedOrgId}
				userId={auth.user.id}
				isSelf={true}
				{focusDate}
				{focusEntryId}
			/>
		{/key}
	{:else if orgContext.loading}
		<LoadingSpinner size="sm" />
	{:else}
		<div class="card bg-base-100 border border-base-300 p-5">
			<h2 class="text-base font-semibold m-0">No organization selected</h2>
			<p class="text-sm text-base-content/60 mt-1 mb-4">
				{orgContext.organizations.length > 0
					? 'Select an organization in the menu above to see your history.'
					: 'Your history lives inside an organization. Create or join one first.'}
			</p>
			<a href="/organizations" class="btn btn-primary btn-sm self-start">Go to organizations</a>
		</div>
	{/if}
</div>
