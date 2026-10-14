<script lang="ts">
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { getOrgDetail } from '$lib/stores/orgDetail';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { t } from '$lib/i18n';
	import type { OrganizationDetailResponse } from '$lib/api';
	import ApiKeysPanel from '$lib/components/ApiKeysPanel.svelte';
	import Skeleton from '$lib/components/Skeleton.svelte';

	const orgSlug = $derived(page.params.slug ?? '');
	let org = $state<OrganizationDetailResponse | null>(null);
	let error = $state('');

	$effect(() => {
		const slug = orgSlug;
		untrack(async () => {
			org = null;
			error = '';
			try {
				org = await getOrgDetail(slug);
			} catch (err) {
				error = extractErrorMessage(err, t('orgSettings.loadOrgFailed'));
			}
		});
	});
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: t('orgs.tabs.terminal') })}</title>
</svelte:head>

<div class="max-w-5xl mx-auto sm:px-6 pb-6">
	{#if error}
		<div class="alert alert-error" role="alert">{error}</div>
	{:else if !org}
		<Skeleton />
	{:else}
		<ApiKeysPanel {orgSlug} members={org.members ?? []} />
	{/if}
</div>
