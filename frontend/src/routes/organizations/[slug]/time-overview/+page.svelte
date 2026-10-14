<script lang="ts">
	import { getOrgDetail } from '$lib/stores/orgDetail';
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { organizationsApi } from '$lib/apiClient';
	import type { MemberTimeOverviewResponse, OrganizationDetailResponse, TimeEntryResponse } from '$lib/api';
	import { formatHoursDecimal, formatTime, formatDateShort, formatWeekLabel, formatDuration, formatNumber, roleLabel } from '$lib/utils/formatters';
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';
	import { getWeekRange } from '$lib/utils/dateHelpers';
	import { fetchAllPages } from '$lib/utils/pagination';
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
			const org = await getOrgDetail(orgSlug);
			orgName = org.name ?? '';
		} catch (err) {
			// Only the heading uses the name; the overview below reports its own errors
			if (getErrorStatus(err) !== 403) toast.error(err, t('timeOverview.orgFailed'));
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
				error = t('timeOverview.noPermission');
			} else {
				error = extractErrorMessage(err, t('timeOverview.loadFailed'));
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
			memberEntries = await fetchAllPages((limit, offset) =>
				organizationsApi.apiV1OrganizationsSlugMemberEntriesMemberIdGet(orgSlug, userId, from, to, limit, offset).then((r) => r.data));
		} catch (err) {
			memberEntries = [];
			entriesError = extractErrorMessage(err, t('timeOverview.entriesFailed'));
		} finally {
			entriesLoading = false;
		}
	}

</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: orgName ? `${t('timeOverview.pageTitle')} – ${orgName}` : t('timeOverview.pageTitle') })}</title>
</svelte:head>

