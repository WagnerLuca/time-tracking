<script lang="ts">
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import GroupTags from '$lib/components/GroupTags.svelte';
	import { organizationsApi } from '$lib/apiClient';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import { getWeekRange } from '$lib/utils/dateHelpers';
	import { formatHours, formatNumber, getInitials, roleLabel } from '$lib/utils/formatters';
	import { isOrgAdmin } from '$lib/utils/orgRules';
	import type { OrganizationDetailResponse, MemberTimeOverviewResponse } from '$lib/api';

	const orgSlug = $derived(page.params.slug ?? '');

	let org = $state<OrganizationDetailResponse | null>(null);
	let loading = $state(true);
	let error = $state('');

	let isAdmin = $derived(isOrgAdmin(org?.members?.find((m) => m.id === auth.user?.id)?.role));

	// This week's tracked time per member (only returned where the caller may see it)
	let overviewByUser = $state(new Map<number, MemberTimeOverviewResponse>());

	let groupFilter = $state<number | 'none' | null>(null);

	// (Re)load whenever the slug in the URL changes, so switching orgs never shows stale data
	$effect(() => {
		if (!orgSlug) return;
		untrack(() => loadOrg());
	});

	async function loadOrg() {
		loading = true;
		error = '';
		groupFilter = null;
		overviewByUser = new Map();
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;
		} catch (err) {
			error = getErrorStatus(err) === 404 ? 'Organization not found.' : extractErrorMessage(err, 'Could not load the organization.');
		} finally {
			loading = false;
		}
		if (org) loadTeamOverview();
	}

	async function loadTeamOverview() {
		const week = getWeekRange(0);
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugTimeOverviewGet(orgSlug, week.start.toISOString(), week.end.toISOString());
			overviewByUser = new Map(data.filter((m) => m.userId != null).map((m) => [m.userId!, m]));
		} catch (err) {
			// 403 = time entries are not visible to the caller; the list works without them
			if (getErrorStatus(err) !== 403) toast.error(err, "Could not load this week's hours.");
		}
	}

	let groupOptions = $derived.by(() => {
		const seen = new Map<number, string>();
		for (const m of org?.members ?? []) {
			for (const g of m.groups ?? []) {
				if (g.groupId != null && !seen.has(g.groupId)) seen.set(g.groupId, g.name ?? '');
			}
		}
		return [...seen.entries()].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name));
	});

	let members = $derived.by(() => {
		const all = org?.members ?? [];
		if (groupFilter === null) return all;
		if (groupFilter === 'none') return all.filter((m) => (m.groups ?? []).length === 0);
		return all.filter((m) => (m.groups ?? []).some((g) => g.groupId === groupFilter));
	});

	function roleBadge(role: string | null | undefined): string {
		return role === 'Owner' ? 'badge-warning' : role === 'Admin' ? 'badge-info' : 'badge-ghost';
	}
</script>

<svelte:head>
	<title>Members – {org?.name ?? 'Organization'} – Time Tracking</title>
</svelte:head>

<div class="max-w-5xl mx-auto px-4 sm:px-6 pb-6">
	{#if loading}
		<div class="flex items-center gap-3 justify-center py-12 text-base-content/50"><span class="loading loading-spinner loading-sm"></span><span>Loading…</span></div>
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if org}
		<p class="text-base-content/60 text-sm mt-2 mb-5">
			{isAdmin ? 'Open a member for details, schedule editing and absence history.' : 'Open a member to see their profile.'}
		</p>

		<section>
			<div class="flex items-center justify-between mb-4 flex-wrap gap-2">
				<h2 class="text-xl text-base-content/70 m-0">
					Members ({members.length}{groupFilter !== null ? ` of ${(org.members ?? []).length}` : ''})
				</h2>
				{#if groupOptions.length > 0}
					<label class="flex items-center gap-2 text-sm">
						<span class="sr-only">Filter by group</span>
						<select bind:value={groupFilter} class="select select-bordered select-sm">
							<option value={null}>All groups</option>
							{#each groupOptions as g (g.id)}
								<option value={g.id}>{g.name}</option>
							{/each}
							<option value="none">No group</option>
						</select>
					</label>
				{/if}
			</div>

			<ul class="flex flex-col gap-2 list-none p-0 m-0">
				{#each members as member (member.id)}
					{@const overview = overviewByUser.get(member.id!)}
					<li>
						<a
							class="group flex items-center gap-3 sm:gap-4 p-3.5 bg-base-100 border border-base-300 rounded-xl transition-all hover:border-primary/30 hover:shadow-md no-underline text-base-content"
							href="/organizations/{orgSlug}/members/{member.id}"
						>
							<div class="w-10 h-10 rounded-full bg-gradient-to-br from-primary to-secondary text-primary-content flex items-center justify-center text-sm font-semibold shrink-0" aria-hidden="true">
								{getInitials(member.firstName, member.lastName)}
							</div>
							<div class="flex-1 min-w-0">
								<div class="font-semibold text-[0.9375rem] flex items-center gap-1.5 flex-wrap">
									<span class="break-words">{member.firstName} {member.lastName}</span>
									{#if member.id === auth.user?.id}
										<span class="badge badge-info badge-xs font-semibold">You</span>
									{/if}
									<GroupTags groups={member.groups} />
								</div>
								<div class="text-sm text-base-content/50 mt-0.5 truncate">{member.email}</div>
								<div class="flex gap-x-3 gap-y-1 mt-1.5 flex-wrap items-center text-xs text-base-content/60">
									<span class="badge badge-sm uppercase tracking-wide {roleBadge(member.role)}">{roleLabel(member.role)}</span>
									{#if member.vacationVisible !== false && (member.vacationDaysPerYear ?? 0) > 0}
										<span title="Vacation: {formatNumber(member.vacationDaysUsed ?? 0)}/{formatNumber(member.vacationDaysPerYear ?? 0)} used">
											{formatNumber(member.vacationDaysRemaining ?? member.vacationDaysPerYear ?? 0)} vacation days left
										</span>
									{/if}
									{#if overview}
										<span title="Tracked this week">{formatHours(overview.netTrackedMinutes)} this week</span>
										<span>{formatNumber(overview.entryCount ?? 0)} entries</span>
										{#if overview.weeklyWorkHours}
											<span title="Weekly target">{formatNumber(overview.weeklyWorkHours)} h/week</span>
										{/if}
									{/if}
								</div>
							</div>
							<svg class="w-5 h-5 text-base-content/20 group-hover:text-primary transition-colors shrink-0" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clip-rule="evenodd"/></svg>
						</a>
					</li>
				{/each}
			</ul>
		</section>
	{/if}
</div>
