<script lang="ts">
	import { page } from '$app/state';

	let { children } = $props();

	const slug = $derived(page.params.slug ?? '');
	const currentPath = $derived(page.url.pathname);

	const TAB_PATHS = ['', 'members', 'groups', 'absences', 'holidays', 'settings'];

	function tabHref(path: string) {
		return path ? `/organizations/${slug}/${path}` : `/organizations/${slug}`;
	}

	// Hide tabs on nested routes like /members/[id]
	const showTabs = $derived(TAB_PATHS.some((p) => currentPath === tabHref(p)));
</script>

{#if showTabs}
	<div class="max-w-5xl mx-auto px-4 sm:px-6 pt-6 pb-0">
		<a href="/organizations" class="text-base-content/60 no-underline text-sm inline-block mb-3 hover:text-primary">&larr; Back to Organizations</a>

		<nav class="overflow-x-auto -mx-4 px-4 sm:mx-0 sm:px-0" aria-label="Organization">
			<div class="tabs tabs-bordered flex-nowrap w-max min-w-full">
				<a href={tabHref('')} class="tab whitespace-nowrap gap-1.5" class:tab-active={currentPath === tabHref('')} aria-current={currentPath === tabHref('') ? 'page' : undefined}>
					<svg class="w-4.5 h-4.5 shrink-0" aria-hidden="true" viewBox="0 0 20 20" fill="currentColor"><path d="M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z"/></svg>
					My Schedule
				</a>
				<a href={tabHref('members')} class="tab whitespace-nowrap gap-1.5" class:tab-active={currentPath === tabHref('members')} aria-current={currentPath === tabHref('members') ? 'page' : undefined}>
					<svg class="w-4.5 h-4.5 shrink-0" aria-hidden="true" viewBox="0 0 20 20" fill="currentColor"><path d="M13 6a3 3 0 11-6 0 3 3 0 016 0zM18 8a2 2 0 11-4 0 2 2 0 014 0zM14 15a4 4 0 00-8 0v3h8v-3zM6 8a2 2 0 11-4 0 2 2 0 014 0zM16 18v-3a5.972 5.972 0 00-.75-2.906A3.005 3.005 0 0119 15v3h-3zM4.75 12.094A5.973 5.973 0 004 15v3H1v-3a3 3 0 013.75-2.906z"/></svg>
					Members
				</a>
				<a href={tabHref('groups')} class="tab whitespace-nowrap gap-1.5" class:tab-active={currentPath === tabHref('groups')} aria-current={currentPath === tabHref('groups') ? 'page' : undefined}>
					<svg class="w-4.5 h-4.5 shrink-0" aria-hidden="true" viewBox="0 0 20 20" fill="currentColor"><path d="M10 9a3 3 0 100-6 3 3 0 000 6zm-7 9a7 7 0 1114 0H3z"/></svg>
					Groups
				</a>
				<a href={tabHref('absences')} class="tab whitespace-nowrap gap-1.5" class:tab-active={currentPath === tabHref('absences')} aria-current={currentPath === tabHref('absences') ? 'page' : undefined}>
					<svg class="w-4.5 h-4.5 shrink-0" aria-hidden="true" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z"/></svg>
					Absences
				</a>
				<a href={tabHref('holidays')} class="tab whitespace-nowrap gap-1.5" class:tab-active={currentPath === tabHref('holidays')} aria-current={currentPath === tabHref('holidays') ? 'page' : undefined}>
					<svg class="w-4.5 h-4.5 shrink-0" aria-hidden="true" viewBox="0 0 20 20" fill="currentColor"><path d="M5 3a2 2 0 00-2 2v2a2 2 0 002 2h2a2 2 0 002-2V5a2 2 0 00-2-2H5zM5 11a2 2 0 00-2 2v2a2 2 0 002 2h2a2 2 0 002-2v-2a2 2 0 00-2-2H5zM11 5a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2V5zM14 11a1 1 0 011 1v1h1a1 1 0 110 2h-1v1a1 1 0 11-2 0v-1h-1a1 1 0 110-2h1v-1a1 1 0 011-1z"/></svg>
					Holidays
				</a>
				<a href={tabHref('settings')} class="tab whitespace-nowrap gap-1.5" class:tab-active={currentPath === tabHref('settings')} aria-current={currentPath === tabHref('settings') ? 'page' : undefined}>
					<svg class="w-4.5 h-4.5 shrink-0" aria-hidden="true" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M11.49 3.17c-.38-1.56-2.6-1.56-2.98 0a1.532 1.532 0 01-2.286.948c-1.372-.836-2.942.734-2.106 2.106.54.886.061 2.042-.947 2.287-1.561.379-1.561 2.6 0 2.978a1.532 1.532 0 01.947 2.287c-.836 1.372.734 2.942 2.106 2.106a1.532 1.532 0 012.287.947c.379 1.561 2.6 1.561 2.978 0a1.533 1.533 0 012.287-.947c1.372.836 2.942-.734 2.106-2.106a1.533 1.533 0 01.947-2.287c1.561-.379 1.561-2.6 0-2.978a1.532 1.532 0 01-.947-2.287c.836-1.372-.734-2.942-2.106-2.106a1.532 1.532 0 01-2.287-.947zM10 13a3 3 0 100-6 3 3 0 000 6z" clip-rule="evenodd"/></svg>
					Settings
				</a>
			</div>
		</nav>
	</div>
{/if}

{@render children()}