<div class="max-w-5xl mx-auto px-4 sm:px-6 pb-6">
	<a href="/organizations/{orgSlug}" class="text-base-content/70 text-sm inline-block mb-3 no-underline hover:text-primary">&larr; {t('timeOverview.back', { org: orgName || t('timeOverview.fallbackOrg') })}</a>

	<h1 class="text-xl font-bold mb-1">{t('timeOverview.title')}</h1>
	{#if orgName}<p class="text-base-content/70 text-sm mb-6">{orgName}</p>{/if}

	{#if error}
		<div class="alert alert-error">{error}</div>
	{:else}
		<!-- Week nav -->
		<div class="flex items-center justify-center gap-4 mb-6">
			<button type="button" class="btn btn-ghost btn-sm btn-square border border-base-300" onclick={() => changeWeek(-1)} aria-label={t('timeOverview.prevWeek')} title={t('timeOverview.prevWeek')}>&lsaquo;</button>
			<span class="font-semibold text-base text-center" aria-live="polite">{formatWeekLabel(weekRange)}</span>
			<button type="button" class="btn btn-ghost btn-sm btn-square border border-base-300" onclick={() => changeWeek(1)} disabled={weekOffset >= 0} aria-label={t('timeOverview.nextWeek')} title={t('timeOverview.nextWeek')}>&rsaquo;</button>
		</div>

		{#if loading}
			<p class="text-base-content/70 flex items-center gap-2"><span class="loading loading-spinner loading-xs"></span> {t('common.loading')}</p>
		{:else if members.length === 0}
			<p class="text-base-content/70">{t('timeOverview.noMembers')}</p>
		{:else}
			<div class="bg-base-100 border border-base-300 rounded-xl overflow-x-auto">
				<div class="min-w-[40rem]">
				<div class="grid grid-cols-[2fr_0.7fr_0.7fr_0.8fr_0.8fr_0.6fr_0.8fr] gap-2 px-4 py-3 bg-base-200/50 text-[0.6875rem] font-bold text-base-content/70 uppercase tracking-wider border-b border-base-300">
					<span class="overflow-hidden">{t('timeOverview.cols.member')}</span>
					<span class="text-center">{t('timeOverview.cols.role')}</span>
					<span class="text-center">{t('timeOverview.cols.target')}</span>
					<span class="text-center">{t('timeOverview.cols.tracked')}</span>
					<span class="text-center">{t('timeOverview.cols.net')}</span>
					<span class="text-center">{t('timeOverview.cols.entries')}</span>
					<span class="text-center">{t('timeOverview.cols.status')}</span>
				</div>

				{#each members as member}
					{@const targetMinutes = member.weeklyWorkHours ? member.weeklyWorkHours * 60 : 0}
					{@const pct = targetMinutes > 0 ? Math.round(((member.netTrackedMinutes ?? 0) / targetMinutes) * 100) : 0}
					<button type="button" class="grid grid-cols-[2fr_0.7fr_0.7fr_0.8fr_0.8fr_0.6fr_0.8fr] gap-2 px-4 py-3 border-b border-base-200 bg-base-100 w-full text-left cursor-pointer transition-colors text-sm items-center hover:bg-base-200/50" aria-expanded={expandedUserId === member.userId} onclick={() => toggleMemberDetail(member.userId!)}>
						<span class="overflow-hidden">
							<span class="block font-semibold text-sm">{member.firstName} {member.lastName}</span>
							{#if member.email}<span class="block text-[0.6875rem] text-base-content/70 overflow-hidden text-ellipsis whitespace-nowrap">{member.email}</span>{/if}
						</span>
						<span class="text-center">
							{#if member.role}<span class="badge badge-sm {member.role === 'Owner' ? 'badge-warning' : member.role === 'Admin' ? 'badge-info' : 'badge-ghost'}">{roleLabel(member.role)}</span>{/if}
						</span>
						<span class="text-center">
							{member.weeklyWorkHours ? t('common.hoursShort', { value: formatNumber(member.weeklyWorkHours) }) : '-'}
						</span>
						<span class="text-center">{formatHoursDecimal(member.totalTrackedMinutes ?? 0)}</span>
						<span class="text-center">{formatHoursDecimal(member.netTrackedMinutes ?? 0)}</span>
						<span class="text-center">
							{member.entryCount}
							{#if (member.failureEntryCount ?? 0) > 0}
								<span class="badge badge-error badge-xs ml-1" title={t('timeOverview.toFixTitle')}>{t('timeOverview.toFix', { count: member.failureEntryCount ?? 0 })}</span>
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
								<span class="text-base-content/70">-</span>
							{/if}
						</span>
					</button>

					{#if expandedUserId === member.userId}
						<div class="px-5 py-3 bg-base-200/50 border-b border-base-300">
							{#if entriesLoading}
								<p class="text-base-content/70 flex items-center gap-2"><span class="loading loading-spinner loading-xs"></span> {t('common.loading')}</p>
							{:else if entriesError}
								<div class="alert alert-error text-sm py-2">{entriesError}</div>
							{:else if memberEntries.length === 0}
								<p class="text-base-content/70">{t('timeOverview.noEntries')}</p>
							{:else}
								<div class="flex flex-col gap-1.5">
									{#each memberEntries as entry}
										<div class="flex items-center gap-3 text-sm px-2 py-1.5 bg-base-100 rounded-md border border-base-300">
											<span class="text-base-content/70 min-w-[56px] text-xs whitespace-nowrap">{formatDateShort(entry.startTime!)}</span>
											<span class="text-base-content min-w-[100px] whitespace-nowrap">
												{formatTime(entry.startTime!)}{entry.endTime ? ` – ${formatTime(entry.endTime!)}` : ''}
											</span>
											<span class="text-base-content/70 flex-1 overflow-hidden text-ellipsis whitespace-nowrap">{entry.description || ''}</span>
											<span class="font-semibold min-w-[56px] text-right">
												{entry.isRunning ? t('timeOverview.running') : formatDuration(entry.netDurationMinutes ?? entry.durationMinutes ?? 0)}
											</span>
											{#if (entry.pauseDurationMinutes ?? 0) > 0}
												<span class="text-[0.6875rem] text-warning bg-warning/10 px-1.5 rounded-full">{t('timeOverview.pause', { minutes: entry.pauseDurationMinutes ?? 0 })}</span>
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

