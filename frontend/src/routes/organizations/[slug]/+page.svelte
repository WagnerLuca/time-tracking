<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { goto } from '$app/navigation';
	import { auth } from '$lib/stores/auth.svelte';
	import { organizationsApi, workScheduleApi, absenceDayApi, notificationsApi } from '$lib/apiClient';
	import { absenceTypeLabel, absenceTypeBadge } from '$lib/utils/formatters';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import {
		ruleModeLabel
	} from '$lib/utils/orgRules';
	import type {
		OrganizationDetailResponse,
		UpdateOrganizationRequest,
		WorkScheduleResponse,
		AbsenceDayResponse,
		AbsenceType
	} from '$lib/api';

	let org = $state<OrganizationDetailResponse | null>(null);
	let loading = $state(true);
	let error = $state('');

	// My role in this org
	let myRole = $derived(
		org?.members?.find((m) => m.id === auth.user?.id)?.role ?? null
	);
	let canEdit = $derived(myRole === 'Owner' || myRole === 'Admin');
	let isOwner = $derived(myRole === 'Owner');

	// Edit mode
	let editing = $state(false);
	let editName = $state('');
	let editDescription = $state('');
	let editSlug = $state('');
	let editWebsite = $state('');
	let editError = $state('');
	let editSaving = $state(false);

	// Add member

	// User search for add member dropdown

	// Action feedback
	let actionError = $state('');

	// Tab navigation

	// Team overview data (admin)

	// Work schedule (for member view)
	let workSchedule = $state<WorkScheduleResponse | null>(null);

	// Initial overtime editing (inline)
	let editingMyOvertime = $state(false);
	let myOvertimeHours = $state(0);
	let myOvertimeSaving = $state(false);
	let myOvertimeError = $state('');

	// My vacation days editing (inline)
	let editingMyVacation = $state(false);
	let myVacationDays = $state(0);
	let myVacationSaving = $state(false);
	let myVacationError = $state('');
	let myMember = $derived(org?.members?.find(m => m.id === auth.user?.id) ?? null);

	// Holidays

	// Absences / sick days
	let absences = $state<AbsenceDayResponse[]>([]);
	let absencesLoading = $state(false);
	let absencesLoaded = $state(false);
	let showAddAbsence = $state(false);
	let newAbsenceDate = $state('');
	let newAbsenceToDate = $state('');
	let newAbsenceType = $state(0); // SickDay
	let newAbsenceNote = $state('');
	let newAbsenceHalfDay = $state(false);
	let addingAbsence = $state(false);
	let absenceError = $state('');
	// Admin absence
	let showAdminAddAbsence = $state(false);
	let adminAbsenceUserId = $state<number | null>(null);
	let adminAbsenceDate = $state('');
	let adminAbsenceHalfDay = $state(false);
	let adminAbsenceFilter = $state<number | null>(null); // userId filter
	let calendarYear = $state(new Date().getFullYear());
	// Day-click add-absence dialog
	let dayDialogDate = $state('');
	let dayDialogToDate = $state('');
	let dayDialogUserId = $state<number | null>(null);
	let dayDialogType = $state(1); // default Vacation
	let dayDialogHalfDay = $state(false);
	let dayDialogSaving = $state(false);
	// Absence list year dropdown

	// Schedule periods
	let schedulePeriods = $state<WorkScheduleResponse[]>([]);
	let schedulePeriodsLoading = $state(false);
	let schedulePeriodsLoaded = $state(false);
	let showAddPeriod = $state(false);
	let newPeriodFrom = $state('');
	let newPeriodTo = $state('');
	let newPeriodWeeklyHours = $state<number | null>(null);
	let newPeriodDistributeEvenly = $state(true);
	let newPeriodMon = $state(0);
	let newPeriodTue = $state(0);
	let newPeriodWed = $state(0);
	let newPeriodThu = $state(0);
	let newPeriodFri = $state(0);
	let addingPeriod = $state(false);
	let periodError = $state('');

	let showSettingsChangedBanner = $state(false);
	let settingsNotificationIds: number[] = $state([]);

	let orgSlug = $state('');

	onMount(() => {
		orgSlug = $page.params.slug ?? '';
		loadOrg();
	});

	async function loadOrg() {
		loading = true;
		error = '';
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;

			// Check for unread settings-change notifications from backend
			try {
				const resp = await notificationsApi.apiV1NotificationsGet(true) as any;
				const notifications = resp.data as any[];
				const orgId = org!.id;
				const settingsNotifs = notifications.filter(
					(n: any) => n.type === 'SettingsChanged' && n.organizationId === orgId && !n.isRead
				);
				if (settingsNotifs.length > 0) {
					showSettingsChangedBanner = true;
					settingsNotificationIds = settingsNotifs.map((n: any) => n.id);
				}
			} catch {
				// Ignore notification fetch errors
			}

			// Load work schedule for the current user
			try {
				const { data: ws } = await workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(orgSlug);
				workSchedule = ws;
			} catch {
				workSchedule = null;
			}
			// Auto-load all lazy sections in parallel
			loadAllSections();
		} catch (err) {
			if (getErrorStatus(err) === 404) { error = 'Organization not found.'; }
			else { error = 'Failed to load organization.'; }
		} finally {
			loading = false;
		}
	}

	async function dismissSettingsNotifications() {
		showSettingsChangedBanner = false;
		// Mark all settings-change notifications as read on the backend
		for (const nid of settingsNotificationIds) {
			try {
				await notificationsApi.apiV1NotificationsIdReadPut(nid);
			} catch {
				// Ignore errors
			}
		}
		settingsNotificationIds = [];
	}

	function loadAllSections() {
		loadAbsences();
		loadSchedulePeriods();
	}

	/** Load time overview for current week (admins only, when visibility enabled) */
	/** Reload org data without toggling loading state (avoids scroll-to-top) */
	async function reloadOrg() {
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;
			try {
				const { data: ws } = await workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(orgSlug);
				workSchedule = ws;
			} catch {
				workSchedule = null;
			}
		} catch (err) {
			actionError = extractErrorMessage(err, 'Failed to reload organization.');
		}
	}

	function normalizeDateOnly(value: string): string {
		return value?.split('T')[0] ?? '';
	}

	function startEditMyOvertime() {
		if (!workSchedule) return;
		myOvertimeHours = workSchedule.initialOvertimeHours ?? 0;
		myOvertimeError = '';
		editingMyOvertime = true;
	}

	async function saveMyOvertime() {
		myOvertimeSaving = true;
		myOvertimeError = '';
		try {
			await workScheduleApi.apiV1OrganizationsSlugInitialOvertimePut(orgSlug, { initialOvertimeHours: myOvertimeHours });
			// Reload work schedule
			const { data: ws } = await workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(orgSlug);
			workSchedule = ws;
			editingMyOvertime = false;
		} catch (err) {
			myOvertimeError = extractErrorMessage(err, 'Failed to save overtime.');
		} finally {
			myOvertimeSaving = false;
		}
	}

	function startEditMyVacation() {
		myVacationDays = myMember?.vacationDaysPerYear ?? 0;
		myVacationError = '';
		editingMyVacation = true;
	}

	async function saveMyVacation() {
		myVacationSaving = true;
		myVacationError = '';
		try {
			await organizationsApi.apiV1OrganizationsSlugMembersMemberIdVacationDaysPut(orgSlug, auth.user!.id!, { days: myVacationDays });
			await reloadOrg();
			editingMyVacation = false;
		} catch (err) {
			myVacationError = extractErrorMessage(err, 'Failed to save vacation days.');
		} finally {
			myVacationSaving = false;
		}
	}

	function startEdit() {
		if (!org) return;
		editName = org.name ?? '';
		editDescription = org.description ?? '';
		editSlug = org.slug ?? '';
		editWebsite = org.website ?? '';
		editError = '';
		editing = true;
	}

	function cancelEdit() {
		editing = false;
	}

	async function saveEdit(e: Event) {
		e.preventDefault();
		editError = '';
		editSaving = true;
		try {
			const payload: UpdateOrganizationRequest = {
				name: editName.trim(),
				description: editDescription.trim() || undefined,
				slug: editSlug.trim(),
				website: editWebsite.trim() || undefined
			};
			await organizationsApi.apiV1OrganizationsSlugPut(orgSlug, payload);
			await reloadOrg();
			editing = false;
		} catch (err) {
			editError = extractErrorMessage(err, 'Failed to update organization.');
		} finally {
			editSaving = false;
		}
	}

	async function deleteOrg() {
		if (!confirm('Are you sure you want to delete this organization? This cannot be undone.'))
			return;
		try {
			await organizationsApi.apiV1OrganizationsSlugDelete(orgSlug);
			goto('/organizations');
		} catch (err) {
			actionError = extractErrorMessage(err, 'Failed to delete organization.');
		}
	}

	async function updateMemberRole(userId: number, newRole: number) {
		actionError = '';
		try {
			await organizationsApi.apiV1OrganizationsSlugMembersMemberIdPut(orgSlug, userId, { role: newRole as any });
			await reloadOrg();
		} catch (err) {
			actionError = extractErrorMessage(err, 'Failed to update member role.');
		}
	}

	async function removeMember(userId: number, memberName: string) {
		if (!confirm(`Remove ${memberName} from this organization?`)) return;
		actionError = '';
		try {
			await organizationsApi.apiV1OrganizationsSlugMembersMemberIdDelete(orgSlug, userId);
			await reloadOrg();
		} catch (err) {
			actionError = extractErrorMessage(err, 'Failed to remove member.');
		}
	}

	function roleName(role: number): string {
		switch (role) {
			case 0: return 'Member';
			case 1: return 'Admin';
			case 2: return 'Owner';
			default: return 'Member';
		}
	}

	// â”€â”€ Holidays â”€â”€
	function formatDateDisplay(dateStr?: string): string {
		if (!dateStr) return '';
		try {
			const d = new Date(dateStr + 'T00:00:00');
			return d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });
		} catch { return dateStr; }
	}

	// â”€â”€ Absences â”€â”€
	async function loadAbsences() {
		if (absencesLoaded) return;
		absencesLoading = true;
		absenceError = '';
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsencesGet(orgSlug, undefined, undefined, undefined, 200);
			absences = [...(data.items ?? [])].sort((a, b) => (b.date ?? '').localeCompare(a.date ?? ''));
			absencesLoaded = true;
		} catch {
			absences = [];
		} finally {
			absencesLoading = false;
		}
	}

	async function addAbsence(e: Event) {
		e.preventDefault();
		if (!newAbsenceDate) return;
		addingAbsence = true;
		absenceError = '';
		try {
			const from = new Date(newAbsenceDate);
			const to = newAbsenceToDate ? new Date(newAbsenceToDate) : from;
			const workdays: string[] = [];
			const cursor = new Date(from);
			while (cursor <= to) {
				const dow = cursor.getDay();
				if (dow >= 1 && dow <= 5) {
					workdays.push(`${cursor.getFullYear()}-${String(cursor.getMonth() + 1).padStart(2, '0')}-${String(cursor.getDate()).padStart(2, '0')}`);
				}
				cursor.setDate(cursor.getDate() + 1);
			}
			if (workdays.length === 0) {
				absenceError = 'No workdays in selected range.';
				addingAbsence = false;
				return;
			}
			for (const dateStr of workdays) {
				await absenceDayApi.apiV1OrganizationsSlugAbsencesPost(orgSlug, {
					date: dateStr,
					type: newAbsenceType as AbsenceType,
					isHalfDay: newAbsenceHalfDay,
					note: newAbsenceNote || undefined
				});
			}
			absencesLoaded = false;
			await loadAbsences();
			await reloadOrg();
			showAddAbsence = false;
			newAbsenceDate = '';
			newAbsenceToDate = '';
			newAbsenceType = 0;
			newAbsenceNote = '';
			newAbsenceHalfDay = false;
		} catch (err) {
			absenceError = extractErrorMessage(err, 'Failed to add absence.');
		} finally {
			addingAbsence = false;
		}
	}

	async function deleteAbsence(id: number) {
		if (!confirm('Delete this absence?')) return;
		try {
			await absenceDayApi.apiV1OrganizationsSlugAbsencesIdDelete(orgSlug, id);
			absencesLoaded = false;
			await loadAbsences();
			await reloadOrg();
		} catch (err) {
			absenceError = extractErrorMessage(err, 'Failed to delete absence.');
		}
	}

	// Admin absence
	// â”€â”€ Schedule Periods â”€â”€
	async function loadSchedulePeriods() {
		if (schedulePeriodsLoaded) return;
		schedulePeriodsLoading = true;
		periodError = '';
		try {
			const { data } = await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesGet(orgSlug);
			schedulePeriods = (data as WorkScheduleResponse[]).sort((a, b) => (b.validFrom ?? '').localeCompare(a.validFrom ?? ''));
			schedulePeriodsLoaded = true;
		} catch {
			schedulePeriods = [];
		} finally {
			schedulePeriodsLoading = false;
		}
	}

	// Calendar helpers
	/** Group per-user absences of same type into consecutive-day spans */
	/** Color palette for distinguishing different users' vacations */
	const userColorPalette = [
		{ bg: 'bg-sky-200/60 border-sky-400/60', text: 'text-sky-900' },
		{ bg: 'bg-violet-200/60 border-violet-400/60', text: 'text-violet-900' },
		{ bg: 'bg-teal-200/60 border-teal-400/60', text: 'text-teal-900' },
		{ bg: 'bg-amber-200/60 border-amber-400/60', text: 'text-amber-900' },
		{ bg: 'bg-rose-200/60 border-rose-400/60', text: 'text-rose-900' },
		{ bg: 'bg-emerald-200/60 border-emerald-400/60', text: 'text-emerald-900' },
		{ bg: 'bg-indigo-200/60 border-indigo-400/60', text: 'text-indigo-900' },
		{ bg: 'bg-orange-200/60 border-orange-400/60', text: 'text-orange-900' },
		{ bg: 'bg-cyan-200/60 border-cyan-400/60', text: 'text-cyan-900' },
		{ bg: 'bg-fuchsia-200/60 border-fuchsia-400/60', text: 'text-fuchsia-900' },
		{ bg: 'bg-lime-200/60 border-lime-400/60', text: 'text-lime-900' },
		{ bg: 'bg-pink-200/60 border-pink-400/60', text: 'text-pink-900' },
		{ bg: 'bg-blue-200/60 border-blue-400/60', text: 'text-blue-900' },
	];

	/** Get weeks as groups of 7 days from calendarDays */
	/** Determine all unique user lanes for a week (one lane per user) */
	/** Find a span entry for a specific user on a specific day */
	async function addSchedulePeriod(e: Event) {
		e.preventDefault();
		if (!newPeriodFrom) {
			periodError = 'A start date is required.';
			return;
		}

		const validFrom = normalizeDateOnly(newPeriodFrom);
		const validTo = normalizeDateOnly(newPeriodTo);
		if (validTo && validTo < validFrom) {
			periodError = 'Valid to must be on or after valid from.';
			return;
		}

		addingPeriod = true;
		periodError = '';
		try {
			const payload = {
				validFrom,
				validTo: validTo || undefined,
				weeklyWorkHours: newPeriodWeeklyHours ?? undefined,
				distributeEvenly: newPeriodDistributeEvenly,
				targetMon: newPeriodDistributeEvenly ? undefined : newPeriodMon,
				targetTue: newPeriodDistributeEvenly ? undefined : newPeriodTue,
				targetWed: newPeriodDistributeEvenly ? undefined : newPeriodWed,
				targetThu: newPeriodDistributeEvenly ? undefined : newPeriodThu,
				targetFri: newPeriodDistributeEvenly ? undefined : newPeriodFri
			};

			const existingPeriod = schedulePeriods.find((p) => p.validFrom === validFrom);
			if (existingPeriod?.id) {
				await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesIdPut(orgSlug, existingPeriod.id, payload);
			} else {
				await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesPost(orgSlug, payload);
			}

			schedulePeriodsLoaded = false;
			await loadSchedulePeriods();
			showAddPeriod = false;
			newPeriodFrom = '';
			newPeriodTo = '';
			newPeriodWeeklyHours = null;
			newPeriodDistributeEvenly = true;
			newPeriodMon = newPeriodTue = newPeriodWed = newPeriodThu = newPeriodFri = 0;
		} catch (err) {
			periodError = extractErrorMessage(err, 'Failed to add schedule period.');
		} finally {
			addingPeriod = false;
		}
	}

	async function deleteSchedulePeriod(id: number) {
		if (!confirm('Delete this schedule period?')) return;
		try {
			await workScheduleApi.apiV1OrganizationsSlugWorkSchedulesIdDelete(orgSlug, id);
			schedulePeriodsLoaded = false;
			await loadSchedulePeriods();
		} catch (err) {
			periodError = extractErrorMessage(err, 'Failed to delete period.');
		}
	}

