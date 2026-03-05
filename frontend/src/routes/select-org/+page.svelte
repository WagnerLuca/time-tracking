<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { goto } from '$app/navigation';

	function selectOrg(orgId: number) {
		orgContext.select(orgId);
		goto('/');
	}

	function continuePersonal() {
		orgContext.select(null);
		goto('/');
	}

	// If already selected, redirect to dashboard
	$effect(() => {
		if (!orgContext.loading && orgContext.selectedOrgId) {
			goto('/');
		}
	});
</script>

<svelte:head>
	<title>Select Organization - Time Tracking</title>
</svelte:head>

<div class="min-h-[80vh] flex items-center justify-center p-8">
	<div class="card bg-base-100 shadow-lg w-full max-w-[480px] text-center">
		<div class="card-body p-10">
			<h1 class="text-xl font-bold mb-2">Choose Organization</h1>
			<p class="text-base-content/60 text-sm mb-8">Select which organization you'd like to work with</p>

			{#if orgContext.loading}
				<p class="text-base-content/50 text-sm my-4">Loading organizations...</p>
			{:else if orgContext.organizations.length === 0}
				<p class="text-base-content/50 text-sm my-4">You're not a member of any organization yet.</p>
				<button class="btn btn-primary" onclick={continuePersonal}>Continue as Personal</button>
			{:else}
				<div class="flex flex-col gap-2 mb-6">
					{#each orgContext.organizations as org}
						<button class="flex items-center justify-between px-5 py-4 bg-base-200/50 border-2 border-base-300 rounded-xl cursor-pointer transition-all text-left w-full hover:border-primary hover:bg-primary/5" onclick={() => selectOrg(org.organizationId!)}>
							<div class="flex flex-col gap-0.5">
								<span class="font-semibold text-base">{org.name}</span>
								<span class="text-xs text-base-content/60 uppercase font-medium tracking-wider">{org.role}</span>
							</div>
							<span class="text-xl text-base-content/40">→</span>
						</button>
					{/each}
				</div>
				<button class="btn btn-ghost btn-sm text-base-content/60" onclick={continuePersonal}>
					Continue without organization
				</button>
			{/if}
		</div>
	</div>
</div>

