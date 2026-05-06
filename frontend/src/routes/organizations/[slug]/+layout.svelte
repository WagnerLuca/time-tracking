<script lang="ts">
	import { page } from '$app/stores';
	import { orgContext } from '$lib/stores/orgContext.svelte';

	let { children } = $props();

	const slug = $derived($page.params.slug ?? '');
	const role = $derived(orgContext.selectedOrg?.role);
	const isAdmin = $derived(role === 'Owner' || role === 'Admin');
	const currentPath = $derived($page.url.pathname);

	// Hide tabs on nested routes like /members/[id]
	const showTabs = $derived(
		currentPath === `/organizations/${slug}` ||
		currentPath === `/organizations/${slug}/members` ||
		currentPath === `/organizations/${slug}/absences` ||
		currentPath === `/organizations/${slug}/holidays` ||
		currentPath === `/organizations/${slug}/settings`
	);

	function tabClass(path: string) {
		const base = `/organizations/${slug}`;
		const fullPath = path ? `${base}/${path}` : base;
		const active = path ? currentPath === fullPath : currentPath === base;
		return `tab ${active ? 'tab-active' : ''}`;
	}
</script>

{#if showTabs}
	<div class="max-w-5xl mx-auto px-6 pt-6 pb-0">
		<a href="/organizations" class="text-base-content/60 no-underline text-sm inline-block mb-3 hover:text-primary">&larr; Back to Organizations</a>

		<div class="tabs tabs-bordered">
			<a href="/organizations/{slug}" class={tabClass('')}>
				<svg class="w-4.5 h-4.5 shrink-0" viewBox="0 0 20 20" fill="currentColor"><path d="M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z"/></svg>
				My Schedule
			</a>
			<a href="/organizations/{slug}/members" class={tabClass('members')}>
				<svg class="w-4.5 h-4.5 shrink-0" viewBox="0 0 20 20" fill="currentColor"><path d="M13 6a3 3 0 11-6 0 3 3 0 016 0zM18 8a2 2 0 11-4 0 2 2 0 014 0zM14 15a4 4 0 00-8 0v3h8v-3zM6 8a2 2 0 11-4 0 2 2 0 014 0zM16 18v-3a5.972 5.972 0 00-.75-2.906A3.005 3.005 0 0119 15v3h-3zM4.75 12.094A5.973 5.973 0 004 15v3H1v-3a3 3 0 013.75-2.906z"/></svg>
				Members
			</a>
			{#if isAdmin}
				<a href="/organizations/{slug}/absences" class={tabClass('absences')}>
					<svg class="w-4.5 h-4.5 shrink-0" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z"/></svg>
					Absences
				</a>
			{/if}
			<a href="/organizations/{slug}/holidays" class={tabClass('holidays')}>
				<svg class="w-4.5 h-4.5 shrink-0" viewBox="0 0 20 20" fill="currentColor"><path d="M5 3a2 2 0 00-2 2v2a2 2 0 002 2h2a2 2 0 002-2V5a2 2 0 00-2-2H5zM5 11a2 2 0 00-2 2v2a2 2 0 002 2h2a2 2 0 002-2v-2a2 2 0 00-2-2H5zM11 5a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2V5zM14 11a1 1 0 011 1v1h1a1 1 0 110 2h-1v1a1 1 0 11-2 0v-1h-1a1 1 0 110-2h1v-1a1 1 0 011-1z"/></svg>
				Holidays
			</a>
			<a href="/organizations/{slug}/settings" class={tabClass('settings')}>
				<svg class="w-4.5 h-4.5 shrink-0" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M11.49 3.17c-.38-1.56-2.6-1.56-2.98 0a1.532 1.532 0 01-2.286.948c-1.372-.836-2.942.734-2.106 2.106.54.886.061 2.042-.947 2.287-1.561.379-1.561 2.6 0 2.978a1.532 1.532 0 01.947 2.287c-.836 1.372.734 2.942 2.106 2.106a1.532 1.532 0 012.287.947c.379 1.561 2.6 1.561 2.978 0a1.533 1.533 0 012.287-.947c1.372.836 2.942-.734 2.106-2.106a1.533 1.533 0 01.947-2.287c1.561-.379 1.561-2.6 0-2.978a1.532 1.532 0 01-.947-2.287c.836-1.372-.734-2.942-2.106-2.106a1.532 1.532 0 01-2.287-.947zM10 13a3 3 0 100-6 3 3 0 000 6z" clip-rule="evenodd"/></svg>
				Settings
			</a>
		</div>
	</div>
{/if}

{@render children()}