</script>

<svelte:head>
	<title>{org ? org.name : 'Organization'} - Time Tracking</title>
</svelte:head>

<div class="max-w-5xl mx-auto px-6 pb-6">
	{#if loading}
		<div class="flex items-center gap-3 justify-center py-12 text-base-content/40"><span class="loading loading-spinner loading-sm"></span><span>Loading...</span></div>
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if org}
		{#if actionError}
			<div class="alert alert-error mb-4 text-sm">{actionError}</div>
		{/if}

		{#if editing}
			<!-- Edit form -->
			<div class="card bg-base-100 border border-base-300 p-8 max-w-xl">
				<h2 class="text-xl font-bold text-base-content mb-5">Edit Organization</h2>
				{#if editError}
					<div class="alert alert-error mb-4 text-sm">{editError}</div>
				{/if}
				<form onsubmit={saveEdit}>
					<div class="form-control mb-5">
						<label for="editName" class="label text-sm font-medium text-base-content/70">Name</label>
						<input id="editName" type="text" class="input input-bordered w-full" bind:value={editName} required disabled={editSaving} />
					</div>
					<div class="form-control mb-5">
						<label for="editSlug" class="label text-sm font-medium text-base-content/70">Slug</label>
						<input id="editSlug" type="text" class="input input-bordered w-full" bind:value={editSlug} required disabled={editSaving} />
					</div>
					<div class="form-control mb-5">
						<label for="editDesc" class="label text-sm font-medium text-base-content/70">Description</label>
						<textarea id="editDesc" class="textarea textarea-bordered w-full" bind:value={editDescription} rows="3" disabled={editSaving}></textarea>
					</div>
					<div class="form-control mb-5">
						<label for="editWeb" class="label text-sm font-medium text-base-content/70">Website</label>
						<input id="editWeb" type="url" class="input input-bordered w-full" bind:value={editWebsite} disabled={editSaving} />
					</div>
					<div class="flex gap-3 justify-end mt-6">
						<button type="button" class="btn btn-outline btn-sm" onclick={cancelEdit}>Cancel</button>
						<button type="submit" class="btn btn-primary" disabled={editSaving}>
							{editSaving ? 'Saving...' : 'Save Changes'}
						</button>
					</div>
				</form>
			</div>
		{:else}
			<!-- Organization header -->
			<div class="flex items-start justify-between mb-2">
				<div>
					<h1 class="text-2xl font-bold text-base-content">{org.name}</h1>
					<span class="text-base-content/40 text-sm">/{org.slug}</span>
				</div>
				{#if canEdit}
					<div class="flex gap-2">
						<button class="btn btn-outline btn-sm" onclick={startEdit}>Edit</button>
						{#if isOwner}
							<button class="btn btn-outline btn-error btn-sm" onclick={deleteOrg}>Delete</button>
						{/if}
					</div>
				{/if}
			</div>

			{#if !canEdit && org.memberTimeEntryVisibility}
				<div class="alert alert-warning mb-4 text-sm">
					<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
					Your administrator can view your tracked working hours.
				</div>
			{/if}

			{#if showSettingsChangedBanner}
				<div class="alert alert-info mb-4 text-sm">
					<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
					Organization rules have been updated. Check the <a href="/organizations/{orgSlug}/settings" class="link link-primary font-semibold" onclick={() => dismissSettingsNotifications()}>Settings</a> page for details.
					<button class="btn btn-ghost btn-xs ml-auto text-lg" onclick={() => dismissSettingsNotifications()}>&times;</button>
				</div>
			{/if}

			{#if org.description}
				<p class="text-base-content/70 my-3">{org.description}</p>
			{/if}

			{#if org.website}
				<a href={org.website} target="_blank" class="link link-primary text-sm inline-block mb-6">{org.website}</a>
			{/if}

			<div class="pt-2">
					<p class="text-base-content/50 text-sm mt-2 mb-5 leading-relaxed">Your personal work schedule, overtime balance, absences and time period configurations.</p>
					<!-- Active Work Schedule (read-only, managed via periods) -->
					{#if myRole && workSchedule}
						<section class="mt-8 bg-base-200/30 rounded-lg p-5 border border-base-300">
							<div class="flex items-center justify-between mb-4">
								<h2 class="text-xl font-bold text-base-content">Active Schedule</h2>
							</div>

							<div class="grid grid-cols-[repeat(auto-fill,minmax(90px,1fr))] gap-2">
								<div class="flex flex-col items-center bg-base-100 p-2 rounded-lg border border-base-300">
									<span class="text-xs text-base-content/60 font-medium">Weekly Hours</span>
									<span class="text-lg font-bold text-base-content">{workSchedule.weeklyWorkHours ?? 'â€”'}h</span>
								</div>
								<div class="flex flex-col items-center bg-base-100 p-2 rounded-lg border border-base-300">
									<span class="text-xs text-base-content/60 font-medium">Mon</span>
									<span class="text-lg font-bold text-base-content">{workSchedule.targetMon ?? 0}h</span>
								</div>
								<div class="flex flex-col items-center bg-base-100 p-2 rounded-lg border border-base-300">
									<span class="text-xs text-base-content/60 font-medium">Tue</span>
									<span class="text-lg font-bold text-base-content">{workSchedule.targetTue ?? 0}h</span>
								</div>
								<div class="flex flex-col items-center bg-base-100 p-2 rounded-lg border border-base-300">
									<span class="text-xs text-base-content/60 font-medium">Wed</span>
									<span class="text-lg font-bold text-base-content">{workSchedule.targetWed ?? 0}h</span>
								</div>
								<div class="flex flex-col items-center bg-base-100 p-2 rounded-lg border border-base-300">
									<span class="text-xs text-base-content/60 font-medium">Thu</span>
									<span class="text-lg font-bold text-base-content">{workSchedule.targetThu ?? 0}h</span>
								</div>
								<div class="flex flex-col items-center bg-base-100 p-2 rounded-lg border border-base-300">
									<span class="text-xs text-base-content/60 font-medium">Fri</span>
									<span class="text-lg font-bold text-base-content">{workSchedule.targetFri ?? 0}h</span>
								</div>
							</div>
							<p class="text-xs text-base-content/50 mt-3">Schedule values are managed through the Schedule Periods section below.</p>
						</section>

						<!-- Initial Overtime Balance -->
						{#if workSchedule.initialOvertimeMode !== 'Disabled'}
							<section class="mt-8 bg-base-200/30 rounded-lg p-5 border border-base-300 mt-4">
								<div class="flex items-center justify-between mb-4">
									<h2 class="text-xl font-bold text-base-content">Initial Overtime Balance</h2>
									{#if !editingMyOvertime && workSchedule.initialOvertimeMode === 'Allowed'}
										<button class="btn btn-outline btn-info btn-xs" onclick={startEditMyOvertime}>Edit</button>
									{/if}
								</div>

								{#if editingMyOvertime}
									<div class="flex flex-col gap-3 py-2">
										{#if myOvertimeError}
											<div class="alert alert-error text-sm py-1.5 px-2.5">{myOvertimeError}</div>
										{/if}
										<div class="flex items-center gap-3">
											<label for="my-overtime-hours" class="text-sm text-base-content/70 font-medium min-w-[100px]">Hours</label>
											<input id="my-overtime-hours" type="number" class="input input-bordered input-xs w-20" bind:value={myOvertimeHours} step="0.5" />
										</div>
										<div class="flex gap-2 mt-1">
											<button class="btn btn-primary btn-sm" onclick={saveMyOvertime} disabled={myOvertimeSaving}>
												{myOvertimeSaving ? 'Saving...' : 'Save'}
											</button>
											<button class="btn btn-ghost btn-sm" onclick={() => (editingMyOvertime = false)}>Cancel</button>
										</div>
									</div>
								{:else}
									<div class="grid grid-cols-[repeat(auto-fill,minmax(90px,1fr))] gap-2">
										<div class="flex flex-col items-center bg-base-100 p-2 rounded-lg border border-base-300">
											<span class="text-xs text-base-content/60 font-medium">Hours</span>
											<span class="text-lg font-bold text-primary">{workSchedule.initialOvertimeHours ?? 0}h</span>
										</div>
										<div class="flex flex-col items-center bg-base-100 p-2 rounded-lg border border-base-300">
											<span class="text-xs text-base-content/60 font-medium">Mode</span>
											<span class="text-lg font-bold text-base-content !text-sm break-words">{ruleModeLabel(workSchedule.initialOvertimeMode)}</span>
										</div>
									</div>
									{#if workSchedule.initialOvertimeMode === 'RequiresApproval'}
										<p class="text-sm text-base-content/40 mt-3">Requires admin approval to change</p>
									{/if}
								{/if}
							</section>
						{/if}
					{/if}

					<!-- Vacation Days -->
					{#if myRole && myMember}
						<section class="mt-4 bg-base-200/30 rounded-lg p-5 border border-base-300">
							<div class="flex items-center justify-between mb-4">
								<h2 class="text-xl font-bold text-base-content">Vacation Days</h2>
								{#if canEdit && !editingMyVacation}
									<button class="btn btn-outline btn-info btn-xs" onclick={startEditMyVacation}>Edit</button>
								{/if}
							</div>

							{#if editingMyVacation}
								<div class="flex flex-col gap-3 py-2">
									{#if myVacationError}
										<div class="alert alert-error text-sm py-1.5 px-2.5">{myVacationError}</div>
									{/if}
									<div class="flex items-center gap-3">
										<label for="my-vacation-days" class="text-sm text-base-content/70 font-medium min-w-[100px]">Days / Year</label>
										<input id="my-vacation-days" type="number" class="input input-bordered input-xs w-20" bind:value={myVacationDays} step="0.5" min="0" max="365" />
									</div>
									<div class="flex gap-2 mt-1">
										<button class="btn btn-primary btn-sm" onclick={saveMyVacation} disabled={myVacationSaving}>
											{myVacationSaving ? 'Saving...' : 'Save'}
										</button>
										<button class="btn btn-ghost btn-sm" onclick={() => (editingMyVacation = false)}>Cancel</button>
									</div>
								</div>
							{:else}
								{@const allowed = myMember.vacationDaysPerYear ?? 0}
								{@const used = myMember.vacationDaysUsed ?? 0}
								{@const remaining = myMember.vacationDaysRemaining ?? 0}
								{#if allowed > 0}
									<div class="grid grid-cols-[repeat(auto-fill,minmax(90px,1fr))] gap-2 mb-3">
										<div class="flex flex-col items-center bg-base-100 p-2 rounded-lg border border-base-300">
											<span class="text-xs text-base-content/60 font-medium">Remaining</span>
											<span class="text-lg font-bold text-success">{remaining}</span>
										</div>
										<div class="flex flex-col items-center bg-base-100 p-2 rounded-lg border border-base-300">
											<span class="text-xs text-base-content/60 font-medium">Used</span>
											<span class="text-lg font-bold text-base-content">{used}</span>
										</div>
										<div class="flex flex-col items-center bg-base-100 p-2 rounded-lg border border-base-300">
											<span class="text-xs text-base-content/60 font-medium">Total</span>
											<span class="text-lg font-bold text-base-content">{allowed}</span>
										</div>
									</div>
									<div class="w-full bg-base-200 rounded-full h-2.5">
										<div
											class="h-2.5 rounded-full transition-all {used / allowed > 0.9 ? 'bg-error' : used / allowed > 0.7 ? 'bg-warning' : 'bg-success'}"
											style="width: {Math.min(100, (used / allowed) * 100)}%"
										></div>
									</div>
								{:else}
									<p class="text-base-content/50 text-sm">No vacation allowance set. {canEdit ? 'Click Edit to set your annual days.' : 'Ask an admin to set your vacation allowance.'}</p>
								{/if}
							{/if}
						</section>
					{/if}

					<!-- My Absences -->
					{#if myRole}
						<section class="mt-8 bg-base-200/30 rounded-lg p-5 border border-base-300">
							<div class="flex items-center justify-between mb-4">
								<h2 class="text-xl font-bold text-base-content">My Absences</h2>
								{#if absencesLoaded}
									<button class="btn btn-primary btn-sm" onclick={() => (showAddAbsence = !showAddAbsence)}>
										{showAddAbsence ? 'Cancel' : '+ Add Absence'}
									</button>
								{/if}
							</div>

							{#if absencesLoading}
								<p class="text-base-content/40">Loading absences...</p>
							{:else if absencesLoaded}
								{#if absenceError}
									<div class="alert alert-error text-sm py-1.5 px-2.5">{absenceError}</div>
								{/if}

								{#if showAddAbsence}
									<form onsubmit={addAbsence} class="flex flex-col gap-2.5 p-3 bg-base-200/30 rounded-lg mb-3 border border-base-300">
										<div class="flex items-center gap-2">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label class="min-w-[80px] text-sm font-medium text-base-content/70">From</label>
											<input type="date" class="input input-bordered input-sm flex-1" bind:value={newAbsenceDate} required disabled={addingAbsence} />
										</div>
										<div class="flex items-center gap-2">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label class="min-w-[80px] text-sm font-medium text-base-content/70">To (optional)</label>
											<input type="date" class="input input-bordered input-sm flex-1" bind:value={newAbsenceToDate} min={newAbsenceDate} disabled={addingAbsence} />
										</div>
										{#if newAbsenceDate && newAbsenceToDate && newAbsenceDate !== newAbsenceToDate}
											<p class="text-xs text-base-content/60 my-1 italic">Absences will be created for workdays (Monâ€“Fri) only.</p>
										{/if}
										<div class="flex items-center gap-2">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label class="min-w-[80px] text-sm font-medium text-base-content/70">Type</label>
											<select class="select select-bordered select-sm flex-1" bind:value={newAbsenceType} disabled={addingAbsence}>
												<option value={0}>Sick Day</option>
												<option value={1}>Vacation</option>
												<option value={2}>Other</option>
											</select>
										</div>
										<div class="flex items-center gap-2">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label class="min-w-[80px] text-sm font-medium text-base-content/70">Note</label>
											<input type="text" class="input input-bordered input-sm flex-1" bind:value={newAbsenceNote} placeholder="Optional note" disabled={addingAbsence} />
										</div>
										<div class="flex items-center gap-2">
											<label class="label cursor-pointer flex items-center gap-1.5 text-sm text-base-content/70">
												<input type="checkbox" class="checkbox checkbox-sm" bind:checked={newAbsenceHalfDay} disabled={addingAbsence} />
												Half day
											</label>
										</div>
										<div class="flex gap-2 mt-1">
											<button type="submit" class="btn btn-primary btn-sm" disabled={addingAbsence}>
												{addingAbsence ? 'Adding...' : 'Add'}
											</button>
										</div>
									</form>
								{/if}

								{@const myAbsences = (absences ?? []).filter(a => a.userId === auth.user?.id)}
								{#if myAbsences.length === 0}
									<p class="text-base-content/40">No absences recorded.</p>
								{:else}
									{@const absencesByYear = Object.entries(
										myAbsences.reduce<Record<string, typeof myAbsences>>((acc, a) => {
											const year = a.date?.substring(0, 4) ?? 'Unknown';
											(acc[year] ??= []).push(a);
											return acc;
										}, {})
									).sort(([a], [b]) => b.localeCompare(a))}
									{#each absencesByYear as [year, yearAbsences]}
										<div class="mb-4">
											<h3 class="text-sm font-semibold text-base-content/50 mb-2">{year}</h3>
											<div class="flex flex-col gap-1.5">
												{#each yearAbsences as a}
													<div class="flex items-center gap-3 py-2 px-3 bg-base-200/30 rounded-md text-sm">
														<span class="font-medium text-base-content/70 min-w-[90px]">{formatDateDisplay(a.date)}</span>
														<span class="badge badge-sm {absenceTypeBadge(a.type) === 'badge-sick' ? 'badge-error' : absenceTypeBadge(a.type) === 'badge-vacation' ? 'badge-info' : 'badge-ghost'}">{absenceTypeLabel(a.type)}</span>
														{#if a.isHalfDay}
															<span class="badge badge-warning badge-xs">½ Day</span>
														{/if}
														{#if a.note}
															<span class="text-base-content/40 text-sm italic flex-1">{a.note}</span>
														{/if}
														<button class="btn btn-ghost btn-xs text-error" title="Delete" onclick={() => deleteAbsence(a.id!)}>&times;</button>
													</div>
												{/each}
											</div>
										</div>
									{/each}
								{/if}
							{/if}
						</section>
					{/if}

					<!-- My Schedule Periods -->
					{#if myRole && workSchedule}
						<section class="mt-8 bg-base-200/30 rounded-lg p-5 border border-base-300">
							<div class="flex items-center justify-between mb-4">
								<h2 class="text-xl font-bold text-base-content">Schedule Periods</h2>
								{#if schedulePeriodsLoaded && workSchedule.workScheduleChangeMode === 'Allowed'}
									<button class="btn btn-primary btn-sm" onclick={() => (showAddPeriod = !showAddPeriod)}>
										{showAddPeriod ? 'Cancel' : '+ Add Period'}
									</button>
								{/if}
							</div>

							{#if workSchedule.workScheduleChangeMode === 'RequiresApproval'}
								<p class="text-sm text-base-content/40 mt-3">Schedule period changes require admin approval</p>
							{:else if workSchedule.workScheduleChangeMode === 'Disabled'}
								<p class="text-sm text-base-content/40 mt-3">Schedule period changes are disabled for this organization.</p>
							{/if}

							{#if schedulePeriodsLoading}
								<p class="text-base-content/40">Loading schedule periods...</p>
							{:else if schedulePeriodsLoaded}
								{#if periodError}
									<div class="alert alert-error text-sm py-1.5 px-2.5">{periodError}</div>
								{/if}

								{#if showAddPeriod && workSchedule.workScheduleChangeMode === 'Allowed'}
									<form onsubmit={addSchedulePeriod} class="flex flex-col gap-2.5 p-3 bg-base-200/30 rounded-lg mb-3 border border-base-300">
										<div class="flex items-center gap-2">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label class="min-w-[80px] text-sm font-medium text-base-content/70">From</label>
											<input type="date" class="input input-bordered input-sm flex-1" bind:value={newPeriodFrom} required disabled={addingPeriod} />
										</div>
										<div class="flex items-center gap-2">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label class="min-w-[80px] text-sm font-medium text-base-content/70">To (optional)</label>
											<input type="date" class="input input-bordered input-sm flex-1" bind:value={newPeriodTo} min={newPeriodFrom || undefined} disabled={addingPeriod} />
										</div>
										<div class="flex items-center gap-2">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label class="min-w-[80px] text-sm font-medium text-base-content/70">Weekly Hours</label>
											<input type="number" class="input input-bordered input-sm flex-1" bind:value={newPeriodWeeklyHours} step="0.5" min="0" max="168" disabled={addingPeriod} />
										</div>
										<div class="flex items-center gap-3">
											<label class="label cursor-pointer flex items-center gap-1.5 text-sm text-base-content/70">
												<input type="checkbox" class="checkbox checkbox-sm checkbox-primary" bind:checked={newPeriodDistributeEvenly} disabled={addingPeriod} />
												Distribute evenly (Monâ€“Fri)
											</label>
										</div>
										{#if !newPeriodDistributeEvenly}
											<div class="flex gap-2 flex-wrap">
												<div class="flex flex-col items-center gap-1"><span class="text-xs text-base-content/60 font-medium">Mon</span><input type="number" class="input input-bordered input-xs w-14 text-center" bind:value={newPeriodMon} step="0.5" min="0" max="24" disabled={addingPeriod} /></div>
												<div class="flex flex-col items-center gap-1"><span class="text-xs text-base-content/60 font-medium">Tue</span><input type="number" class="input input-bordered input-xs w-14 text-center" bind:value={newPeriodTue} step="0.5" min="0" max="24" disabled={addingPeriod} /></div>
												<div class="flex flex-col items-center gap-1"><span class="text-xs text-base-content/60 font-medium">Wed</span><input type="number" class="input input-bordered input-xs w-14 text-center" bind:value={newPeriodWed} step="0.5" min="0" max="24" disabled={addingPeriod} /></div>
												<div class="flex flex-col items-center gap-1"><span class="text-xs text-base-content/60 font-medium">Thu</span><input type="number" class="input input-bordered input-xs w-14 text-center" bind:value={newPeriodThu} step="0.5" min="0" max="24" disabled={addingPeriod} /></div>
												<div class="flex flex-col items-center gap-1"><span class="text-xs text-base-content/60 font-medium">Fri</span><input type="number" class="input input-bordered input-xs w-14 text-center" bind:value={newPeriodFri} step="0.5" min="0" max="24" disabled={addingPeriod} /></div>
											</div>
										{/if}
										<div class="flex gap-2 mt-1">
											<button type="submit" class="btn btn-primary btn-sm" disabled={addingPeriod}>
												{addingPeriod ? 'Adding...' : 'Add'}
											</button>
										</div>
									</form>
								{/if}

								{#if schedulePeriods.length === 0}
									<p class="text-base-content/40">No schedule periods configured. The base work schedule is used.</p>
								{:else}
									<div class="flex flex-col gap-1.5">
										{#each schedulePeriods as p}
											{@const today = new Date().toISOString().slice(0, 10)}
											{@const isActive = p.validFrom && p.validFrom <= today && (!p.validTo || p.validTo >= today)}
											<div class="flex items-center gap-3 py-2 px-3 rounded-md text-sm {isActive ? 'bg-success/5 border border-success' : 'bg-base-200/30'}">
												<div class="flex items-center gap-1.5 font-medium text-base-content/70">
													<span>{formatDateDisplay(p.validFrom)}</span>
													<span class="text-base-content/40">&rarr;</span>
													<span>{p.validTo ? formatDateDisplay(p.validTo) : 'ongoing'}</span>
												</div>
												{#if isActive}
													<span class="badge badge-success badge-xs font-semibold">Active</span>
												{/if}
												<span class="text-primary font-semibold ml-auto">{p.weeklyWorkHours ?? 'â€”'}h/week</span>
												{#if workSchedule.workScheduleChangeMode === 'Allowed'}
													<button class="btn btn-ghost btn-xs text-error" title="Delete" onclick={() => deleteSchedulePeriod(p.id!)}>&times;</button>
												{/if}
											</div>
										{/each}
									</div>
								{/if}
							{/if}
						</section>
					{/if}
				</div>

		{/if}
	{/if}
</div>

