<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { goto } from '$app/navigation';
	import { auth } from '$lib/stores/auth.svelte';
	import { organizationsApi, workScheduleApi, absenceDayApi } from '$lib/apiClient';
	import { formatHoursDecimal, formatWeekLabel, formatTime, formatDateShort, formatDateFull, formatDuration, absenceTypeLabel, absenceTypeBadge, getInitials } from '$lib/utils/formatters';
	import { getWeekRange } from '$lib/utils/dateHelpers';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import type {
		OrganizationDetailResponse,
		OrganizationMemberResponse,
		MemberTimeOverviewResponse,
		TimeEntryResponse,
		WorkScheduleResponse,
		AbsenceDayResponse,
		AbsenceType
	} from '$lib/api';

	let orgSlug = $state('');
	let memberId = $state(0);
	let org = $state<OrganizationDetailResponse | null>(null);
	let member = $state<OrganizationMemberResponse | null>(null);
	let loading = $state(true);
	let error = $state('');

	// Role info
	let myRole = $derived(org?.members?.find(m => m.id === auth.user?.id)?.role ?? null);
	let canEdit = $derived(myRole === 'Owner' || myRole === 'Admin');
	let isSelf = $derived(auth.user?.id === memberId);
	let canViewTime = $derived(isSelf || (canEdit && (org?.memberTimeEntryVisibility ?? false)));

	// Time overview
	let weekOffset = $state(0);
	let overview = $state<MemberTimeOverviewResponse | null>(null);
	let entries = $state<TimeEntryResponse[]>([]);
	let overviewLoading = $state(false);
	let entriesExpanded = $state(false);
	let entriesLoading = $state(false);
	let firstEntryDate = $state<Date | null>(null);

	// Absences
	let absences = $state<AbsenceDayResponse[]>([]);
	let absencesLoading = $state(false);
	let showAddAbsence = $state(false);
	let absenceFromDate = $state('');
	let absenceToDate = $state('');
	let absenceType = $state<number>(0);
	let absenceNote = $state('');
	let absenceHalfDay = $state(false);
	let absenceSaving = $state(false);
	let absenceError = $state('');

	// Work schedule
	let schedule = $state<WorkScheduleResponse | null>(null);
	let scheduleLoading = $state(false);

	// Schedule periods
	let periods = $state<WorkScheduleResponse[]>([]);
	let periodsLoading = $state(false);
	let showAddPeriod = $state(false);
	let periodFrom = $state('');
	let periodTo = $state('');
	let periodWeekly = $state<number | null>(null);
	let periodDistribute = $state(true);
	let periodMon = $state(0);
	let periodTue = $state(0);
	let periodWed = $state(0);
	let periodThu = $state(0);
	let periodFri = $state(0);
	let periodSaving = $state(false);
	let periodError = $state('');

	// All-time tracked data
	let allTimeOverview = $state<MemberTimeOverviewResponse | null>(null);
	let allTimeLoading = $state(false);

	// Initial overtime
	let editingOvertime = $state(false);
	let overtimeHours = $state(0);
	let overtimeSaving = $state(false);

	// Vacation days
	let editingVacation = $state(false);
	let vacationDays = $state(0);
	let vacationSaving = $state(false);

	// Action feedback
	let actionError = $state('');

	const weekRange = $derived(getWeekRange(weekOffset));

	onMount(() => {
		orgSlug = $page.params.slug ?? '';
		memberId = parseInt($page.params.memberId ?? '0', 10);
		if (!auth.isAuthenticated) { goto('/login'); return; }
		loadAll();
	});

	async function loadAll() {
		loading = true;
		error = '';
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;
			member = data.members?.find(m => m.id === memberId) ?? null;
			if (!member) { error = 'Member not found.'; loading = false; return; }
		} catch (e) {
			error = extractErrorMessage(e, 'Failed to load organization.');
			loading = false;
			return;
		}
		loading = false;

		// Load sections in parallel
		loadOverview();
		loadAllTimeOverview();
		loadAbsences();
		loadSchedule();
		loadPeriods();
	}

	// ── Time Overview ──
	async function loadOverview() {
		overviewLoading = true;
		try {
			const from = weekRange.start.toISOString();
			const to = weekRange.end.toISOString();
			const { data } = await organizationsApi.apiV1OrganizationsSlugTimeOverviewGet(orgSlug, from, to);
			overview = data.find(m => m.userId === memberId) ?? null;
		} catch { overview = null; }
		overviewLoading = false;
	}

	function changeWeek(dir: number) {
		weekOffset += dir;
		entriesExpanded = false;
		entries = [];
		loadOverview();
	}

	async function loadAllTimeOverview() {
		allTimeLoading = true;
		try {
			// Use a very early start date and far future end date to capture all entries
			const { data } = await organizationsApi.apiV1OrganizationsSlugTimeOverviewGet(
				orgSlug,
				'2000-01-01T00:00:00.000Z',
				'2099-12-31T23:59:59.999Z'
			);
			allTimeOverview = data.find(m => m.userId === memberId) ?? null;
		} catch { allTimeOverview = null; }

		// Find the date of the first time entry to compute weeks accurately
		try {
			const { data: allEntries } = await organizationsApi.apiV1OrganizationsSlugMemberEntriesMemberIdGet(
				orgSlug, memberId,
				'2000-01-01T00:00:00.000Z',
				'2099-12-31T23:59:59.999Z'
			);
			const allEntriesItems = allEntries.items ?? [];
			if (allEntriesItems.length > 0) {
				const sorted = allEntriesItems
					.filter((e: any) => e.startTime)
					.map((e: any) => new Date(e.startTime).getTime())
					.sort((a: number, b: number) => a - b);
				if (sorted.length > 0) {
					firstEntryDate = new Date(sorted[0]);
				}
			}
		} catch { /* ignore */ }

		allTimeLoading = false;
	}

	async function toggleEntries() {
		if (entriesExpanded) { entriesExpanded = false; entries = []; return; }
		entriesExpanded = true;
		entriesLoading = true;
		try {
			const from = weekRange.start.toISOString();
			const to = weekRange.end.toISOString();
			const { data } = await organizationsApi.apiV1OrganizationsSlugMemberEntriesMemberIdGet(orgSlug, memberId, from, to);
			entries = data.items ?? [];
		} catch { entries = []; }
		entriesLoading = false;
	}

	// ── Absences ──
	async function loadAbsences() {
		absencesLoading = true;
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsencesGet(orgSlug, memberId);
			absences = data.items ?? [];
		} catch { absences = []; }
		absencesLoading = false;
	}

	async function addAbsence() {
		if (!absenceFromDate) return;
		absenceSaving = true;
		absenceError = '';
		try {
			// Collect workdays in range
			const from = new Date(absenceFromDate);
			const to = absenceToDate ? new Date(absenceToDate) : from;
			const workdays: Date[] = [];
			const current = new Date(from);
			while (current <= to) {
				const day = current.getDay();
				if (day >= 1 && day <= 5) { // Mon-Fri
					workdays.push(new Date(current));
				}
				current.setDate(current.getDate() + 1);
			}
			if (workdays.length === 0) {
				absenceError = 'No workdays in selected range.';
				absenceSaving = false;
				return;
			}
			// Create one absence per workday
			for (const wd of workdays) {
				const dateStr = `${wd.getFullYear()}-${String(wd.getMonth() + 1).padStart(2, '0')}-${String(wd.getDate()).padStart(2, '0')}`;
				await absenceDayApi.apiV1OrganizationsSlugAbsencesAdminPost(orgSlug, {
					userId: memberId,
					date: dateStr,
					type: absenceType as AbsenceType,
					isHalfDay: absenceHalfDay,
					note: absenceNote || undefined
				});
			}
			showAddAbsence = false;
			absenceFromDate = '';
			absenceToDate = '';
			absenceType = 0;
			absenceNote = '';
			absenceHalfDay = false;
			loadAbsences();
		} catch (e) {
			absenceError = extractErrorMessage(e, 'Failed to add absence.');
		}
		absenceSaving = false;
	}

	async function deleteAbsence(id: number) {
		try {
			await absenceDayApi.apiV1OrganizationsSlugAbsencesIdDelete(orgSlug, id);
			loadAbsences();
		} catch (e) {
			actionError = extractErrorMessage(e, 'Failed to delete absence.');
		}
	}

	// ── Work Schedule ──
	async function loadSchedule() {
		scheduleLoading = true;
		try {
			const { data } = await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkScheduleGet(orgSlug, memberId);
			schedule = data;
			overtimeHours = data.initialOvertimeHours ?? 0;
		} catch { schedule = null; }
		scheduleLoading = false;
	}

	// ── Schedule Periods ──
	function normalizeDateOnly(value: string): string {
		return value?.split('T')[0] ?? '';
	}

	async function loadPeriods() {
		periodsLoading = true;
		try {
			const { data } = await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkSchedulesGet(orgSlug, memberId);
			periods = (data as WorkScheduleResponse[]).sort((a, b) => (b.validFrom ?? '').localeCompare(a.validFrom ?? ''));
		} catch { periods = []; }
		periodsLoading = false;
	}

	async function addPeriod() {
		if (!periodFrom) {
			periodError = 'A start date is required.';
			return;
		}

		const validFrom = normalizeDateOnly(periodFrom);
		const validTo = normalizeDateOnly(periodTo);
		if (validTo && validTo < validFrom) {
			periodError = 'Valid to must be on or after valid from.';
			return;
		}

		periodSaving = true;
		periodError = '';
		try {
			const payload = {
				validFrom,
				validTo: validTo || undefined,
				weeklyWorkHours: periodWeekly ?? undefined,
				distributeEvenly: periodDistribute,
				targetMon: periodDistribute ? undefined : periodMon,
				targetTue: periodDistribute ? undefined : periodTue,
				targetWed: periodDistribute ? undefined : periodWed,
				targetThu: periodDistribute ? undefined : periodThu,
				targetFri: periodDistribute ? undefined : periodFri
			};

			const existingPeriod = periods.find((p) => p.validFrom === validFrom);
			if (existingPeriod?.id) {
				await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkSchedulesIdPut(
					orgSlug,
					memberId,
					existingPeriod.id,
					payload
				);
			} else {
				await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkSchedulesPost(orgSlug, memberId, payload);
			}

			showAddPeriod = false;
			periodFrom = '';
			periodTo = '';
			periodWeekly = null;
			periodDistribute = true;
			periodMon = periodTue = periodWed = periodThu = periodFri = 0;
			await loadPeriods();
			await loadSchedule();
		} catch (e) {
			periodError = extractErrorMessage(e, 'Failed to add period.');
		}
		periodSaving = false;
	}

	async function deletePeriod(periodId: number) {
		try {
			await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkSchedulesIdDelete(orgSlug, memberId, periodId);
			await loadPeriods();
			await loadSchedule();
		} catch (e) {
			actionError = extractErrorMessage(e, 'Failed to delete period.');
		}
	}

	// ── Initial Overtime ──
	async function saveOvertime() {
		overtimeSaving = true;
		try {
			await organizationsApi.apiV1OrganizationsSlugMembersMemberIdInitialOvertimePut(orgSlug, memberId, { initialOvertimeHours: overtimeHours });
			editingOvertime = false;
			loadSchedule();
		} catch (e) {
			actionError = extractErrorMessage(e, 'Failed to update overtime.');
		}
		overtimeSaving = false;
	}

	// ── Vacation Days ──
	async function saveVacationDays() {
		vacationSaving = true;
		try {
			await organizationsApi.apiV1OrganizationsSlugMembersMemberIdVacationDaysPut(orgSlug, memberId, { days: vacationDays });
			editingVacation = false;
			loadAll();
		} catch (e) {
			actionError = extractErrorMessage(e, 'Failed to update vacation days.');
		}
		vacationSaving = false;
	}

	// ── Member Management ──
	async function changeMemberRole(newRole: number) {
		try {
			await organizationsApi.apiV1OrganizationsSlugMembersMemberIdPut(orgSlug, memberId, { role: newRole as any });
			loadAll();
		} catch (e) {
			actionError = extractErrorMessage(e, 'Failed to change role.');
		}
	}

	async function removeMember() {
		if (!confirm(`Remove ${member?.firstName} ${member?.lastName} from this organization?`)) return;
		try {
			await organizationsApi.apiV1OrganizationsSlugMembersMemberIdDelete(orgSlug, memberId);
			goto(`/organizations/${orgSlug}`);
		} catch (e) {
			actionError = extractErrorMessage(e, 'Failed to remove member.');
		}
	}


