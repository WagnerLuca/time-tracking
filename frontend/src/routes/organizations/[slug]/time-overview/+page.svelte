<script lang="ts">
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { organizationsApi } from '$lib/apiClient';
	import type { MemberTimeOverviewResponse, OrganizationDetailResponse, TimeEntryResponse } from '$lib/api';
	import { formatHoursDecimal, formatTime, formatDateShort, formatWeekLabel, formatDuration, formatNumber, roleLabel } from '$lib/utils/formatters';
	import { toast } from '$lib/stores/toast.svelte';
	import { getWeekRange } from '$lib/utils/dateHelpers';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';

	const orgSlug = $derived(page.params.slug ?? '');
	let orgName = $state('');
	let members = $state<MemberTimeOverviewResponse[]>([]);
	let loading = $state(true);
	let error = $state('');

	// Week navigation
	let weekOffset = $state(0);
	const weekRange = $derived(getWeekRange(weekOffset));

	// Expanded member detail
	let expandedUserId = $state<number | null>(null);
	let memberEntries = $state<TimeEntryResponse[]>([]);
	let entriesLoading = $state(false);
	let entriesError = $state('');

	// (Re)load whenever the slug in the URL changes, so switching orgs never shows stale data
	$effect(() => {
		if (!orgSlug) return;
		untrack(() => {
			loadOrg();
			loadOverview();
		});
	});

	async function loadOrg() {
		try {
			const { data: org } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			orgName = org.name ?? '';
		} catch (err) {
			// Only the heading uses the name; the overview below reports its own errors
			if (getErrorStatus(err) !== 403) toast.error(err, 'Could not load the organization.');
		}
	}

	async function loadOverview() {
		loading = true;
		error = '';
		try {
			const from = weekRange.start.toISOString();
			const to = weekRange.end.toISOString();
			const { data } = await organizationsApi.apiV1OrganizationsSlugTimeOverviewGet(orgSlug, from, to);
			members = data;
		} catch (err) {
			if (getErrorStatus(err) === 403) {
				error = 'You do not have permission to view time entries. An admin or group setting must grant visibility.';
			} else {
				error = extractErrorMessage(err, 'Failed to load time overview.');
			}
		} finally {
			loading = false;
		}
	}

	function changeWeek(dir: number) {
		weekOffset += dir;
		expandedUserId = null;
		memberEntries = [];
		loadOverview();
	}

	async function toggleMemberDetail(userId: number) {
		if (expandedUserId === userId) {
			expandedUserId = null;
			memberEntries = [];
			return;
		}
		expandedUserId = userId;
		entriesLoading = true;
		entriesError = '';
		try {
			const from = weekRange.start.toISOString();
			const to = weekRange.end.toISOString();
			const { data } = await organizationsApi.apiV1OrganizationsSlugMemberEntriesMemberIdGet(orgSlug, userId, from, to);
			memberEntries = data.items ?? [];
		} catch (err) {
			memberEntries = [];
			entriesError = extractErrorMessage(err, 'Could not load the time entries.');
		} finally {
			entriesLoading = false;
		}
	}

</script>

<svelte:head>
	<title>Team time overview – {orgName || 'Organization'}</title>
</svelte:head>

