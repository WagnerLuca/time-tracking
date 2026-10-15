<script lang="ts">
	import { page } from '$app/state';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { isOrgAdmin } from '$lib/utils/orgRules';
	import { t } from '$lib/i18n';

	let { children } = $props();

	const slug = $derived(page.params.slug ?? '');
	const currentPath = $derived(page.url.pathname);
	const membership = $derived(orgContext.organizations.find((o) => o.slug === slug) ?? null);
	const isAdmin = $derived(isOrgAdmin(membership?.role));

	// An org page always works on its own org: a deep link to another org of the user selects it
	$effect(() => {
		if (membership?.organizationId && membership.organizationId !== orgContext.selectedOrgId) orgContext.select(membership.organizationId);
	});

	/** The "Organisation" area: one sub-section per job instead of one long page. */
	const tabs = $derived([
		...(isAdmin ? [{ path: 'general', label: t('orgs.tabs.general') }] : []),
		{ path: '', label: t('orgs.tabs.schedule') },
		{ path: 'members', label: t('orgs.tabs.members') },
		{ path: 'groups', label: t('orgs.tabs.groups') },
		{ path: 'settings', label: isAdmin ? t('orgs.tabs.settings') : t('orgs.tabs.rules') },
		{ path: 'holidays', label: t('orgs.tabs.holidays') },
		...(isAdmin ? [{ path: 'terminal', label: t('orgs.tabs.terminal') }] : []),
		{ path: 'export', label: t('orgs.tabs.export') }
	]);

	function tabHref(path: string) {
		return path ? `/organizations/${slug}/${path}` : `/organizations/${slug}`;
	}

	// Tabs on the section pages only (not on /members/[id] or the team calendar, which has its own nav entry)
	const showTabs = $derived(tabs.some((tab) => currentPath === tabHref(tab.path)));
</script>

{#if showTabs}
	<div class="max-w-5xl mx-auto sm:px-6 pb-2">
		<p class="m-0 mb-2 text-sm text-base-content/70">
			<a href="/organizations" class="no-underline text-base-content/70 hover:text-primary">{t('orgs.tabs.label')}</a> ›
			<span class="font-semibold text-base-content">{membership?.name ?? slug}</span>
		</p>
		<nav class="overflow-x-auto -mx-4 px-4 sm:mx-0 sm:px-0" aria-label={t('orgs.tabs.label')}>
			<div class="tabs flex-nowrap w-max min-w-full">
				{#each tabs as tab (tab.path)}
					<a href={tabHref(tab.path)} class="tab whitespace-nowrap min-h-11" class:tab-active={currentPath === tabHref(tab.path)} aria-current={currentPath === tabHref(tab.path) ? 'page' : undefined}>{tab.label}</a>
				{/each}
			</div>
		</nav>
	</div>
{/if}

{@render children()}
