<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { goto } from '$app/navigation';
	import { auth } from '$lib/stores/auth.svelte';
	import { organizationsApi, workScheduleApi, absenceDayApi } from '$lib/apiClient';
	import type {
		OrganizationDetailResponse,
		OrganizationMemberResponse,
		MemberTimeOverviewResponse,
		TimeEntryResponse,
		WorkScheduleResponse,
		AbsenceDayResponse,
		AbsenceType
	} from '$lib/api';

	let orgSlug = '';
	let memberId = 0;
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
	let absenceSaving = $state(false);
	let absenceError = $state('');

	// Work schedule
	let schedule = $state<WorkScheduleResponse | null>(null);
	let scheduleLoading = $state(false);
	let editingSchedule = $state(false);
	let schedWeeklyHours = $state<number | null>(null);
	let schedDistribute = $state(true);
	let schedMon = $state(0);
	let schedTue = $state(0);
	let schedWed = $state(0);
	let schedThu = $state(0);
	let schedFri = $state(0);
	let scheduleSaving = $state(false);
	let scheduleError = $state('');

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

	// Action feedback
	let actionError = $state('');

	const weekRange = $derived(getWeekRange(weekOffset));

	function getWeekRange(offset: number) {
		const now = new Date();
		const start = new Date(now);
		const dayOfWeek = now.getDay() || 7;
		start.setDate(now.getDate() - dayOfWeek + 1 + offset * 7);
		start.setHours(0, 0, 0, 0);
		const end = new Date(start);
		end.setDate(start.getDate() + 6);
		end.setHours(23, 59, 59, 999);
		return { start, end };
	}

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
			const { data } = await organizationsApi.apiOrganizationsSlugGet(orgSlug);
			org = data;
			member = data.members?.find(m => m.id === memberId) ?? null;
			if (!member) { error = 'Member not found.'; loading = false; return; }
		} catch (e: any) {
			error = e.response?.data?.message || 'Failed to load organization.';
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
			const { data } = await organizationsApi.apiOrganizationsSlugTimeOverviewGet(orgSlug, from, to);
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
			const { data } = await organizationsApi.apiOrganizationsSlugTimeOverviewGet(
				orgSlug,
				'2000-01-01T00:00:00.000Z',
				'2099-12-31T23:59:59.999Z'
			);
			allTimeOverview = data.find(m => m.userId === memberId) ?? null;
		} catch { allTimeOverview = null; }

		// Find the date of the first time entry to compute weeks accurately
		try {
			const { data: allEntries } = await organizationsApi.apiOrganizationsSlugMemberEntriesUserIdGet(
				orgSlug, memberId,
				'2000-01-01T00:00:00.000Z',
				'2099-12-31T23:59:59.999Z'
			);
			if (allEntries.length > 0) {
				const sorted = allEntries
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
			const { data } = await organizationsApi.apiOrganizationsSlugMemberEntriesUserIdGet(orgSlug, memberId, from, to);
			entries = data;
		} catch { entries = []; }
		entriesLoading = false;
	}

	// ── Absences ──
	async function loadAbsences() {
		absencesLoading = true;
		try {
			const { data } = await absenceDayApi.apiOrganizationsSlugAbsencesGet(orgSlug, memberId);
			absences = data;
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
				await absenceDayApi.apiOrganizationsSlugAbsencesAdminPost(orgSlug, {
					userId: memberId,
					date: dateStr,
					type: absenceType as AbsenceType,
					note: absenceNote || undefined
				});
			}
			showAddAbsence = false;
			absenceFromDate = '';
			absenceToDate = '';
			absenceType = 0;
			absenceNote = '';
			loadAbsences();
		} catch (e: any) {
			absenceError = e.response?.data?.message || 'Failed to add absence.';
		}
		absenceSaving = false;
	}

	async function deleteAbsence(id: number) {
		try {
			await absenceDayApi.apiOrganizationsSlugAbsencesIdDelete(orgSlug, id);
			loadAbsences();
		} catch (e: any) {
			actionError = e.response?.data?.message || 'Failed to delete absence.';
		}
	}

	// ── Work Schedule ──
	async function loadSchedule() {
		scheduleLoading = true;
		try {
			const { data } = await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkScheduleGet(orgSlug, memberId);
			schedule = data;
			overtimeHours = data.initialOvertimeHours ?? 0;
		} catch { schedule = null; }
		scheduleLoading = false;
	}

	function startEditSchedule() {
		if (!schedule) return;
		schedWeeklyHours = schedule.weeklyWorkHours ?? null;
		schedDistribute = true;
		schedMon = schedule.targetMon ?? 0;
		schedTue = schedule.targetTue ?? 0;
		schedWed = schedule.targetWed ?? 0;
		schedThu = schedule.targetThu ?? 0;
		schedFri = schedule.targetFri ?? 0;
		editingSchedule = true;
	}

	async function saveSchedule() {
		scheduleSaving = true;
		scheduleError = '';
		try {
			const scheduleId = schedule?.id;
			if (scheduleId && scheduleId > 0) {
				await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkSchedulesIdPut(orgSlug, memberId, scheduleId, {
					weeklyWorkHours: schedWeeklyHours,
					distributeEvenly: schedDistribute,
					targetMon: schedDistribute ? null : schedMon,
					targetTue: schedDistribute ? null : schedTue,
					targetWed: schedDistribute ? null : schedWed,
					targetThu: schedDistribute ? null : schedThu,
					targetFri: schedDistribute ? null : schedFri
				});
			} else {
				const today = new Date();
				const validFrom = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
				await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkSchedulesPost(orgSlug, memberId, {
					validFrom,
					weeklyWorkHours: schedWeeklyHours,
					distributeEvenly: schedDistribute,
					targetMon: schedDistribute ? null : schedMon,
					targetTue: schedDistribute ? null : schedTue,
					targetWed: schedDistribute ? null : schedWed,
					targetThu: schedDistribute ? null : schedThu,
					targetFri: schedDistribute ? null : schedFri
				});
			}
			editingSchedule = false;
			loadSchedule();
		} catch (e: any) {
			scheduleError = e.response?.data?.message || 'Failed to update schedule.';
		}
		scheduleSaving = false;
	}

	// ── Schedule Periods ──
	async function loadPeriods() {
		periodsLoading = true;
		try {
			const { data } = await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkSchedulesGet(orgSlug, memberId);
			periods = data;
		} catch { periods = []; }
		periodsLoading = false;
	}

	async function addPeriod() {
		if (!periodFrom) return;
		periodSaving = true;
		periodError = '';
		try {
			await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkSchedulesPost(orgSlug, memberId, {
				validFrom: new Date(periodFrom).toISOString(),
				validTo: periodTo ? new Date(periodTo).toISOString() : undefined,
				weeklyWorkHours: periodWeekly,
				distributeEvenly: periodDistribute,
				targetMon: periodDistribute ? null : periodMon,
				targetTue: periodDistribute ? null : periodTue,
				targetWed: periodDistribute ? null : periodWed,
				targetThu: periodDistribute ? null : periodThu,
				targetFri: periodDistribute ? null : periodFri
			});
			showAddPeriod = false;
			periodFrom = '';
			periodTo = '';
			periodWeekly = null;
			periodDistribute = true;
			loadPeriods();
		} catch (e: any) {
			periodError = e.response?.data?.message || 'Failed to add period.';
		}
		periodSaving = false;
	}

	async function deletePeriod(periodId: number) {
		try {
			await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkSchedulesIdDelete(orgSlug, memberId, periodId);
			loadPeriods();
		} catch (e: any) {
			actionError = e.response?.data?.message || 'Failed to delete period.';
		}
	}

	// ── Initial Overtime ──
	async function saveOvertime() {
		overtimeSaving = true;
		try {
			await organizationsApi.apiOrganizationsSlugMembersMemberIdInitialOvertimePut(orgSlug, memberId, { initialOvertimeHours: overtimeHours });
			editingOvertime = false;
			loadSchedule();
		} catch (e: any) {
			actionError = e.response?.data?.message || 'Failed to update overtime.';
		}
		overtimeSaving = false;
	}

	// ── Member Management ──
	async function changeMemberRole(newRole: number) {
		try {
			await organizationsApi.apiOrganizationsSlugMembersMemberIdPut(orgSlug, memberId, { role: newRole as any });
			loadAll();
		} catch (e: any) {
			actionError = e.response?.data?.message || 'Failed to change role.';
		}
	}

	async function removeMember() {
		if (!confirm(`Remove ${member?.firstName} ${member?.lastName} from this organization?`)) return;
		try {
			await organizationsApi.apiOrganizationsSlugMembersMemberIdDelete(orgSlug, memberId);
			goto(`/organizations/${orgSlug}`);
		} catch (e: any) {
			actionError = e.response?.data?.message || 'Failed to remove member.';
		}
	}

	// ── Formatters ──
	function formatHours(minutes: number | undefined): string {
		if (!minutes) return '0h';
		return (minutes / 60).toFixed(1) + 'h';
	}

	function formatWeekLabel(range: { start: Date; end: Date }): string {
		const opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric' };
		return `${range.start.toLocaleDateString([], opts)} – ${range.end.toLocaleDateString([], opts)}`;
	}

	function formatTime(iso: string): string {
		return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
	}

	function formatDate(iso: string): string {
		return new Date(iso).toLocaleDateString([], { month: 'short', day: 'numeric', year: 'numeric' });
	}

	function formatDateShort(iso: string): string {
		return new Date(iso).toLocaleDateString([], { month: 'short', day: 'numeric' });
	}

	function formatDuration(minutes?: number): string {
		if (minutes == null || minutes === 0) return '-';
		const h = Math.floor(minutes / 60);
		const m = Math.round(minutes % 60);
		if (h > 0) return `${h}h ${m}m`;
		return `${m}m`;
	}

	function absenceTypeLabel(type: string | null): string {
		if (type === 'SickDay' || type === '0') return 'Sick Day';
		if (type === 'Vacation' || type === '1') return 'Vacation';
		return 'Other';
	}

	function absenceTypeBadge(type: string | null): string {
		if (type === 'SickDay' || type === '0') return 'badge-sick';
		if (type === 'Vacation' || type === '1') return 'badge-vacation';
		return 'badge-other';
	}

	function getInitials(first: string | null, last: string | null): string {
		return ((first?.[0] ?? '') + (last?.[0] ?? '')).toUpperCase() || '?';
	}