</script>

<svelte:head>
	<title>{member ? `${member.firstName} ${member.lastName}` : 'Member'} - {org?.name ?? 'Organization'}</title>
</svelte:head>

<div class="max-w-3xl mx-auto py-8 px-6">
	<!-- Back link -->
	<a href="/organizations/{orgSlug}" class="inline-flex items-center gap-1.5 text-base-content/60 no-underline text-sm mb-6 transition-colors hover:text-primary">
		<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5"/><path d="M12 19l-7-7 7-7"/></svg>
		Back to {org?.name ?? 'Organization'}
	</a>

	{#if loading}
		<div class="text-center py-12">
			<span class="loading loading-spinner loading-md"></span>
			<p class="text-base-content/40 text-sm">Loading member details...</p>
		</div>
	{:else if error}
		<div class="alert alert-error mb-4 text-sm">{error}</div>
	{:else if member}
		<!-- ═══ Member Header ═══ -->
		<div class="flex items-center gap-5 mb-8 pb-6 border-b border-base-300">
			<div class="w-16 h-16 rounded-full bg-gradient-to-br from-primary to-secondary text-primary-content flex items-center justify-center text-xl font-bold shrink-0">
				{getInitials(member.firstName, member.lastName)}
			</div>
			<div class="flex-1">
				<h1 class="text-2xl font-bold text-base-content m-0">{member.firstName} {member.lastName}</h1>
				<p class="text-base-content/60 text-sm mt-0.5 mb-2">{member.email}</p>
				<div class="flex items-center gap-2.5 flex-wrap">
					<span class="badge badge-sm uppercase tracking-wide {member.role === 'Owner' ? 'badge-warning' : member.role === 'Admin' ? 'badge-info' : 'badge-ghost'}">{member.role}</span>
					{#if member.joinedAt}
						<span class="text-base-content/40 text-xs">Joined {formatDateFull(member.joinedAt)}</span>
					{/if}
					{#if isSelf}
						<span class="badge badge-success badge-xs font-semibold">You</span>
					{/if}
				</div>
			</div>

			{#if canEdit && !isSelf}
				<div class="flex items-center gap-2 shrink-0">
					<select class="select select-bordered select-xs" value={member.role === 'Admin' ? 1 : member.role === 'Owner' ? 2 : 0} onchange={(e) => changeMemberRole(parseInt(e.currentTarget.value))}>
						<option value={0}>Member</option>
						<option value={1}>Admin</option>
						{#if myRole === 'Owner'}<option value={2}>Owner</option>{/if}
					</select>
					<button class="btn btn-outline btn-error btn-xs" onclick={removeMember}>Remove</button>
				</div>
			{/if}
		</div>

		{#if actionError}
			<div class="alert alert-error mb-4 text-sm">{actionError}</div>
		{/if}

		<!-- ═══ Time Overview ═══ -->
		{#if canViewTime}
		<section class="card bg-base-100 border border-base-300 p-5 mb-5">
			<div class="flex items-center justify-between mb-4">
				<h2 class="text-base font-bold text-base-content flex items-center gap-2 m-0">
					<svg class="text-base-content/60" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/></svg>
					Time Overview
				</h2>
			</div>

			<div class="flex items-center justify-center gap-4 mb-4">
				<button class="btn btn-ghost btn-sm btn-square text-lg" onclick={() => changeWeek(-1)}>&lsaquo;</button>
				<span class="font-semibold text-[0.9375rem] text-base-content">{formatWeekLabel(weekRange)}</span>
				<button class="btn btn-ghost btn-sm btn-square text-lg" onclick={() => changeWeek(1)} disabled={weekOffset >= 0}>&rsaquo;</button>
			</div>

			{#if overviewLoading}
				<p class="text-base-content/40 text-sm">Loading...</p>
			{:else if overview}
				{@const targetMinutes = overview.weeklyWorkHours ? overview.weeklyWorkHours * 60 : 0}
				{@const diff = (overview.netTrackedMinutes ?? 0) - targetMinutes}
				<div class="grid grid-cols-[repeat(auto-fit,minmax(120px,1fr))] gap-3 mb-4">
					<div class="bg-base-200/50 border border-base-200 rounded-lg p-3 text-center">
						<span class="block text-xl font-bold text-base-content">{formatHoursDecimal(overview.totalTrackedMinutes)}</span>
						<span class="block text-xs text-base-content/40 mt-0.5 uppercase tracking-wide">Total Tracked</span>
					</div>
					<div class="bg-base-200/50 border border-base-200 rounded-lg p-3 text-center">
						<span class="block text-xl font-bold text-base-content">{formatHoursDecimal(overview.netTrackedMinutes)}</span>
						<span class="block text-xs text-base-content/40 mt-0.5 uppercase tracking-wide">Net (after pauses)</span>
					</div>
					<div class="bg-base-200/50 border border-base-200 rounded-lg p-3 text-center">
						<span class="block text-xl font-bold text-base-content">{overview.weeklyWorkHours ? overview.weeklyWorkHours + 'h' : '-'}</span>
						<span class="block text-xs text-base-content/40 mt-0.5 uppercase tracking-wide">Weekly Target</span>
					</div>
					<div class="bg-base-200/50 border border-base-200 rounded-lg p-3 text-center {diff >= 0 ? 'border-success/30 bg-success/5' : 'border-error/30 bg-error/5'}">
						<span class="block text-xl font-bold {diff >= 0 ? 'text-success' : 'text-error'}">{diff >= 0 ? '+' : ''}{formatHoursDecimal(Math.abs(diff))}</span>
						<span class="block text-xs text-base-content/40 mt-0.5 uppercase tracking-wide">Overtime</span>
					</div>
					<div class="bg-base-200/50 border border-base-200 rounded-lg p-3 text-center">
						<span class="block text-xl font-bold text-base-content">{overview.entryCount}</span>
						<span class="block text-xs text-base-content/40 mt-0.5 uppercase tracking-wide">Entries</span>
					</div>
				</div>

				<button class="btn btn-ghost btn-xs text-primary font-medium gap-1" onclick={toggleEntries}>
					{entriesExpanded ? 'Hide' : 'Show'} time entries
					<svg class="[&.rotated]:rotate-180 transition-transform" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class:rotated={entriesExpanded}><path d="M6 9l6 6 6-6"/></svg>
				</button>

				{#if entriesExpanded}
					{#if entriesLoading}
						<p class="text-base-content/40 text-sm">Loading entries...</p>
					{:else if entries.length === 0}
						<p class="text-base-content/40 text-sm">No entries this week.</p>
					{:else}
						<div class="mt-3 flex flex-col gap-1.5">
							{#each entries as entry}
								<div class="flex items-center gap-3 text-sm py-1.5 px-2 bg-base-200/30 rounded-md border border-base-200">
									<span class="text-base-content/60 min-w-[56px] text-xs">{formatDateShort(entry.startTime!)}</span>
									<span class="text-base-content/70 min-w-[100px]">
										{formatTime(entry.startTime!)}{entry.endTime ? ` – ${formatTime(entry.endTime)}` : ''}
									</span>
									<span class="text-base-content/40 flex-1 overflow-hidden text-ellipsis whitespace-nowrap">{entry.description || ''}</span>
									<span class="font-semibold text-base-content/70 min-w-[56px] text-right">
										{entry.isRunning ? 'Running' : formatDuration(entry.netDurationMinutes ?? entry.durationMinutes ?? 0)}
									</span>
									{#if (entry.pauseDurationMinutes ?? 0) > 0}
										<span class="badge badge-warning badge-xs">-{entry.pauseDurationMinutes}m</span>
									{/if}
								</div>
							{/each}
						</div>
					{/if}
				{/if}
			{:else}
				<p class="text-base-content/40 text-sm">No time data available for this week.</p>
			{/if}
		</section>

		<!-- ═══ Cumulative Summary ═══ -->
		<section class="card bg-base-100 border border-base-300 p-5 mb-5 bg-gradient-to-br from-base-100 to-base-200/50">
			<div class="flex items-center justify-between mb-4">
				<h2 class="text-base font-bold text-base-content flex items-center gap-2 m-0">
					<svg class="text-base-content/60" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="23 6 13.5 15.5 8.5 10.5 1 18"/><polyline points="17 6 23 6 23 12"/></svg>
					Cumulative Summary
				</h2>
			</div>

			{#if allTimeLoading}
				<p class="text-base-content/40 text-sm">Calculating...</p>
			{:else if allTimeOverview}
				{@const initialOTMinutes = (schedule?.initialOvertimeHours ?? 0) * 60}
				{@const startDate = firstEntryDate ?? (member?.joinedAt ? new Date(member.joinedAt) : new Date())}
				{@const weeksWorked = Math.max(1, (Date.now() - startDate.getTime()) / (7 * 24 * 60 * 60 * 1000))}
				{@const expectedMinutes = (allTimeOverview.weeklyWorkHours ?? 0) * 60 * weeksWorked}
				{@const cumulativeOT = (allTimeOverview.netTrackedMinutes ?? 0) - expectedMinutes + initialOTMinutes}
				<div class="grid grid-cols-[repeat(auto-fit,minmax(120px,1fr))] gap-3 mb-4">
					<div class="bg-base-200/50 border rounded-lg p-3 text-center border-2 {cumulativeOT >= 0 ? 'border-success/40 bg-success/5' : 'border-error/40 bg-error/5'}">
						<span class="block text-xl font-bold {cumulativeOT >= 0 ? 'text-success' : 'text-error'}">{cumulativeOT >= 0 ? '+' : ''}{formatHoursDecimal(Math.abs(cumulativeOT))}</span>
						<span class="block text-xs text-base-content/40 mt-0.5 uppercase tracking-wide">Cumulative Overtime</span>
					</div>
					<div class="bg-base-200/50 border border-base-200 rounded-lg p-3 text-center">
						<span class="block text-xl font-bold text-base-content">{formatHoursDecimal(allTimeOverview.netTrackedMinutes)}</span>
						<span class="block text-xs text-base-content/40 mt-0.5 uppercase tracking-wide">All-Time Net Tracked</span>
					</div>
					<div class="bg-base-200/50 border border-base-200 rounded-lg p-3 text-center">
						<span class="block text-xl font-bold text-base-content">{allTimeOverview.entryCount}</span>
						<span class="block text-xs text-base-content/40 mt-0.5 uppercase tracking-wide">Total Entries</span>
					</div>
					<div class="bg-base-200/50 border border-base-200 rounded-lg p-3 text-center">
						<span class="block text-xl font-bold text-base-content">{schedule?.initialOvertimeHours ?? 0}h</span>
						<span class="block text-xs text-base-content/40 mt-0.5 uppercase tracking-wide">Initial Overtime</span>
					</div>
					{#if (member?.vacationDaysPerYear ?? 0) > 0}
						<div class="bg-base-200/50 border border-base-200 rounded-lg p-3 text-center">
							<span class="block text-xl font-bold text-info">{member?.vacationDaysRemaining ?? 0}/{member?.vacationDaysPerYear}</span>
							<span class="block text-xs text-base-content/40 mt-0.5 uppercase tracking-wide">Vacation Days Left</span>
						</div>
					{/if}
				</div>
			{:else}
				<p class="text-base-content/40 text-sm">No cumulative data available.</p>
			{/if}
		</section>
		{/if}

		<!-- ═══ Absences ═══ -->
		{#if isSelf || canEdit}
		<section class="card bg-base-100 border border-base-300 p-5 mb-5">
			<div class="flex items-center justify-between mb-4">
				<h2 class="text-base font-bold text-base-content flex items-center gap-2 m-0">
					<svg class="text-base-content/60" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>
					Absences
				</h2>
				{#if canEdit}
					<button class="btn btn-ghost btn-sm" onclick={() => showAddAbsence = !showAddAbsence}>
						{showAddAbsence ? 'Cancel' : '+ Add'}
					</button>
				{/if}
			</div>

			{#if showAddAbsence && canEdit}
				<div class="flex items-center gap-2 flex-wrap mb-3">
					<div class="flex items-center gap-1.5">
						<label for="absence-from" class="text-xs text-base-content/60 font-medium">From</label>
						<input id="absence-from" type="date" bind:value={absenceFromDate} class="input input-bordered input-sm" />
						<label for="absence-to" class="text-xs text-base-content/60 font-medium">To</label>
						<input id="absence-to" type="date" bind:value={absenceToDate} class="input input-bordered input-sm" placeholder="Same day" />
					</div>
					<select bind:value={absenceType} class="select select-bordered select-sm">
						<option value={0}>Sick Day</option>
						<option value={1}>Vacation</option>
						<option value={2}>Other</option>
					</select>
					<input type="text" bind:value={absenceNote} placeholder="Note (optional)" class="input input-bordered input-sm flex-1" />
					<label class="label cursor-pointer flex items-center gap-1.5 text-sm text-base-content/70">
						<input type="checkbox" class="checkbox checkbox-sm" bind:checked={absenceHalfDay} />
						Half day
					</label>
					<button class="btn btn-primary btn-sm" onclick={addAbsence} disabled={absenceSaving || !absenceFromDate}>
						{absenceSaving ? 'Saving...' : 'Add'}
					</button>
				</div>
				<p class="text-xs text-base-content/40 -mt-1 mb-2 italic">Only workdays (Mon–Fri) will be counted.</p>
				{#if absenceError}<p class="text-error text-sm mt-1">{absenceError}</p>{/if}
			{/if}

			{#if absencesLoading}
				<p class="text-base-content/40 text-sm">Loading absences...</p>
			{:else if absences.length === 0}
				<p class="text-base-content/40 text-sm">No absences recorded.</p>
			{:else}
				{@const absencesByYear = Object.entries(
					absences.reduce<Record<string, typeof absences>>((acc, a) => {
						const year = a.date?.substring(0, 4) ?? 'Unknown';
						(acc[year] ??= []).push(a);
						return acc;
					}, {})
				).sort(([a], [b]) => b.localeCompare(a))}
				{#each absencesByYear as [year, yearAbsences]}
					<div class="mb-4">
						<h3 class="text-sm font-semibold text-base-content/50 mb-2">{year}</h3>
						<div class="flex flex-col gap-1.5">
							{#each yearAbsences as absence}
								<div class="flex items-center gap-3 py-2 px-2.5 bg-base-200/30 rounded-md border border-base-200 text-sm">
									<span class="font-medium text-base-content/70 min-w-[100px]">{formatDateFull(absence.date!)}</span>
									<span class="badge badge-sm {absenceTypeBadge(absence.type) === 'badge-sick' ? 'badge-error' : absenceTypeBadge(absence.type) === 'badge-vacation' ? 'badge-info' : 'badge-accent'}">{absenceTypeLabel(absence.type)}</span>
									{#if absence.isHalfDay}
										<span class="badge badge-warning badge-xs">½ Day</span>
									{/if}
									{#if absence.note}
										<span class="text-base-content/40 flex-1 overflow-hidden text-ellipsis whitespace-nowrap text-xs">{absence.note}</span>
									{/if}
									{#if canEdit}
										<button class="btn btn-ghost btn-xs text-base-content/30 hover:text-error" onclick={() => deleteAbsence(absence.id!)} title="Delete" aria-label="Delete absence">
											<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 6h18"/><path d="M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2"/></svg>
										</button>
									{/if}
								</div>
							{/each}
						</div>
					</div>
				{/each}
			{/if}
		</section>
		{/if}

		<!-- ═══ Work Schedule ═══ -->
		<section class="card bg-base-100 border border-base-300 p-5 mb-5">
			<div class="flex items-center justify-between mb-4">
				<h2 class="text-base font-bold text-base-content flex items-center gap-2 m-0">
					<svg class="text-base-content/60" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>
					Active Schedule
				</h2>
			</div>

			{#if scheduleLoading}
				<p class="text-base-content/40 text-sm">Loading schedule...</p>
			{:else if schedule}
				<div class="flex flex-col gap-3">
					<div class="flex items-start gap-4">
						<span class="text-sm text-base-content/60 min-w-[110px] font-medium">Weekly Hours</span>
						<span class="text-sm text-base-content font-semibold">{schedule.weeklyWorkHours ?? '-'}h</span>
					</div>
					<div class="flex items-start gap-4">
						<span class="text-sm text-base-content/60 min-w-[110px] font-medium">Daily Targets</span>
						<span class="flex gap-3 flex-wrap font-medium text-sm text-base-content/70">
							<span>Mon {schedule.targetMon ?? 0}h</span>
							<span>Tue {schedule.targetTue ?? 0}h</span>
							<span>Wed {schedule.targetWed ?? 0}h</span>
							<span>Thu {schedule.targetThu ?? 0}h</span>
							<span>Fri {schedule.targetFri ?? 0}h</span>
						</span>
					</div>
					<p class="text-xs text-base-content/50">Schedule values are managed through schedule periods below.</p>
				</div>
			{:else}
				<p class="text-base-content/40 text-sm">No schedule configured.</p>
			{/if}
		</section>

		<!-- ═══ Initial Overtime ═══ -->
		<section class="card bg-base-100 border border-base-300 p-5 mb-5">
			<div class="flex items-center justify-between mb-4">
				<h2 class="text-base font-bold text-base-content flex items-center gap-2 m-0">
					<svg class="text-base-content/60" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="23 6 13.5 15.5 8.5 10.5 1 18"/><polyline points="17 6 23 6 23 12"/></svg>
					Initial Overtime
				</h2>
				{#if canEdit && !editingOvertime}
					<button class="btn btn-ghost btn-sm" onclick={() => editingOvertime = true}>Edit</button>
				{/if}
			</div>

			{#if editingOvertime}
				<div class="flex items-center gap-2 flex-wrap mb-3">
					<input type="number" bind:value={overtimeHours} step="0.5" class="input input-bordered input-sm" style="width: 100px;" />
					<span class="text-base-content/40 text-sm">hours</span>
					<button class="btn btn-primary btn-sm" onclick={saveOvertime} disabled={overtimeSaving}>
						{overtimeSaving ? 'Saving...' : 'Save'}
					</button>
					<button class="btn btn-ghost btn-sm" onclick={() => editingOvertime = false}>Cancel</button>
				</div>
			{:else}
				<p class="text-2xl font-bold text-base-content mb-1">{schedule?.initialOvertimeHours ?? 0}h</p>
				<p class="text-base-content/40 text-sm" style="font-size: 0.75rem;">Carry-over overtime from before time tracking started.</p>
			{/if}
		</section>

		<!-- ═══ Vacation Days ═══ -->
		<section class="card bg-base-100 border border-base-300 p-5 mb-5">
			<div class="flex items-center justify-between mb-4">
				<h2 class="text-base font-bold text-base-content flex items-center gap-2 m-0">
					<svg class="text-base-content/60" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/></svg>
					Vacation Days
				</h2>
				{#if canEdit && !editingVacation}
					<button class="btn btn-ghost btn-sm" onclick={() => { vacationDays = member?.vacationDaysPerYear ?? 0; editingVacation = true; }}>Edit</button>
				{/if}
			</div>

			{#if editingVacation}
				<div class="flex items-center gap-2 flex-wrap mb-3">
					<input type="number" bind:value={vacationDays} step="0.5" min="0" max="365" class="input input-bordered input-sm" style="width: 100px;" />
					<span class="text-base-content/40 text-sm">days/year</span>
					<button class="btn btn-primary btn-sm" onclick={saveVacationDays} disabled={vacationSaving}>
						{vacationSaving ? 'Saving...' : 'Save'}
					</button>
					<button class="btn btn-ghost btn-sm" onclick={() => editingVacation = false}>Cancel</button>
				</div>
			{:else}
				{@const allowed = member?.vacationDaysPerYear ?? 0}
				{@const used = member?.vacationDaysUsed ?? 0}
				{@const remaining = member?.vacationDaysRemaining ?? 0}
				{#if allowed > 0}
					<div class="flex gap-6 mb-3">
						<div>
							<span class="text-2xl font-bold text-base-content">{remaining}</span>
							<span class="text-sm text-base-content/50 ml-1">remaining</span>
						</div>
						<div>
							<span class="text-lg font-semibold text-base-content/60">{used}</span>
							<span class="text-sm text-base-content/50 ml-1">used</span>
						</div>
						<div>
							<span class="text-lg font-semibold text-base-content/60">{allowed}</span>
							<span class="text-sm text-base-content/50 ml-1">total</span>
						</div>
					</div>
					<div class="w-full bg-base-200 rounded-full h-2.5">
						<div
							class="h-2.5 rounded-full transition-all {used / allowed > 0.9 ? 'bg-error' : used / allowed > 0.7 ? 'bg-warning' : 'bg-success'}"
							style="width: {Math.min(100, (used / allowed) * 100)}%"
						></div>
					</div>
				{:else}
					<p class="text-base-content/40 text-sm">No vacation allowance set.</p>
				{/if}
			{/if}
		</section>

		<!-- ═══ Schedule Periods ═══ -->
		<section class="card bg-base-100 border border-base-300 p-5 mb-5">
			<div class="flex items-center justify-between mb-4">
				<h2 class="text-base font-bold text-base-content flex items-center gap-2 m-0">
					<svg class="text-base-content/60" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M8 2v4"/><path d="M16 2v4"/><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M3 10h18"/><path d="M8 14h.01"/><path d="M12 14h.01"/><path d="M16 14h.01"/></svg>
					Schedule Periods
				</h2>
				{#if canEdit}
					<button class="btn btn-ghost btn-sm" onclick={() => showAddPeriod = !showAddPeriod}>
						{showAddPeriod ? 'Cancel' : '+ Add'}
					</button>
				{/if}
			</div>

			{#if showAddPeriod && canEdit}
				<div class="flex flex-col gap-3 mb-4 pb-4 border-b border-base-200">
					<div class="grid grid-cols-2 gap-3">
						<div class="flex items-center gap-2">
							<label for="member-period-from" class="text-sm text-base-content/70 font-medium">Valid From</label>
							<input id="member-period-from" type="date" bind:value={periodFrom} class="input input-bordered input-sm" />
						</div>
						<div class="flex items-center gap-2">
							<label for="member-period-to" class="text-sm text-base-content/70 font-medium">Valid To (optional)</label>
							<input id="member-period-to" type="date" min={periodFrom || undefined} bind:value={periodTo} class="input input-bordered input-sm" />
						</div>
					</div>
					<div class="flex items-center gap-2">
						<label for="member-period-weekly" class="text-sm text-base-content/70 font-medium">Weekly Hours</label>
						<input id="member-period-weekly" type="number" bind:value={periodWeekly} min="0" max="80" step="0.5" class="input input-bordered input-sm" style="width: 120px;" />
					</div>
					<div class="flex items-center gap-2">
						<label class="text-sm text-base-content/70 font-medium">
							<input type="checkbox" bind:checked={periodDistribute} />
							Distribute evenly
						</label>
					</div>
					{#if !periodDistribute}
						<div class="grid grid-cols-5 gap-2">
							<div class="flex flex-col gap-1"><label for="member-period-mon" class="text-xs text-base-content/60 font-semibold uppercase">Mon</label><input id="member-period-mon" type="number" bind:value={periodMon} min="0" max="24" step="0.5" class="input input-bordered input-sm" /></div>
							<div class="flex flex-col gap-1"><label for="member-period-tue" class="text-xs text-base-content/60 font-semibold uppercase">Tue</label><input id="member-period-tue" type="number" bind:value={periodTue} min="0" max="24" step="0.5" class="input input-bordered input-sm" /></div>
							<div class="flex flex-col gap-1"><label for="member-period-wed" class="text-xs text-base-content/60 font-semibold uppercase">Wed</label><input id="member-period-wed" type="number" bind:value={periodWed} min="0" max="24" step="0.5" class="input input-bordered input-sm" /></div>
							<div class="flex flex-col gap-1"><label for="member-period-thu" class="text-xs text-base-content/60 font-semibold uppercase">Thu</label><input id="member-period-thu" type="number" bind:value={periodThu} min="0" max="24" step="0.5" class="input input-bordered input-sm" /></div>
							<div class="flex flex-col gap-1"><label for="member-period-fri" class="text-xs text-base-content/60 font-semibold uppercase">Fri</label><input id="member-period-fri" type="number" bind:value={periodFri} min="0" max="24" step="0.5" class="input input-bordered input-sm" /></div>
						</div>
					{/if}
					{#if periodError}<p class="text-error text-sm mt-1">{periodError}</p>{/if}
					<div class="flex gap-2">
						<button class="btn btn-primary btn-sm" onclick={addPeriod} disabled={periodSaving || !periodFrom}>
							{periodSaving ? 'Saving...' : 'Add Period'}
						</button>
					</div>
				</div>
			{/if}

			{#if periodsLoading}
				<p class="text-base-content/40 text-sm">Loading periods...</p>
			{:else if periods.length === 0}
				<p class="text-base-content/40 text-sm">No schedule periods defined.</p>
			{:else}
				<div class="flex flex-col gap-1.5">
					{#each periods as period}
						<div class="flex items-center gap-3 py-2 px-2.5 bg-base-200/30 rounded-md border border-base-200 text-sm">
							<div class="flex items-center gap-1.5 flex-1">
								<span class="font-medium text-base-content/70">{formatDateFull(period.validFrom!)}</span>
								<span class="text-base-content/40 text-xs">→</span>
								<span class="font-medium text-base-content/70">{period.validTo ? formatDateFull(period.validTo) : 'Ongoing'}</span>
							</div>
							<span class="font-semibold text-base-content min-w-[65px] text-right">{period.weeklyWorkHours ?? '-'}h/week</span>
							{#if canEdit}
								<button class="btn btn-ghost btn-xs text-base-content/30 hover:text-error" onclick={() => deletePeriod(period.id!)} title="Delete" aria-label="Delete period">
									<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 6h18"/><path d="M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2"/></svg>
								</button>
							{/if}
						</div>
					{/each}
				</div>
			{/if}
		</section>
	{/if}
</div>