<div class="max-w-5xl mx-auto px-4 sm:px-6 pb-6">
	<a href="/organizations/{orgSlug}" class="text-base-content/60 text-sm inline-block mb-3 no-underline hover:text-primary">&larr; Back to {orgName || 'Organization'}</a>

	<h1 class="text-xl font-bold mb-1">Team time overview</h1>
	{#if orgName}<p class="text-base-content/60 text-sm mb-6">{orgName}</p>{/if}

	{#if error}
		<div class="alert alert-error">{error}</div>
	{:else}
		<!-- Week nav -->
		<div class="flex items-center justify-center gap-4 mb-6">
			<button type="button" class="btn btn-ghost btn-sm btn-square border border-base-300" onclick={() => changeWeek(-1)} aria-label="Previous week" title="Previous week">&lsaquo;</button>
			<span class="font-semibold text-base text-center" aria-live="polite">{formatWeekLabel(weekRange)}</span>
			<button type="button" class="btn btn-ghost btn-sm btn-square border border-base-300" onclick={() => changeWeek(1)} disabled={weekOffset >= 0} aria-label="Next week" title="Next week">&rsaquo;</button>
		</div>

		{#if loading}
			<p class="text-base-content/50 flex items-center gap-2"><span class="loading loading-spinner loading-xs"></span> Loading…</p>
		{:else if members.length === 0}
			<p class="text-base-content/50">No members found.</p>
		{:else}
			<div class="bg-base-100 border border-base-300 rounded-xl overflow-x-auto">
				<div class="min-w-[40rem]">
				<div class="grid grid-cols-[2fr_0.7fr_0.7fr_0.8fr_0.8fr_0.6fr_0.8fr] gap-2 px-4 py-3 bg-base-200/50 text-[0.6875rem] font-bold text-base-content/60 uppercase tracking-wider border-b border-base-300">
					<span class="overflow-hidden">Member</span>
					<span class="text-center">Role</span>
					<span class="text-center">Target</span>
					<span class="text-center">Tracked</span>
					<span class="text-center">Net</span>
					<span class="text-center">Entries</span>
					<span class="text-center">Status</span>
				</div>

				{#each members as member}
					{@const targetMinutes = member.weeklyWorkHours ? member.weeklyWorkHours * 60 : 0}
					{@const pct = targetMinutes > 0 ? Math.round(((member.netTrackedMinutes ?? 0) / targetMinutes) * 100) : 0}
					<button type="button" class="grid grid-cols-[2fr_0.7fr_0.7fr_0.8fr_0.8fr_0.6fr_0.8fr] gap-2 px-4 py-3 border-b border-base-200 bg-base-100 w-full text-left cursor-pointer transition-colors text-sm items-center hover:bg-base-200/50" aria-expanded={expandedUserId === member.userId} onclick={() => toggleMemberDetail(member.userId!)}>
						<span class="overflow-hidden">
							<span class="block font-semibold text-sm">{member.firstName} {member.lastName}</span>
							<span class="block text-[0.6875rem] text-base-content/50 overflow-hidden text-ellipsis whitespace-nowrap">{member.email}</span>
						</span>
						<span class="text-center">
							<span class="badge badge-sm {member.role === 'Owner' ? 'badge-warning' : member.role === 'Admin' ? 'badge-info' : 'badge-ghost'}">{roleLabel(member.role)}</span>
						</span>
						<span class="text-center">
							{member.weeklyWorkHours ? `${formatNumber(member.weeklyWorkHours)}h` : '-'}
						</span>
						<span class="text-center">{formatHoursDecimal(member.totalTrackedMinutes ?? 0)}</span>
						<span class="text-center">{formatHoursDecimal(member.netTrackedMinutes ?? 0)}</span>
						<span class="text-center">
							{member.entryCount}
							{#if (member.failureEntryCount ?? 0) > 0}
								<span class="badge badge-error badge-xs ml-1" title="Timers stopped automatically at the business day end. Not counted until the member corrects them.">{member.failureEntryCount} to fix</span>
							{/if}
						</span>
						<span class="text-center">
							{#if targetMinutes > 0}
								{#if pct >= 100}
									<span class="badge badge-success badge-sm">&#10003; {pct}%</span>
								{:else if pct >= 75}
									<span class="badge badge-info badge-sm">{pct}%</span>
								{:else if pct >= 50}
									<span class="badge badge-warning badge-sm">{pct}%</span>
								{:else}
									<span class="badge badge-error badge-sm">{pct}%</span>
								{/if}
							{:else}
								<span class="text-base-content/50">-</span>
							{/if}
						</span>
					</button>

					{#if expandedUserId === member.userId}
						<div class="px-5 py-3 bg-base-200/50 border-b border-base-300">
							{#if entriesLoading}
								<p class="text-base-content/50 flex items-center gap-2"><span class="loading loading-spinner loading-xs"></span> Loading…</p>
							{:else if entriesError}
								<div class="alert alert-error text-sm py-2">{entriesError}</div>
							{:else if memberEntries.length === 0}
								<p class="text-base-content/50">No entries this week.</p>
							{:else}
								<div class="flex flex-col gap-1.5">
									{#each memberEntries as entry}
										<div class="flex items-center gap-3 text-sm px-2 py-1.5 bg-base-100 rounded-md border border-base-300">
											<span class="text-base-content/60 min-w-[56px] text-xs whitespace-nowrap">{formatDateShort(entry.startTime!)}</span>
											<span class="text-base-content min-w-[100px] whitespace-nowrap">
												{formatTime(entry.startTime!)}{entry.endTime ? ` – ${formatTime(entry.endTime!)}` : ''}
											</span>
											<span class="text-base-content/50 flex-1 overflow-hidden text-ellipsis whitespace-nowrap">{entry.description || ''}</span>
											<span class="font-semibold min-w-[56px] text-right">
												{entry.isRunning ? 'Running' : formatDuration(entry.netDurationMinutes ?? entry.durationMinutes ?? 0)}
											</span>
											{#if (entry.pauseDurationMinutes ?? 0) > 0}
												<span class="text-[0.6875rem] text-warning bg-warning/10 px-1.5 rounded-full">-{entry.pauseDurationMinutes}m pause</span>
											{/if}
										</div>
									{/each}
								</div>
							{/if}
						</div>
					{/if}
				{/each}
				</div>
			</div>
		{/if}
	{/if}
</div>

