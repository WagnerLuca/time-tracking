<script lang="ts">
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import MemberCalendar from '$lib/components/MemberCalendar.svelte';
</script>

<div class="max-w-4xl mx-auto py-6 px-4">
	<h1 class="text-2xl font-bold text-base-content mb-4 m-0">History</h1>

	{#if orgContext.selectedOrgSlug && auth.user?.id}
		{#key `${orgContext.selectedOrgSlug}-${auth.user.id}`}
			<MemberCalendar
				orgSlug={orgContext.selectedOrgSlug}
				orgId={orgContext.selectedOrgId}
				userId={auth.user.id}
				isSelf={true}
			/>
		{/key}
	{:else}
		<p class="text-base-content/50 text-sm">Select an organization to see your history.</p>
	{/if}
</div>