</script>

<svelte:head>
	<title>{member ? `${member.firstName} ${member.lastName}` : 'Member'} - {org?.name ?? 'Organization'}</title>
</svelte:head>

<div class="page">
	<!-- Back link -->
	<a href="/organizations/{orgSlug}" class="back-link">
		<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5"/><path d="M12 19l-7-7 7-7"/></svg>
		Back to {org?.name ?? 'Organization'}
	</a>

	{#if loading}
		<div class="loading-container">
			<div class="spinner"></div>
			<p class="muted">Loading member details...</p>
		</div>
	{:else if error}
		<div class="error-msg">{error}</div>
	{:else if member}
		<!-- ═══ Member Header ═══ -->
		<div class="member-header">
			<div class="member-avatar">
				{getInitials(member.firstName, member.lastName)}
			</div>
			<div class="member-info">
				<h1>{member.firstName} {member.lastName}</h1>
				<p class="member-email">{member.email}</p>
				<div class="member-meta">
					<span class="role-badge role-{member.role?.toLowerCase()}">{member.role}</span>
					{#if member.joinedAt}
						<span class="joined">Joined {formatDate(member.joinedAt)}</span>
					{/if}
					{#if isSelf}
						<span class="you-badge">You</span>
					{/if}
				</div>
			</div>

			{#if canEdit && !isSelf}
				<div class="member-actions">
					<select class="role-select" value={member.role === 'Admin' ? 1 : member.role === 'Owner' ? 2 : 0} onchange={(e) => changeMemberRole(parseInt(e.currentTarget.value))}>
						<option value={0}>Member</option>
						<option value={1}>Admin</option>
						{#if myRole === 'Owner'}<option value={2}>Owner</option>{/if}
					</select>
					<button class="btn-danger-sm" onclick={removeMember}>Remove</button>
				</div>
			{/if}
		</div>

		{#if actionError}
			<div class="error-msg">{actionError}</div>
		{/if}

		<!-- ═══ Time Overview ═══ -->
		{#if canViewTime}
		<section class="card">
			<div class="card-header">
				<h2>
					<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/></svg>
					Time Overview
				</h2>
			</div>

			<div class="week-nav">
				<button class="week-btn" onclick={() => changeWeek(-1)}>&lsaquo;</button>
				<span class="week-label">{formatWeekLabel(weekRange)}</span>
				<button class="week-btn" onclick={() => changeWeek(1)} disabled={weekOffset >= 0}>&rsaquo;</button>
			</div>

			{#if overviewLoading}
				<p class="muted">Loading...</p>
			{:else if overview}
				{@const targetMinutes = overview.weeklyWorkHours ? overview.weeklyWorkHours * 60 : 0}
				{@const diff = (overview.netTrackedMinutes ?? 0) - targetMinutes}
				<div class="stats-grid">
					<div class="stat-card">
						<span class="stat-value">{formatHours(overview.totalTrackedMinutes)}</span>
						<span class="stat-label">Total Tracked</span>
					</div>
					<div class="stat-card">
						<span class="stat-value">{formatHours(overview.netTrackedMinutes)}</span>
						<span class="stat-label">Net (after pauses)</span>
					</div>
					<div class="stat-card">
						<span class="stat-value">{overview.weeklyWorkHours ? overview.weeklyWorkHours + 'h' : '-'}</span>
						<span class="stat-label">Weekly Target</span>
					</div>
					<div class="stat-card {diff >= 0 ? 'stat-positive' : 'stat-negative'}">
						<span class="stat-value">{diff >= 0 ? '+' : ''}{formatHours(Math.abs(diff))}</span>
						<span class="stat-label">Overtime</span>
					</div>
					<div class="stat-card">
						<span class="stat-value">{overview.entryCount}</span>
						<span class="stat-label">Entries</span>
					</div>
				</div>

				<button class="toggle-entries" onclick={toggleEntries}>
					{entriesExpanded ? 'Hide' : 'Show'} time entries
					<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class:rotated={entriesExpanded}><path d="M6 9l6 6 6-6"/></svg>
				</button>

				{#if entriesExpanded}
					{#if entriesLoading}
						<p class="muted">Loading entries...</p>
					{:else if entries.length === 0}
						<p class="muted">No entries this week.</p>
					{:else}
						<div class="entries-list">
							{#each entries as entry}
								<div class="entry-row">
									<span class="entry-date">{formatDateShort(entry.startTime!)}</span>
									<span class="entry-time">
										{formatTime(entry.startTime!)}{entry.endTime ? ` – ${formatTime(entry.endTime)}` : ''}
									</span>
									<span class="entry-desc">{entry.description || ''}</span>
									<span class="entry-dur">
										{entry.isRunning ? 'Running' : formatDuration(entry.netDurationMinutes ?? entry.durationMinutes ?? 0)}
									</span>
									{#if (entry.pauseDurationMinutes ?? 0) > 0}
										<span class="entry-pause">-{entry.pauseDurationMinutes}m</span>
									{/if}
								</div>
							{/each}
						</div>
					{/if}
				{/if}
			{:else}
				<p class="muted">No time data available for this week.</p>
			{/if}
		</section>

		<!-- ═══ Cumulative Summary ═══ -->
		<section class="card cumulative-card">
			<div class="card-header">
				<h2>
					<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="23 6 13.5 15.5 8.5 10.5 1 18"/><polyline points="17 6 23 6 23 12"/></svg>
					Cumulative Summary
				</h2>
			</div>

			{#if allTimeLoading}
				<p class="muted">Calculating...</p>
			{:else if allTimeOverview}
				{@const initialOTMinutes = (schedule?.initialOvertimeHours ?? 0) * 60}
				{@const startDate = firstEntryDate ?? (member?.joinedAt ? new Date(member.joinedAt) : new Date())}
				{@const weeksWorked = Math.max(1, (Date.now() - startDate.getTime()) / (7 * 24 * 60 * 60 * 1000))}
				{@const expectedMinutes = (allTimeOverview.weeklyWorkHours ?? 0) * 60 * weeksWorked}
				{@const cumulativeOT = (allTimeOverview.netTrackedMinutes ?? 0) - expectedMinutes + initialOTMinutes}
				<div class="stats-grid">
					<div class="stat-card stat-highlight {cumulativeOT >= 0 ? 'stat-positive' : 'stat-negative'}">
						<span class="stat-value">{cumulativeOT >= 0 ? '+' : ''}{formatHours(Math.abs(cumulativeOT))}</span>
						<span class="stat-label">Cumulative Overtime</span>
					</div>
					<div class="stat-card">
						<span class="stat-value">{formatHours(allTimeOverview.netTrackedMinutes)}</span>
						<span class="stat-label">All-Time Net Tracked</span>
					</div>
					<div class="stat-card">
						<span class="stat-value">{allTimeOverview.entryCount}</span>
						<span class="stat-label">Total Entries</span>
					</div>
					<div class="stat-card">
						<span class="stat-value">{schedule?.initialOvertimeHours ?? 0}h</span>
						<span class="stat-label">Initial Overtime</span>
					</div>
				</div>
			{:else}
				<p class="muted">No cumulative data available.</p>
			{/if}
		</section>
		{/if}

		<!-- ═══ Absences ═══ -->
		{#if isSelf || canEdit}
		<section class="card">
			<div class="card-header">
				<h2>
					<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>
					Absences
				</h2>
				{#if canEdit}
					<button class="btn-sm" onclick={() => showAddAbsence = !showAddAbsence}>
						{showAddAbsence ? 'Cancel' : '+ Add'}
					</button>
				{/if}
			</div>

			{#if showAddAbsence && canEdit}
				<div class="form-inline">
					<div class="date-range-inputs">
						<label class="date-label">From</label>
						<input type="date" bind:value={absenceFromDate} class="input-sm" />
						<label class="date-label">To</label>
						<input type="date" bind:value={absenceToDate} class="input-sm" placeholder="Same day" />
					</div>
					<select bind:value={absenceType} class="input-sm">
						<option value={0}>Sick Day</option>
						<option value={1}>Vacation</option>
						<option value={2}>Other</option>
					</select>
					<input type="text" bind:value={absenceNote} placeholder="Note (optional)" class="input-sm input-grow" />
					<button class="btn-primary-sm" onclick={addAbsence} disabled={absenceSaving || !absenceFromDate}>
						{absenceSaving ? 'Saving...' : 'Add'}
					</button>
				</div>
				<p class="hint">Only workdays (Mon–Fri) will be counted.</p>
				{#if absenceError}<p class="error-inline">{absenceError}</p>{/if}
			{/if}

			{#if absencesLoading}
				<p class="muted">Loading absences...</p>
			{:else if absences.length === 0}
				<p class="muted">No absences recorded.</p>
			{:else}
				<div class="absence-list">
					{#each absences as absence}
						<div class="absence-row">
							<span class="absence-date">{formatDate(absence.date!)}</span>
							<span class="absence-type {absenceTypeBadge(absence.type)}">{absenceTypeLabel(absence.type)}</span>
							{#if absence.note}
								<span class="absence-note">{absence.note}</span>
							{/if}
							{#if canEdit}
								<button class="btn-icon-danger" onclick={() => deleteAbsence(absence.id!)} title="Delete">
									<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 6h18"/><path d="M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2"/></svg>
								</button>
							{/if}
						</div>
					{/each}
				</div>
			{/if}
		</section>
		{/if}

		<!-- ═══ Work Schedule ═══ -->
		<section class="card">
			<div class="card-header">
				<h2>
					<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>
					Work Schedule
				</h2>
				{#if canEdit && !editingSchedule}
					<button class="btn-sm" onclick={startEditSchedule}>Edit</button>
				{/if}
			</div>

			{#if scheduleLoading}
				<p class="muted">Loading schedule...</p>
			{:else if editingSchedule}
				<div class="schedule-form">
					<div class="form-row">
						<label>Weekly Hours</label>
						<input type="number" bind:value={schedWeeklyHours} min="0" max="80" step="0.5" class="input-sm" />
					</div>
					<div class="form-row">
						<label>
							<input type="checkbox" bind:checked={schedDistribute} />
							Distribute evenly (Mon–Fri)
						</label>
					</div>
					{#if !schedDistribute}
						<div class="day-grid">
							<div class="day-input"><label>Mon</label><input type="number" bind:value={schedMon} min="0" max="24" step="0.5" class="input-sm" /></div>
							<div class="day-input"><label>Tue</label><input type="number" bind:value={schedTue} min="0" max="24" step="0.5" class="input-sm" /></div>
							<div class="day-input"><label>Wed</label><input type="number" bind:value={schedWed} min="0" max="24" step="0.5" class="input-sm" /></div>
							<div class="day-input"><label>Thu</label><input type="number" bind:value={schedThu} min="0" max="24" step="0.5" class="input-sm" /></div>
							<div class="day-input"><label>Fri</label><input type="number" bind:value={schedFri} min="0" max="24" step="0.5" class="input-sm" /></div>
						</div>
					{/if}
					{#if scheduleError}<p class="error-inline">{scheduleError}</p>{/if}
					<div class="form-actions">
						<button class="btn-primary-sm" onclick={saveSchedule} disabled={scheduleSaving}>
							{scheduleSaving ? 'Saving...' : 'Save'}
						</button>
						<button class="btn-sm" onclick={() => editingSchedule = false}>Cancel</button>
					</div>
				</div>
			{:else if schedule}
				<div class="schedule-display">
					<div class="sched-row">
						<span class="sched-label">Weekly Hours</span>
						<span class="sched-value">{schedule.weeklyWorkHours ?? '-'}h</span>
					</div>
					<div class="sched-row">
						<span class="sched-label">Daily Targets</span>
						<span class="sched-value days">
							<span>Mon {schedule.targetMon ?? 0}h</span>
							<span>Tue {schedule.targetTue ?? 0}h</span>
							<span>Wed {schedule.targetWed ?? 0}h</span>
							<span>Thu {schedule.targetThu ?? 0}h</span>
							<span>Fri {schedule.targetFri ?? 0}h</span>
						</span>
					</div>
				</div>
			{:else}
				<p class="muted">No schedule configured.</p>
			{/if}
		</section>

		<!-- ═══ Initial Overtime ═══ -->
		<section class="card">
			<div class="card-header">
				<h2>
					<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="23 6 13.5 15.5 8.5 10.5 1 18"/><polyline points="17 6 23 6 23 12"/></svg>
					Initial Overtime
				</h2>
				{#if canEdit && !editingOvertime}
					<button class="btn-sm" onclick={() => editingOvertime = true}>Edit</button>
				{/if}
			</div>

			{#if editingOvertime}
				<div class="form-inline">
					<input type="number" bind:value={overtimeHours} step="0.5" class="input-sm" style="width: 100px;" />
					<span class="muted">hours</span>
					<button class="btn-primary-sm" onclick={saveOvertime} disabled={overtimeSaving}>
						{overtimeSaving ? 'Saving...' : 'Save'}
					</button>
					<button class="btn-sm" onclick={() => editingOvertime = false}>Cancel</button>
				</div>
			{:else}
				<p class="overtime-value">{schedule?.initialOvertimeHours ?? 0}h</p>
				<p class="muted" style="font-size: 0.75rem;">Carry-over overtime from before time tracking started.</p>
			{/if}
		</section>

		<!-- ═══ Schedule Periods ═══ -->
		<section class="card">
			<div class="card-header">
				<h2>
					<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M8 2v4"/><path d="M16 2v4"/><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M3 10h18"/><path d="M8 14h.01"/><path d="M12 14h.01"/><path d="M16 14h.01"/></svg>
					Schedule Periods
				</h2>
				{#if canEdit}
					<button class="btn-sm" onclick={() => showAddPeriod = !showAddPeriod}>
						{showAddPeriod ? 'Cancel' : '+ Add'}
					</button>
				{/if}
			</div>

			{#if showAddPeriod && canEdit}
				<div class="period-form">
					<div class="form-row-pair">
						<div class="form-row">
							<label>Valid From</label>
							<input type="date" bind:value={periodFrom} class="input-sm" />
						</div>
						<div class="form-row">
							<label>Valid To (optional)</label>
							<input type="date" bind:value={periodTo} class="input-sm" />
						</div>
					</div>
					<div class="form-row">
						<label>Weekly Hours</label>
						<input type="number" bind:value={periodWeekly} min="0" max="80" step="0.5" class="input-sm" style="width: 120px;" />
					</div>
					<div class="form-row">
						<label>
							<input type="checkbox" bind:checked={periodDistribute} />
							Distribute evenly
						</label>
					</div>
					{#if !periodDistribute}
						<div class="day-grid">
							<div class="day-input"><label>Mon</label><input type="number" bind:value={periodMon} min="0" max="24" step="0.5" class="input-sm" /></div>
							<div class="day-input"><label>Tue</label><input type="number" bind:value={periodTue} min="0" max="24" step="0.5" class="input-sm" /></div>
							<div class="day-input"><label>Wed</label><input type="number" bind:value={periodWed} min="0" max="24" step="0.5" class="input-sm" /></div>
							<div class="day-input"><label>Thu</label><input type="number" bind:value={periodThu} min="0" max="24" step="0.5" class="input-sm" /></div>
							<div class="day-input"><label>Fri</label><input type="number" bind:value={periodFri} min="0" max="24" step="0.5" class="input-sm" /></div>
						</div>
					{/if}
					{#if periodError}<p class="error-inline">{periodError}</p>{/if}
					<div class="form-actions">
						<button class="btn-primary-sm" onclick={addPeriod} disabled={periodSaving || !periodFrom}>
							{periodSaving ? 'Saving...' : 'Add Period'}
						</button>
					</div>
				</div>
			{/if}

			{#if periodsLoading}
				<p class="muted">Loading periods...</p>
			{:else if periods.length === 0}
				<p class="muted">No schedule periods defined.</p>
			{:else}
				<div class="periods-list">
					{#each periods as period}
						<div class="period-row">
							<div class="period-dates">
								<span class="period-from">{formatDate(period.validFrom!)}</span>
								<span class="period-arrow">→</span>
								<span class="period-to">{period.validTo ? formatDate(period.validTo) : 'Ongoing'}</span>
							</div>
							<span class="period-hours">{period.weeklyWorkHours ?? '-'}h/week</span>
							{#if canEdit}
								<button class="btn-icon-danger" onclick={() => deletePeriod(period.id!)} title="Delete">
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

<style>
	.page {
		max-width: 800px;
		margin: 0 auto;
		padding: 2rem 1.5rem;
	}

	/* Back link */
	.back-link {
		display: inline-flex;
		align-items: center;
		gap: 0.375rem;
		color: #6b7280;
		text-decoration: none;
		font-size: 0.875rem;
		margin-bottom: 1.5rem;
		transition: color 0.15s;
	}
	.back-link:hover { color: #3b82f6; }

	/* Loading */
	.loading-container {
		text-align: center;
		padding: 3rem 0;
	}

	.spinner {
		width: 32px;
		height: 32px;
		border: 3px solid #e5e7eb;
		border-top-color: #3b82f6;
		border-radius: 50%;
		animation: spin 0.6s linear infinite;
		margin: 0 auto 1rem;
	}
	@keyframes spin { to { transform: rotate(360deg); } }

	.muted { color: #9ca3af; font-size: 0.875rem; }

	.error-msg {
		color: #dc2626;
		background: #fef2f2;
		padding: 0.75rem 1rem;
		border-radius: 8px;
		border-left: 3px solid #dc2626;
		margin-bottom: 1rem;
		font-size: 0.875rem;
	}

	.error-inline {
		color: #dc2626;
		font-size: 0.8125rem;
		margin: 0.25rem 0 0;
	}

	/* ═══ Member Header ═══ */
	.member-header {
		display: flex;
		align-items: center;
		gap: 1.25rem;
		margin-bottom: 2rem;
		padding-bottom: 1.5rem;
		border-bottom: 1px solid #e5e7eb;
	}

	.member-avatar {
		width: 64px;
		height: 64px;
		border-radius: 50%;
		background: linear-gradient(135deg, #3b82f6, #8b5cf6);
		color: white;
		display: flex;
		align-items: center;
		justify-content: center;
		font-size: 1.375rem;
		font-weight: 700;
		flex-shrink: 0;
	}

	.member-info { flex: 1; }
	.member-info h1 {
		margin: 0;
		font-size: 1.5rem;
		color: #1a1a2e;
		font-weight: 700;
	}

	.member-email {
		color: #6b7280;
		font-size: 0.875rem;
		margin: 0.125rem 0 0.5rem;
	}

	.member-meta {
		display: flex;
		align-items: center;
		gap: 0.625rem;
		flex-wrap: wrap;
	}

	.role-badge {
		font-size: 0.6875rem;
		font-weight: 600;
		padding: 0.125rem 0.5rem;
		border-radius: 999px;
		text-transform: uppercase;
		letter-spacing: 0.03em;
	}
	.role-owner { background: #fef3c7; color: #92400e; }
	.role-admin { background: #dbeafe; color: #1e40af; }
	.role-member { background: #f3f4f6; color: #4b5563; }

	.joined {
		color: #9ca3af;
		font-size: 0.75rem;
	}

	.you-badge {
		font-size: 0.6875rem;
		font-weight: 600;
		padding: 0.125rem 0.5rem;
		border-radius: 999px;
		background: #dcfce7;
		color: #16a34a;
	}

	.member-actions {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		flex-shrink: 0;
	}

	.role-select {
		font-size: 0.8125rem;
		padding: 0.375rem 0.5rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		background: white;
		color: #374151;
	}

	/* ═══ Cards ═══ */
	.card {
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		padding: 1.25rem 1.5rem;
		margin-bottom: 1.25rem;
	}

	.card-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		margin-bottom: 1rem;
	}

	.card-header h2 {
		margin: 0;
		font-size: 1rem;
		font-weight: 700;
		color: #1a1a2e;
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.card-header h2 svg { color: #6b7280; }

	/* ═══ Week Navigation ═══ */
	.week-nav {
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 1rem;
		margin-bottom: 1rem;
	}

	.week-btn {
		background: none;
		border: 1px solid #e5e7eb;
		width: 30px;
		height: 30px;
		border-radius: 6px;
		font-size: 1.125rem;
		cursor: pointer;
		color: #374151;
		display: flex;
		align-items: center;
		justify-content: center;
	}
	.week-btn:hover:not(:disabled) { background: #f3f4f6; }
	.week-btn:disabled { opacity: 0.3; cursor: not-allowed; }

	.week-label {
		font-weight: 600;
		font-size: 0.9375rem;
		color: #1a1a2e;
	}

	/* ═══ Stats Grid ═══ */
	.stats-grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
		gap: 0.75rem;
		margin-bottom: 1rem;
	}

	.stat-card {
		background: #f9fafb;
		border: 1px solid #f3f4f6;
		border-radius: 8px;
		padding: 0.75rem;
		text-align: center;
	}

	.stat-value {
		display: block;
		font-size: 1.25rem;
		font-weight: 700;
		color: #1a1a2e;
	}

	.stat-label {
		display: block;
		font-size: 0.6875rem;
		color: #9ca3af;
		margin-top: 0.125rem;
		text-transform: uppercase;
		letter-spacing: 0.03em;
	}

	.stat-positive .stat-value { color: #16a34a; }
	.stat-negative .stat-value { color: #dc2626; }

	.stat-highlight {
		border: 2px solid;
	}
	.stat-highlight.stat-positive { border-color: #bbf7d0; background: #f0fdf4; }
	.stat-highlight.stat-negative { border-color: #fecaca; background: #fef2f2; }

	.cumulative-card {
		background: linear-gradient(135deg, #fafbff 0%, #f8fafc 100%);
	}

	/* ═══ Toggle entries ═══ */
	.toggle-entries {
		background: none;
		border: none;
		color: #3b82f6;
		font-size: 0.8125rem;
		cursor: pointer;
		display: flex;
		align-items: center;
		gap: 0.25rem;
		padding: 0;
		font-weight: 500;
	}
	.toggle-entries:hover { color: #2563eb; }
	.toggle-entries svg { transition: transform 0.2s; }
	.toggle-entries .rotated { transform: rotate(180deg); }

	/* ═══ Entries list ═══ */
	.entries-list {
		margin-top: 0.75rem;
		display: flex;
		flex-direction: column;
		gap: 0.375rem;
	}

	.entry-row {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		font-size: 0.8125rem;
		padding: 0.375rem 0.5rem;
		background: #f9fafb;
		border-radius: 6px;
		border: 1px solid #f3f4f6;
	}

	.entry-date {
		color: #6b7280;
		min-width: 56px;
		font-size: 0.75rem;
	}

	.entry-time {
		color: #374151;
		min-width: 100px;
	}

	.entry-desc {
		color: #9ca3af;
		flex: 1;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.entry-dur {
		font-weight: 600;
		color: #374151;
		min-width: 56px;
		text-align: right;
	}

	.entry-pause {
		font-size: 0.6875rem;
		color: #c2410c;
		background: #fff7ed;
		padding: 0 0.375rem;
		border-radius: 999px;
	}

	/* ═══ Absences ═══ */
	.absence-list {
		display: flex;
		flex-direction: column;
		gap: 0.375rem;
	}

	.absence-row {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		padding: 0.5rem 0.625rem;
		background: #f9fafb;
		border-radius: 6px;
		border: 1px solid #f3f4f6;
		font-size: 0.8125rem;
	}

	.absence-date {
		font-weight: 500;
		color: #374151;
		min-width: 100px;
	}

	.absence-type {
		font-size: 0.6875rem;
		font-weight: 600;
		padding: 0.125rem 0.5rem;
		border-radius: 999px;
	}

	.badge-sick { background: #fef2f2; color: #dc2626; }
	.badge-vacation { background: #eff6ff; color: #2563eb; }
	.badge-other { background: #f5f3ff; color: #7c3aed; }

	.absence-note {
		color: #9ca3af;
		flex: 1;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
		font-size: 0.75rem;
	}

	/* ═══ Schedule display ═══ */
	.schedule-display {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}

	.sched-row {
		display: flex;
		align-items: flex-start;
		gap: 1rem;
	}

	.sched-label {
		font-size: 0.8125rem;
		color: #6b7280;
		min-width: 110px;
		font-weight: 500;
	}

	.sched-value {
		font-size: 0.875rem;
		color: #1a1a2e;
		font-weight: 600;
	}

	.sched-value.days {
		display: flex;
		gap: 0.75rem;
		flex-wrap: wrap;
		font-weight: 500;
		font-size: 0.8125rem;
		color: #374151;
	}

	/* ═══ Schedule form ═══ */
	.schedule-form {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}

	.day-grid {
		display: grid;
		grid-template-columns: repeat(5, 1fr);
		gap: 0.5rem;
	}

	.day-input {
		display: flex;
		flex-direction: column;
		gap: 0.25rem;
	}

	.day-input label {
		font-size: 0.6875rem;
		color: #6b7280;
		font-weight: 600;
		text-transform: uppercase;
	}

	/* ═══ Form shared ═══ */
	.form-inline {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		flex-wrap: wrap;
		margin-bottom: 0.75rem;
	}

	.date-range-inputs {
		display: flex;
		align-items: center;
		gap: 0.375rem;
	}

	.date-label {
		font-size: 0.75rem;
		color: #6b7280;
		font-weight: 500;
	}

	.hint {
		font-size: 0.6875rem;
		color: #9ca3af;
		margin: -0.25rem 0 0.5rem;
		font-style: italic;
	}

	.form-row {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.form-row label {
		font-size: 0.8125rem;
		color: #374151;
		font-weight: 500;
	}

	.form-row-pair {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 0.75rem;
	}

	.form-actions {
		display: flex;
		gap: 0.5rem;
	}

	.input-sm {
		font-size: 0.8125rem;
		padding: 0.375rem 0.5rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		background: white;
		color: #374151;
	}
	.input-sm:focus {
		outline: none;
		border-color: #3b82f6;
		box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
	}

	.input-grow { flex: 1; }

	/* ═══ Buttons ═══ */
	.btn-sm {
		font-size: 0.8125rem;
		padding: 0.375rem 0.75rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		background: white;
		color: #374151;
		cursor: pointer;
		font-weight: 500;
		transition: all 0.15s;
	}
	.btn-sm:hover { background: #f3f4f6; border-color: #9ca3af; }

	.btn-primary-sm {
		font-size: 0.8125rem;
		padding: 0.375rem 0.75rem;
		border: none;
		border-radius: 6px;
		background: #3b82f6;
		color: white;
		cursor: pointer;
		font-weight: 500;
		transition: background 0.15s;
	}
	.btn-primary-sm:hover { background: #2563eb; }
	.btn-primary-sm:disabled { opacity: 0.5; cursor: not-allowed; }

	.btn-danger-sm {
		font-size: 0.75rem;
		padding: 0.25rem 0.625rem;
		border: 1px solid #fca5a5;
		border-radius: 6px;
		background: #fef2f2;
		color: #dc2626;
		cursor: pointer;
		font-weight: 600;
		transition: all 0.15s;
	}
	.btn-danger-sm:hover { background: #fee2e2; border-color: #f87171; }

	.btn-icon-danger {
		background: none;
		border: none;
		color: #d1d5db;
		cursor: pointer;
		padding: 0.25rem;
		border-radius: 4px;
		display: flex;
		align-items: center;
		transition: color 0.15s;
	}
	.btn-icon-danger:hover { color: #dc2626; }

	/* ═══ Periods ═══ */
	.period-form {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
		margin-bottom: 1rem;
		padding-bottom: 1rem;
		border-bottom: 1px solid #f3f4f6;
	}

	.periods-list {
		display: flex;
		flex-direction: column;
		gap: 0.375rem;
	}

	.period-row {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		padding: 0.5rem 0.625rem;
		background: #f9fafb;
		border-radius: 6px;
		border: 1px solid #f3f4f6;
		font-size: 0.8125rem;
	}

	.period-dates {
		display: flex;
		align-items: center;
		gap: 0.375rem;
		flex: 1;
	}

	.period-from, .period-to {
		font-weight: 500;
		color: #374151;
	}

	.period-arrow {
		color: #9ca3af;
		font-size: 0.75rem;
	}

	.period-hours {
		font-weight: 600;
		color: #1a1a2e;
		min-width: 65px;
		text-align: right;
	}

	/* ═══ Overtime ═══ */
	.overtime-value {
		font-size: 1.5rem;
		font-weight: 700;
		color: #1a1a2e;
		margin: 0 0 0.25rem;
	}
</style>
