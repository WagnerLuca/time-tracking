<script lang="ts">
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { goto } from '$app/navigation';
	import { roleLabel } from '$lib/utils/formatters';
	import { t } from '$lib/i18n';

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
	<title>{t('common.pageTitle', { page: t('orgs.select.pageTitle') })}</title>
</svelte:head>

<div class="min-h-[80vh] flex items-center justify-center p-4 sm:p-8">
	<div class="card bg-base-100 shadow-lg w-full max-w-[480px] text-center">
		<div class="card-body p-6 sm:p-10">
			<h1 class="text-xl font-bold mb-2">{t('orgs.select.title')}</h1>
			<p class="text-base-content/70 text-sm mb-8">{t('orgs.select.subtitle')}</p>

			{#if orgContext.loading}
				<p class="text-base-content/70 text-sm my-4">{t('orgs.loading')}</p>
			{:else if orgContext.organizations.length === 0}
				<p class="text-base-content/70 text-sm my-4">{t('orgs.select.none')}</p>
				<div class="flex flex-col gap-2">
					<a href="/organizations" class="btn btn-primary">{t('orgs.select.createOrJoin')}</a>
					<button type="button" class="btn btn-ghost btn-sm text-base-content/70" onclick={continuePersonal}>{t('orgs.select.withoutOrg')}</button>
				</div>
			{:else}
				<div class="flex flex-col gap-2 mb-6">
					{#each orgContext.organizations as org (org.organizationId)}
						<button type="button" class="flex items-center justify-between px-5 py-4 bg-base-200/50 border-2 border-base-300 rounded-xl cursor-pointer transition-[border-color,box-shadow,background-color] text-left w-full hover:border-primary hover:bg-primary/5" onclick={() => selectOrg(org.organizationId!)}>
							<div class="flex flex-col gap-0.5">
								<span class="font-semibold text-base">{org.name}</span>
								<span class="text-xs text-base-content/70 uppercase font-medium tracking-wider">{roleLabel(org.role)}</span>
							</div>
							<span class="text-xl text-base-content/70" aria-hidden="true">→</span>
						</button>
					{/each}
				</div>
				<button type="button" class="btn btn-ghost btn-sm text-base-content/70" onclick={continuePersonal}>
					{t('orgs.select.withoutOrg')}
				</button>
			{/if}
		</div>
	</div>
</div>

