<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { goto } from '$app/navigation';
	import { auth } from '$lib/stores/auth.svelte';
	import { organizationsApi, pauseRulesApi, workScheduleApi, requestsApi, holidayApi, absenceDayApi, notificationsApi } from '$lib/apiClient';
	import { formatRequestType, formatTimeAgo, statusBadgeClass, parseRequestData, absenceTypeLabel, absenceTypeBadge } from '$lib/utils/formatters';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import {
		parseRuleMode, ruleModeLabel, joinPolicyLabel, ruleModeButtonClass,
		ruleSettings, toggleSettings,
		visibilitySettings, parseVisibilityMode, visibilityModeLabel, visibilityModeButtonClass
	} from '$lib/utils/orgRules';
	import type {
		OrganizationDetailResponse,
		UpdateOrganizationRequest,
		AddMemberRequest,
		UpdateOrganizationSettingsRequest,
		PauseRuleResponse,
		CreatePauseRuleRequest,
		RuleMode,
		VisibilityMode,
		WorkScheduleResponse,
		OrgRequestResponse,
		HolidayResponse,
		AbsenceDayResponse,
		AbsenceType,
		MemberTimeOverviewResponse
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
	let showAddMember = $state(false);
	let newMemberEmail = $state('');
	let newMemberRole = $state(0);
	let addMemberError = $state('');
	let addingMember = $state(false);

	// User search for add member dropdown
	let allUsers = $state<Array<{id: number, email: string, firstName: string, lastName: string}>>([]);
	let selectedUserId = $state<number | null>(null);
	let usersLoaded = $state(false);

	// Action feedback
	let actionError = $state('');

	// Tab navigation
	let activeTab = $state<'my-schedule' | 'team' | 'absences' | 'settings'>('my-schedule');

	// Team overview data (admin)
	let teamOverview = $state<MemberTimeOverviewResponse[]>([]);
	let teamOverviewLoading = $state(false);
	let teamOverviewLoaded = $state(false);

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

	// Request history (admin)
	let requestHistory = $state<OrgRequestResponse[]>([]);
	let requestHistoryLoading = $state(false);
	let requestHistoryLoaded = $state(false);
	let requestHistoryFilter = $state<string>('all'); // 'all', 'Pending', 'Accepted', 'Declined'

	// Holidays
	let holidays = $state<HolidayResponse[]>([]);
	let holidaysLoading = $state(false);
	let holidaysLoaded = $state(false);
	let showAddHoliday = $state(false);
	let newHolidayName = $state('');
	let newHolidayDate = $state('');
	let addingHoliday = $state(false);
	let holidayError = $state('');
	let editingHolidayId = $state<number | null>(null);
	let editHolidayName = $state('');
	let editHolidayDate = $state('');
	let editHolidaySaving = $state(false);
	let newHolidayRecurring = $state(false);
	let newHolidayHalfDay = $state(false);
	let editHolidayRecurring = $state(false);
	let editHolidayHalfDay = $state(false);


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
	let adminAbsenceType = $state(0);
	let adminAbsenceNote = $state('');
	let adminAbsenceHalfDay = $state(false);
	let addingAdminAbsence = $state(false);
	let adminAbsenceError = $state('');
	let adminAbsenceFilter = $state<number | null>(null); // userId filter
	let panelTypeFilter = $state<string>('all'); // 'all', 'Vacation', 'SickDay', 'Other'
	let calendarMonth = $state(new Date().getMonth()); // 0-11
	let calendarYear = $state(new Date().getFullYear());

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

	// Settings change notification
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
		loadHolidays();
		loadAbsences();
		loadSchedulePeriods();
		loadUsersForDropdown();
		loadRequestHistory();
		loadTeamOverview();
	}

	/** Load time overview for current week (admins only, when visibility enabled) */
	async function loadTeamOverview() {
		if (teamOverviewLoaded || !canEdit || !org?.memberTimeEntryVisibility) return;
		teamOverviewLoading = true;
		try {
			const now = new Date();
			const dayOfWeek = now.getDay() || 7;
			const weekStart = new Date(now);
			weekStart.setDate(now.getDate() - dayOfWeek + 1);
			weekStart.setHours(0, 0, 0, 0);
			const weekEnd = new Date(weekStart);
			weekEnd.setDate(weekStart.getDate() + 6);
			weekEnd.setHours(23, 59, 59, 999);
			const { data } = await organizationsApi.apiV1OrganizationsSlugTimeOverviewGet(orgSlug, weekStart.toISOString(), weekEnd.toISOString());
			teamOverview = data;
			teamOverviewLoaded = true;
		} catch {
			teamOverview = [];
		} finally {
			teamOverviewLoading = false;
		}
	}

	function getMemberOverview(memberId: number): MemberTimeOverviewResponse | null {
		return teamOverview.find(m => m.userId === memberId) ?? null;
	}

	function formatMinutesToHours(minutes?: number | null): string {
		if (!minutes) return '0h';
		const h = Math.floor(Math.abs(minutes) / 60);
		const m = Math.abs(minutes) % 60;
		const sign = minutes < 0 ? '-' : '';
		return m > 0 ? `${sign}${h}h ${m}m` : `${sign}${h}h`;
	}

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

	async function loadRequestHistory() {
		if (requestHistoryLoaded) return;
		requestHistoryLoading = true;
		try {
			const { data } = await requestsApi.apiV1OrganizationsSlugRequestsGet(orgSlug);
			requestHistory = data.items ?? [];
			requestHistoryLoaded = true;
		} catch {
			requestHistory = [];
		} finally {
			requestHistoryLoading = false;
		}
	}

	function filteredRequests(): OrgRequestResponse[] {
		if (requestHistoryFilter === 'all') return requestHistory;
		return requestHistory.filter(r => r.status === requestHistoryFilter);
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

	async function loadUsersForDropdown() {
		if (usersLoaded) return;
		try {
			const { data: orgData } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			const users = orgData?.members ?? [];
			allUsers = users.map((u: any) => ({
				id: u.id,
				email: u.email,
				firstName: u.firstName,
				lastName: u.lastName
			}));
			usersLoaded = true;
		} catch {
			allUsers = [];
		}
	}

	function getAvailableUsers() {
		const memberIds = new Set((org?.members ?? []).map(m => m.id));
		return allUsers.filter(u => !memberIds.has(u.id));
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

	async function addMember(e: Event) {
		e.preventDefault();
		addMemberError = '';
		addingMember = true;
		try {
			if (!selectedUserId) {
				addMemberError = 'Please select a user.';
				addingMember = false;
				return;
			}

			const payload: AddMemberRequest = {
				userId: selectedUserId,
				role: newMemberRole as any
			};
			await organizationsApi.apiV1OrganizationsSlugMembersPost(orgSlug, payload);
			await reloadOrg();
			showAddMember = false;
			selectedUserId = null;
			newMemberRole = 0;
		} catch (err) {
			addMemberError = extractErrorMessage(err, 'Failed to add member.');
		} finally {
			addingMember = false;
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

	// Settings
	let settingsSaving = $state(false);
	let settingsError = $state('');

	async function cycleSetting(key: string) {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const current = parseRuleMode((org as Record<string, any>)[key]);
			const next = ((current + 1) % 3) as RuleMode;
			await organizationsApi.apiV1OrganizationsSlugSettingsPut(orgSlug, { [key]: next } as UpdateOrganizationSettingsRequest);
			await reloadOrg();
		} catch (err) {
			settingsError = extractErrorMessage(err, 'Failed to update setting.');
		} finally {
			settingsSaving = false;
		}
	}

	async function cycleVisibilitySetting(key: string) {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const current = parseVisibilityMode((org as Record<string, any>)[key]);
			const next = ((current + 1) % 3) as VisibilityMode;
			await organizationsApi.apiV1OrganizationsSlugSettingsPut(orgSlug, { [key]: next } as UpdateOrganizationSettingsRequest);
			await reloadOrg();
		} catch (err) {
			settingsError = extractErrorMessage(err, 'Failed to update setting.');
		} finally {
			settingsSaving = false;
		}
	}

	async function toggleSetting(key: string) {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const current = (org as Record<string, any>)[key];
			await organizationsApi.apiV1OrganizationsSlugSettingsPut(orgSlug, { [key]: !current } as UpdateOrganizationSettingsRequest);
			await reloadOrg();
		} catch (err) {
			settingsError = extractErrorMessage(err, 'Failed to update setting.');
		} finally {
			settingsSaving = false;
		}
	}

	// Pause Rules
	function getPauseRules(): PauseRuleResponse[] {
		return org?.pauseRules ? [...org.pauseRules].sort((a, b) => (a.minHours ?? 0) - (b.minHours ?? 0)) : [];
	}
	let showAddRule = $state(false);
	let newRuleMinHours = $state(6);
	let newRulePauseMinutes = $state(30);
	let addRuleError = $state('');
	let addingRule = $state(false);

	// Edit rule
	let editingRuleId = $state<number | null>(null);
	let editRuleMinHours = $state(0);
	let editRulePauseMinutes = $state(0);
	let editRuleError = $state('');
	let editingRuleSaving = $state(false);

	// Initial overtime per member (admin feature)
	let editingOvertimeMemberId = $state<number | null>(null);
	let editOvertimeMinutes = $state(0);
	let editOvertimeSaving = $state(false);
	let editOvertimeError = $state('');

	async function addPauseRule(e: Event) {
		e.preventDefault();
		addRuleError = '';
		addingRule = true;
		try {
			const payload: CreatePauseRuleRequest = {
				minHours: newRuleMinHours,
				pauseMinutes: newRulePauseMinutes
			};
			await pauseRulesApi.apiV1OrganizationsSlugPauseRulesPost(orgSlug, payload);
			await reloadOrg();
			showAddRule = false;
			newRuleMinHours = 6;
			newRulePauseMinutes = 30;
		} catch (err) {
			addRuleError = extractErrorMessage(err, 'Failed to add rule.');
		} finally {
			addingRule = false;
		}
	}

	function startEditRule(rule: PauseRuleResponse) {
		editingRuleId = rule.id ?? null;
		editRuleMinHours = rule.minHours ?? 0;
		editRulePauseMinutes = rule.pauseMinutes ?? 0;
		editRuleError = '';
	}

	function cancelEditRule() {
		editingRuleId = null;
	}

	async function saveEditRule(ruleId: number) {
		editRuleError = '';
		editingRuleSaving = true;
		try {
			await pauseRulesApi.apiV1OrganizationsSlugPauseRulesRuleIdPut(orgSlug, ruleId, {
				minHours: editRuleMinHours,
				pauseMinutes: editRulePauseMinutes
			});
			await reloadOrg();
			editingRuleId = null;
		} catch (err) {
			editRuleError = extractErrorMessage(err, 'Failed to update rule.');
		} finally {
			editingRuleSaving = false;
		}
	}

	async function deleteRule(ruleId: number) {
		if (!confirm('Delete this pause rule?')) return;
		try {
			await pauseRulesApi.apiV1OrganizationsSlugPauseRulesRuleIdDelete(orgSlug, ruleId);
			await reloadOrg();
		} catch (err) {
			settingsError = extractErrorMessage(err, 'Failed to delete rule.');
		}
	}

	function startEditOvertime(memberId: number, currentMinutes: number) {
		editingOvertimeMemberId = memberId;
		editOvertimeMinutes = currentMinutes;
		editOvertimeError = '';
	}

	function cancelEditOvertime() {
		editingOvertimeMemberId = null;
		editOvertimeError = '';
	}

	async function saveOvertime(userId: number) {
		editOvertimeSaving = true;
		editOvertimeError = '';
		try {
			await organizationsApi.apiV1OrganizationsSlugMembersMemberIdInitialOvertimePut(
				orgSlug, userId, { initialOvertimeHours: editOvertimeMinutes }
			);
			editingOvertimeMemberId = null;
			await reloadOrg();
		} catch (err) {
			editOvertimeError = extractErrorMessage(err, 'Failed to update overtime.');
		} finally {
			editOvertimeSaving = false;
		}
	}

	function formatOvertimeHours(minutes: number): string {
		if (minutes === 0) return 'Â±0h';
		const sign = minutes > 0 ? '+' : '';
		return sign + (minutes / 60).toFixed(1) + 'h';
	}

	// â”€â”€ Holidays â”€â”€
	async function loadHolidays() {
		if (holidaysLoaded) return;
		holidaysLoading = true;
		holidayError = '';
		try {
			const { data } = await holidayApi.apiV1OrganizationsSlugHolidaysGet(orgSlug);
			holidays = (data as HolidayResponse[]).sort((a, b) => (a.date ?? '').localeCompare(b.date ?? ''));
			holidaysLoaded = true;
		} catch {
			holidays = [];
		} finally {
			holidaysLoading = false;
		}
	}

	async function addHoliday(e: Event) {
		e.preventDefault();
		addingHoliday = true;
		holidayError = '';
		try {
			await holidayApi.apiV1OrganizationsSlugHolidaysPost(orgSlug, {
				date: newHolidayDate,
				name: newHolidayName,
				isRecurring: newHolidayRecurring,
				isHalfDay: newHolidayHalfDay
			});
			holidaysLoaded = false;
			await loadHolidays();
			showAddHoliday = false;
			newHolidayName = '';
			newHolidayDate = '';
			newHolidayRecurring = false;
			newHolidayHalfDay = false;
		} catch (err) {
			holidayError = extractErrorMessage(err, 'Failed to add holiday.');
		} finally {
			addingHoliday = false;
		}
	}

	function startEditHoliday(h: HolidayResponse) {
		editingHolidayId = h.id ?? null;
		editHolidayName = h.name ?? '';
		editHolidayDate = h.date ?? '';
		editHolidayRecurring = h.isRecurring ?? false;
		editHolidayHalfDay = h.isHalfDay ?? false;
	}

	async function saveEditHoliday(id: number) {
		editHolidaySaving = true;
		holidayError = '';
		try {
			await holidayApi.apiV1OrganizationsSlugHolidaysIdPut(orgSlug, id, {
				date: editHolidayDate,
				name: editHolidayName,
				isRecurring: editHolidayRecurring,
				isHalfDay: editHolidayHalfDay
			});
			editingHolidayId = null;
			holidaysLoaded = false;
			await loadHolidays();
		} catch (err) {
			holidayError = extractErrorMessage(err, 'Failed to update holiday.');
		} finally {
			editHolidaySaving = false;
		}
	}

	async function deleteHoliday(id: number) {
		if (!confirm('Delete this holiday?')) return;
		try {
			await holidayApi.apiV1OrganizationsSlugHolidaysIdDelete(orgSlug, id);
			holidaysLoaded = false;
			await loadHolidays();
		} catch (err) {
			holidayError = extractErrorMessage(err, 'Failed to delete holiday.');
		}
	}

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
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsencesGet(orgSlug);
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
		} catch (err) {
			absenceError = extractErrorMessage(err, 'Failed to delete absence.');
		}
	}

	// Admin absence
	async function addAdminAbsence(e: Event) {
		e.preventDefault();
		if (!adminAbsenceUserId) return;
		addingAdminAbsence = true;
		adminAbsenceError = '';
		try {
			await absenceDayApi.apiV1OrganizationsSlugAbsencesAdminPost(orgSlug, {
				userId: adminAbsenceUserId,
				date: adminAbsenceDate,
				type: adminAbsenceType as AbsenceType,
				isHalfDay: adminAbsenceHalfDay,
				note: adminAbsenceNote || undefined
			});
			absencesLoaded = false;
			await loadAbsences();
			showAdminAddAbsence = false;
			adminAbsenceDate = '';
			adminAbsenceType = 0;
			adminAbsenceNote = '';
			adminAbsenceHalfDay = false;
			adminAbsenceUserId = null;
		} catch (err) {
			adminAbsenceError = extractErrorMessage(err, 'Failed to add absence.');
		} finally {
			addingAdminAbsence = false;
		}
	}

	function filteredAbsences(): AbsenceDayResponse[] {
		if (!adminAbsenceFilter) return absences;
		return absences.filter(a => a.userId === adminAbsenceFilter);
	}

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
	function calendarDays(year: number, month: number): { date: Date; inMonth: boolean }[] {
		const first = new Date(year, month, 1);
		const last = new Date(year, month + 1, 0);
		const startDay = (first.getDay() + 6) % 7; // Monday = 0
		const days: { date: Date; inMonth: boolean }[] = [];
		// Fill leading days from prev month
		for (let i = startDay - 1; i >= 0; i--) {
			const d = new Date(year, month, -i);
			days.push({ date: d, inMonth: false });
		}
		// Days in month
		for (let d = 1; d <= last.getDate(); d++) {
			days.push({ date: new Date(year, month, d), inMonth: true });
		}
		// Fill trailing days to complete the grid
		while (days.length % 7 !== 0) {
			const d = new Date(year, month + 1, days.length - last.getDate() - startDay + 1);
			days.push({ date: d, inMonth: false });
		}
		return days;
	}

	function dateToKey(d: Date): string {
		return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
	}

	function buildAbsenceMap(absenceList: AbsenceDayResponse[]): Map<string, AbsenceDayResponse[]> {
		const map = new Map<string, AbsenceDayResponse[]>();
		for (const a of absenceList) {
			const key = a.date ?? '';
			if (!map.has(key)) map.set(key, []);
			map.get(key)!.push(a);
		}
		return map;
	}

	function buildHolidayMap(holidayList: HolidayResponse[]): Map<string, HolidayResponse[]> {
		const map = new Map<string, HolidayResponse[]>();
		for (const h of holidayList) {
			const key = h.date ?? '';
			if (!map.has(key)) map.set(key, []);
			map.get(key)!.push(h);
		}
		return map;
	}

	function navigateMonth(delta: number) {
		let m = calendarMonth + delta;
		let y = calendarYear;
		if (m < 0) { m = 11; y--; }
		else if (m > 11) { m = 0; y++; }
		calendarMonth = m;
		calendarYear = y;
	}

	function absenceColor(type: string | null | undefined): string {
		switch (type) {
			case 'Vacation': return 'bg-info/20 text-info border-info/30';
			case 'SickDay': return 'bg-error/20 text-error border-error/30';
			default: return 'bg-warning/20 text-warning border-warning/30';
		}
	}

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

	// Settings: cycle work schedule change mode — handled by generic cycleSetting now

	async function toggleMemberTimeEntryVisibility() {
		await toggleSetting('memberTimeEntryVisibility');
	}


</script>

<svelte:head>
	<title>{org ? org.name : 'Organization'} - Time Tracking</title>
</svelte:head>

<div class="max-w-4xl mx-auto p-6">
	<a href="/organizations" class="text-base-content/60 no-underline text-sm inline-block mb-3 hover:text-primary">&larr; Back to Organizations</a>

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
						{#if org.memberTimeEntryVisibility}
							<a href="/organizations/{orgSlug}/time-overview" class="btn btn-outline btn-sm">Time Overview</a>
						{/if}
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
					Organization rules have been updated. Check the <button class="link link-primary font-semibold" onclick={() => { activeTab = 'settings'; dismissSettingsNotifications(); }}>Rules</button> tab for details.
					<button class="btn btn-ghost btn-xs ml-auto text-lg" onclick={() => dismissSettingsNotifications()}>&times;</button>
				</div>
			{/if}

			{#if org.description}
				<p class="text-base-content/70 my-3">{org.description}</p>
			{/if}

			{#if org.website}
				<a href={org.website} target="_blank" class="link link-primary text-sm inline-block mb-6">{org.website}</a>
			{/if}

			<!-- Tab navigation -->
			<div class="tabs tabs-bordered mt-5">
				<button class="tab {activeTab === 'my-schedule' ? 'tab-active' : ''}" onclick={() => (activeTab = 'my-schedule')}>
					<svg class="w-4.5 h-4.5 shrink-0" viewBox="0 0 20 20" fill="currentColor"><path d="M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z"/></svg>
					My Schedule
				</button>
				<button class="tab {activeTab === 'team' ? 'tab-active' : ''}" onclick={() => (activeTab = 'team')}>
					<svg class="w-4.5 h-4.5 shrink-0" viewBox="0 0 20 20" fill="currentColor"><path d="M13 6a3 3 0 11-6 0 3 3 0 016 0zM18 8a2 2 0 11-4 0 2 2 0 014 0zM14 15a4 4 0 00-8 0v3h8v-3zM6 8a2 2 0 11-4 0 2 2 0 014 0zM16 18v-3a5.972 5.972 0 00-.75-2.906A3.005 3.005 0 0119 15v3h-3zM4.75 12.094A5.973 5.973 0 004 15v3H1v-3a3 3 0 013.75-2.906z"/></svg>
					Team
				</button>
				{#if canEdit}
					<button class="tab {activeTab === 'absences' ? 'tab-active' : ''}" onclick={() => (activeTab = 'absences')}>
						<svg class="w-4.5 h-4.5 shrink-0" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z"/></svg>
						Absences
					</button>
				{/if}
				<button class="tab {activeTab === 'settings' ? 'tab-active' : ''}" onclick={() => (activeTab = 'settings')}>
					<svg class="w-4.5 h-4.5 shrink-0" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M11.49 3.17c-.38-1.56-2.6-1.56-2.98 0a1.532 1.532 0 01-2.286.948c-1.372-.836-2.942.734-2.106 2.106.54.886.061 2.042-.947 2.287-1.561.379-1.561 2.6 0 2.978a1.532 1.532 0 01.947 2.287c-.836 1.372.734 2.942 2.106 2.106a1.532 1.532 0 012.287.947c.379 1.561 2.6 1.561 2.978 0a1.533 1.533 0 012.287-.947c1.372.836 2.942-.734 2.106-2.106a1.533 1.533 0 01.947-2.287c1.561-.379 1.561-2.6 0-2.978a1.532 1.532 0 01-.947-2.287c.836-1.372-.734-2.942-2.106-2.106a1.532 1.532 0 01-2.287-.947zM10 13a3 3 0 100-6 3 3 0 000 6z" clip-rule="evenodd"/></svg>
					{canEdit ? 'Settings' : 'Rules'}
				</button>
			</div>

			<!-- ==================== MY SCHEDULE TAB ==================== -->
			{#if activeTab === 'my-schedule'}
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

			<!-- ==================== TEAM TAB ==================== -->
			{:else if activeTab === 'team'}
				<div class="pt-2">
					<p class="text-base-content/50 text-sm mt-2 mb-5 leading-relaxed">Your team at a glance. {#if canEdit}Click a member for details, schedule editing, and absence history.{:else}Click a member to see their profile.{/if}</p>

					<!-- Members -->
					<section class="mt-8">
						<div class="flex items-center justify-between mb-4">
							<h2 class="text-xl text-base-content/70">Members ({(org.members ?? []).length})</h2>
							{#if canEdit}
								<button class="btn btn-primary btn-sm" onclick={() => { showAddMember = !showAddMember; if (showAddMember) loadUsersForDropdown(); }}>
									{showAddMember ? 'Cancel' : '+ Add Member'}
								</button>
							{/if}
						</div>

						{#if showAddMember && canEdit}
							<div class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300">
								{#if addMemberError}
									<div class="alert alert-error mb-4 text-sm">{addMemberError}</div>
								{/if}
								<form onsubmit={addMember} class="flex gap-2 items-center flex-wrap">
									<select bind:value={selectedUserId} disabled={addingMember} class="select select-bordered select-sm flex-1 min-w-[200px]">
										<option value={null}>Select a user...</option>
										{#each getAvailableUsers() as user}
											<option value={user.id}>{user.firstName} {user.lastName} ({user.email})</option>
										{/each}
									</select>
									<select bind:value={newMemberRole} disabled={addingMember} class="select select-bordered select-sm">
										<option value={0}>Member</option>
										<option value={1}>Admin</option>
										{#if isOwner}<option value={2}>Owner</option>{/if}
									</select>
									<button type="submit" class="btn btn-primary btn-sm" disabled={addingMember}>
										{addingMember ? 'Adding...' : 'Add'}
									</button>
								</form>
							</div>
						{/if}

						<div class="flex flex-col gap-2">
							{#each (org.members ?? []) as member}
								{@const overview = getMemberOverview(member.id!)}
								<a
									class="group flex items-center gap-4 p-3.5 bg-base-100 border border-base-300 rounded-xl cursor-pointer transition-all hover:border-primary/30 hover:shadow-md hover:-translate-y-px no-underline text-base-content"
									href="/organizations/{orgSlug}/members/{member.id}"
									title="View {member.firstName}'s details"
								>
									<div class="w-10 h-10 rounded-full bg-gradient-to-br from-primary to-secondary text-primary-content flex items-center justify-center text-sm font-semibold shrink-0">
										{(member.firstName?.[0] ?? '').toUpperCase()}{(member.lastName?.[0] ?? '').toUpperCase()}
									</div>
									<div class="flex-1 min-w-0">
										<div class="font-semibold text-base-content text-[0.9375rem] flex items-center gap-1.5">
											{member.firstName} {member.lastName}
											{#if member.id === auth.user?.id}
												<span class="badge badge-info badge-xs font-semibold">You</span>
											{/if}
										</div>
										<div class="text-sm text-base-content/40 mt-0.5">{member.email}</div>
										{#if canEdit && org.memberTimeEntryVisibility && overview}
											<div class="flex gap-3 mt-1.5 flex-wrap">
												<span class="flex items-center gap-1 text-xs text-base-content/50" title="Tracked this week">
													<svg viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5 opacity-60"><path d="M8 3.5a.5.5 0 00-1 0V8a.5.5 0 00.252.434l3.5 2a.5.5 0 00.496-.868L8 7.71V3.5z"/><path d="M8 16A8 8 0 108 0a8 8 0 000 16zm7-8A7 7 0 111 8a7 7 0 0114 0z"/></svg>
													{formatMinutesToHours(overview.netTrackedMinutes)}
												</span>
												<span class="flex items-center gap-1 text-xs text-base-content/50" title="Entries this week">
													<svg viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5 opacity-60"><path d="M5 3.5h6A1.5 1.5 0 0112.5 5v6a1.5 1.5 0 01-1.5 1.5H5A1.5 1.5 0 013.5 11V5A1.5 1.5 0 015 3.5z"/></svg>
													{overview.entryCount ?? 0} entries
												</span>
												{#if overview.weeklyWorkHours}
													<span class="flex items-center gap-1 text-xs text-base-content/50" title="Weekly target">
														<svg viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5 opacity-60"><path d="M8 15A7 7 0 118 1a7 7 0 010 14zm0 1A8 8 0 108 0a8 8 0 000 16z"/><path d="M10.97 4.97a.235.235 0 00-.02.022L7.477 9.417 5.384 7.323a.75.75 0 00-1.06 1.06L6.97 11.03a.75.75 0 001.079-.02l3.992-4.99a.75.75 0 00-1.071-1.05z"/></svg>
														{overview.weeklyWorkHours}h/w
													</span>
												{/if}
											</div>
										{/if}
									</div>
									<div class="flex items-center gap-2 shrink-0">
										{#if (member.vacationDaysPerYear ?? 0) > 0}
											<span class="badge badge-sm badge-outline" title="Vacation: {member.vacationDaysUsed ?? 0}/{member.vacationDaysPerYear} used">
												🏖 {member.vacationDaysRemaining ?? member.vacationDaysPerYear}d left
											</span>
										{/if}
										<span class="badge badge-sm uppercase tracking-wide {(member.role?.toLowerCase() ?? 'member') === 'owner' ? 'badge-warning' : (member.role?.toLowerCase() ?? 'member') === 'admin' ? 'badge-info' : 'badge-ghost'}">{member.role}</span>
										<svg class="w-5 h-5 text-base-content/20 group-hover:text-primary transition-colors" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clip-rule="evenodd"/></svg>
									</div>
								</a>
							{/each}
						</div>
					</section>

					<!-- Holidays -->
					{#if myRole}
						<section class="mt-8 bg-base-200/30 rounded-lg p-5 border border-base-300">
							<div class="flex items-center justify-between mb-4">
								<h2 class="text-xl font-bold text-base-content">Holidays</h2>
								{#if holidaysLoaded && canEdit}
									<button class="btn btn-primary btn-sm" onclick={() => (showAddHoliday = !showAddHoliday)}>
										{showAddHoliday ? 'Cancel' : '+ Add Holiday'}
									</button>
								{/if}
							</div>

							{#if holidaysLoading}
								<p class="text-base-content/40">Loading holidays...</p>
							{:else if holidaysLoaded}
								{#if holidayError}
									<div class="alert alert-error text-sm py-1.5 px-2.5">{holidayError}</div>
								{/if}

	

								{#if showAddHoliday && canEdit}
									<form onsubmit={addHoliday} class="flex flex-col gap-2.5 p-3 bg-base-200/30 rounded-lg mb-3 border border-base-300">
										<div class="flex items-center gap-2">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label class="min-w-[80px] text-sm font-medium text-base-content/70">Date</label>
											<input type="date" class="input input-bordered input-sm flex-1" bind:value={newHolidayDate} required disabled={addingHoliday} />
										</div>
										<div class="flex items-center gap-2">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label class="min-w-[80px] text-sm font-medium text-base-content/70">Name</label>
											<input type="text" class="input input-bordered input-sm flex-1" bind:value={newHolidayName} placeholder="e.g. Christmas" required disabled={addingHoliday} />
										</div>
										<div class="flex items-center gap-2">
											<label class="label cursor-pointer flex items-center gap-1.5 text-sm text-base-content/70">
												<input type="checkbox" class="checkbox checkbox-sm" bind:checked={newHolidayRecurring} disabled={addingHoliday} />
												Yearly recurring
											</label>
											<label class="label cursor-pointer flex items-center gap-1.5 text-sm text-base-content/70">
												<input type="checkbox" class="checkbox checkbox-sm" bind:checked={newHolidayHalfDay} disabled={addingHoliday} />
												Half day
											</label>
										</div>
										<div class="flex gap-2 mt-1">
											<button type="submit" class="btn btn-primary btn-sm" disabled={addingHoliday}>
												{addingHoliday ? 'Adding...' : 'Add'}
											</button>
										</div>
									</form>
								{/if}

								{#if holidays.length === 0}
									<p class="text-base-content/40">No holidays configured.</p>
								{:else}
									{@const holidaysByYear = Object.entries(
										holidays.reduce<Record<string, typeof holidays>>((acc, h) => {
											const year = h.isRecurring ? 'Recurring' : (h.date?.substring(0, 4) ?? 'Unknown');
											(acc[year] ??= []).push(h);
											return acc;
										}, {})
									).sort(([a], [b]) => a === 'Recurring' ? 1 : b === 'Recurring' ? -1 : b.localeCompare(a))}
									{#each holidaysByYear as [year, yearHolidays]}
										<div class="mb-4">
											<h3 class="text-sm font-semibold text-base-content/50 mb-2">{year}</h3>
											<div class="flex flex-col gap-1.5">
												{#each yearHolidays as h}
													<div class="flex items-center gap-3 py-2 px-3 bg-base-200/30 rounded-md text-sm">
														{#if editingHolidayId === h.id && canEdit}
															<input type="date" bind:value={editHolidayDate} class="input input-bordered input-xs w-20" disabled={editHolidaySaving} />
															<input type="text" bind:value={editHolidayName} class="input input-bordered input-xs w-20" disabled={editHolidaySaving} />
															<label class="label cursor-pointer flex items-center gap-1.5 text-[0.8rem] whitespace-nowrap text-sm text-base-content/70">
																<input type="checkbox" class="checkbox checkbox-sm" bind:checked={editHolidayRecurring} disabled={editHolidaySaving} />
																Yearly
															</label>
															<label class="label cursor-pointer flex items-center gap-1.5 text-[0.8rem] whitespace-nowrap text-sm text-base-content/70">
																<input type="checkbox" class="checkbox checkbox-sm" bind:checked={editHolidayHalfDay} disabled={editHolidaySaving} />
																Half
															</label>
															<div class="flex items-center gap-1.5">
																<button class="btn btn-primary btn-sm" onclick={() => saveEditHoliday(h.id!)} disabled={editHolidaySaving}>Save</button>
																<button class="btn btn-ghost btn-sm" onclick={() => (editingHolidayId = null)}>Cancel</button>
															</div>
														{:else}
															<span class="font-medium text-base-content/70 min-w-[90px]">{formatDateDisplay(h.date)}</span>
															<span class="flex-1 text-base-content/60">{h.name}</span>
															{#if h.isHalfDay}
																<span class="badge badge-warning badge-xs">½ Day</span>
															{/if}
															{#if h.isRecurring}
																<span class="badge badge-info badge-xs" title="Repeats every year">🔁 Yearly</span>
															{/if}
															{#if canEdit}
																<div class="flex items-center gap-1.5">
																	<button class="btn btn-ghost btn-sm" onclick={() => startEditHoliday(h)}>Edit</button>
																	<button class="btn btn-ghost btn-xs text-error" title="Delete" onclick={() => deleteHoliday(h.id!)}>&times;</button>
																</div>
															{/if}
														{/if}
													</div>
												{/each}
											</div>
										</div>
									{/each}
								{/if}
							{/if}
						</section>
					{/if}
				</div>

			<!-- ==================== ABSENCES TAB (Admin) ==================== -->
			{:else if activeTab === 'absences' && canEdit}
				<div class="pt-2">
					<p class="text-base-content/50 text-sm mt-2 mb-5 leading-relaxed">Calendar overview of absences and vacation days across all members.</p>

					<!-- Calendar Navigation -->
					{#if org}
					{@const absenceMap = buildAbsenceMap(absences)}
					{@const holidayMap = buildHolidayMap(holidays)}
					{@const days = calendarDays(calendarYear, calendarMonth)}
					{@const monthLabel = new Date(calendarYear, calendarMonth).toLocaleDateString('en-US', { month: 'long', year: 'numeric' })}
					{@const today = dateToKey(new Date())}

					<section>
						<div class="flex items-center justify-between mb-4">
							<button class="btn btn-ghost btn-sm" aria-label="Previous month" onclick={() => navigateMonth(-1)}>
								<svg class="w-5 h-5" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M12.79 5.23a.75.75 0 01-.02 1.06L8.832 10l3.938 3.71a.75.75 0 11-1.04 1.08l-4.5-4.25a.75.75 0 010-1.08l4.5-4.25a.75.75 0 011.06.02z" clip-rule="evenodd"/></svg>
							</button>
							<div class="flex items-center gap-3">
								<h2 class="text-xl font-bold text-base-content">{monthLabel}</h2>
								<button class="btn btn-ghost btn-xs" onclick={() => { calendarMonth = new Date().getMonth(); calendarYear = new Date().getFullYear(); }}>Today</button>
							</div>
							<button class="btn btn-ghost btn-sm" aria-label="Next month" onclick={() => navigateMonth(1)}>
								<svg class="w-5 h-5" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clip-rule="evenodd"/></svg>
							</button>
						</div>

						<!-- Day-of-week headers -->
						<div class="grid grid-cols-7 gap-px mb-px">
							{#each ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'] as dow}
								<div class="text-center text-xs font-semibold text-base-content/50 py-2 {dow === 'Sat' || dow === 'Sun' ? 'text-base-content/30' : ''}">{dow}</div>
							{/each}
						</div>

						<!-- Calendar grid -->
						<div class="grid grid-cols-7 gap-px bg-base-300/50 border border-base-300 rounded-lg overflow-hidden">
							{#each days as { date, inMonth }}
								{@const key = dateToKey(date)}
								{@const isToday = key === today}
								{@const isWeekend = date.getDay() === 0 || date.getDay() === 6}
								{@const dayAbsences = absenceMap.get(key) ?? []}
								{@const dayHolidays = holidayMap.get(key) ?? []}
								<div class="min-h-[90px] p-1.5 flex flex-col {inMonth ? 'bg-base-100' : 'bg-base-200/50'} {isWeekend && inMonth ? 'bg-base-200/30' : ''}">
									<div class="flex items-center justify-between mb-1">
										<span class="text-xs font-medium {isToday ? 'bg-primary text-primary-content rounded-full w-6 h-6 flex items-center justify-center' : ''} {inMonth ? 'text-base-content' : 'text-base-content/25'} {isWeekend && inMonth ? 'text-base-content/40' : ''}">
											{date.getDate()}
										</span>
										{#if dayAbsences.length > 0}
											<span class="text-[10px] text-base-content/40">{dayAbsences.length}</span>
										{/if}
									</div>
									{#each dayHolidays as h}
										<div class="text-[10px] px-1 py-0.5 rounded bg-success/15 text-success border border-success/20 mb-0.5 truncate" title={h.name ?? ''}>
											🎉 {h.name}
										</div>
									{/each}
									{#each dayAbsences.slice(0, 3) as a}
										<a href="/organizations/{orgSlug}/members/{a.userId}" class="block text-[10px] px-1 py-0.5 rounded border mb-0.5 truncate no-underline hover:brightness-90 transition-all cursor-pointer {absenceColor(a.type)}" title="{a.userFirstName} {a.userLastName} — {absenceTypeLabel(a.type ?? '')}{a.isHalfDay ? ' (½)' : ''}{a.note ? ': ' + a.note : ''}">
											{(a.userFirstName?.[0] ?? '')}{(a.userLastName?.[0] ?? '')} {a.userFirstName}{#if a.isHalfDay} ½{/if}
										</a>
									{/each}
									{#if dayAbsences.length > 3}
										<div class="text-[10px] text-base-content/40 text-center">+{dayAbsences.length - 3} more</div>
									{/if}
								</div>
							{/each}
						</div>

						<!-- Legend -->
						<div class="flex flex-wrap gap-4 mt-3 text-xs text-base-content/60">
							<span class="flex items-center gap-1"><span class="w-3 h-3 rounded bg-info/20 border border-info/30"></span> Vacation</span>
							<span class="flex items-center gap-1"><span class="w-3 h-3 rounded bg-error/20 border border-error/30"></span> Sick Day</span>
							<span class="flex items-center gap-1"><span class="w-3 h-3 rounded bg-warning/20 border border-warning/30"></span> Other</span>
							<span class="flex items-center gap-1"><span class="w-3 h-3 rounded bg-success/15 border border-success/20"></span> Holiday</span>
						</div>
					</section>

					<!-- Vacation Days Summary -->
					<section class="mt-8 bg-base-200/30 rounded-lg p-5 border border-base-300">
						<h2 class="text-xl font-bold text-base-content mb-4">Vacation Days Balance</h2>
						{#if org}
						{@const membersWithVacation = (org.members ?? []).filter(m => (m.vacationDaysPerYear ?? 0) > 0).sort((a, b) => ((a.vacationDaysRemaining ?? 0) - (b.vacationDaysRemaining ?? 0)))}
						{#if membersWithVacation.length === 0}
							<p class="text-base-content/40 text-sm">No members have vacation days configured.</p>
						{:else}
							<div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
								{#each membersWithVacation as member}
									{@const total = member.vacationDaysPerYear ?? 0}
									{@const used = member.vacationDaysUsed ?? 0}
									{@const remaining = member.vacationDaysRemaining ?? total}
									{@const pct = total > 0 ? Math.min(100, Math.round((used / total) * 100)) : 0}
									<a href="/organizations/{orgSlug}/members/{member.id}" class="flex items-start gap-3 p-3 bg-base-100 rounded-lg border border-base-300 hover:border-primary/30 hover:shadow-sm transition-all no-underline text-base-content">
										<div class="w-9 h-9 rounded-full bg-gradient-to-br from-primary to-secondary text-primary-content flex items-center justify-center text-xs font-semibold shrink-0 mt-0.5">
											{(member.firstName?.[0] ?? '').toUpperCase()}{(member.lastName?.[0] ?? '').toUpperCase()}
										</div>
										<div class="flex-1 min-w-0">
											<div class="font-semibold text-sm truncate">{member.firstName} {member.lastName}</div>
											<div class="flex items-center gap-2 mt-1">
												<progress class="progress {pct >= 90 ? 'progress-error' : pct >= 70 ? 'progress-warning' : 'progress-primary'} flex-1 h-2" value={pct} max="100"></progress>
												<span class="text-xs text-base-content/50 shrink-0">{pct}%</span>
											</div>
											<div class="flex justify-between mt-1 text-xs text-base-content/50">
												<span>{used}d used / {total}d</span>
												<span class="font-semibold {remaining <= 2 ? 'text-error' : remaining <= 5 ? 'text-warning' : 'text-success'}">{remaining}d left</span>
											</div>
										</div>
									</a>
								{/each}
							</div>
						{/if}
						{/if}
					</section>
					<section class="mt-8 bg-base-200/30 rounded-lg p-5 border border-base-300">
						<div class="flex items-center justify-between mb-4">
							<h2 class="text-xl font-bold text-base-content">Manage Absences</h2>
							<button class="btn btn-primary btn-sm" onclick={() => { showAdminAddAbsence = !showAdminAddAbsence; if (showAdminAddAbsence) loadUsersForDropdown(); }}>
								{showAdminAddAbsence ? 'Cancel' : '+ Add Absence'}
							</button>
						</div>

						{#if showAdminAddAbsence}
							<div class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300">
								{#if adminAbsenceError}
									<div class="alert alert-error text-sm py-1.5 px-2.5 mb-2">{adminAbsenceError}</div>
								{/if}
								<form onsubmit={addAdminAbsence} class="flex flex-wrap gap-2 items-end">
									<div class="flex flex-col gap-1">
										<span class="text-xs text-base-content/60">Member</span>
										<select bind:value={adminAbsenceUserId} class="select select-bordered select-sm min-w-[180px]" required>
											<option value={null}>Select member...</option>
											{#each (org.members ?? []) as m}
												<option value={m.id}>{m.firstName} {m.lastName}</option>
											{/each}
										</select>
									</div>
									<div class="flex flex-col gap-1">
										<span class="text-xs text-base-content/60">Date</span>
										<input type="date" class="input input-bordered input-sm" bind:value={adminAbsenceDate} required />
									</div>
									<div class="flex flex-col gap-1">
										<span class="text-xs text-base-content/60">Type</span>
										<select bind:value={adminAbsenceType} class="select select-bordered select-sm">
											<option value={0}>Sick Day</option>
											<option value={1}>Vacation</option>
											<option value={2}>Other</option>
										</select>
									</div>
									<div class="flex flex-col gap-1">
										<span class="text-xs text-base-content/60">Note</span>
										<input type="text" class="input input-bordered input-sm" bind:value={adminAbsenceNote} placeholder="Optional" />
									</div>
									<label class="label cursor-pointer flex items-center gap-1.5 text-sm">
										<input type="checkbox" class="checkbox checkbox-sm" bind:checked={adminAbsenceHalfDay} />
										Half day
									</label>
									<button type="submit" class="btn btn-primary btn-sm" disabled={addingAdminAbsence}>
										{addingAdminAbsence ? 'Adding...' : 'Add'}
									</button>
								</form>
							</div>
						{/if}
					</section>
					{/if}
				</div>

			<!-- ==================== SETTINGS TAB (Admin) ==================== -->
			{:else if activeTab === 'settings' && canEdit}
				<div class="pt-2">
					<p class="text-base-content/50 text-sm mt-2 mb-5 leading-relaxed">Organization-wide rules, permissions, and approval workflows.</p>

					<!-- Organization Settings -->
					<section class="mt-10">
						<h2 class="text-xl text-base-content/70 mb-4">Organization Settings</h2>
						{#if settingsError}
							<div class="alert alert-error mb-4 text-sm">{settingsError}</div>
						{/if}

						<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
							<!-- Toggle settings (booleans) -->
							{#each toggleSettings as ts}
								{@const value = (org as Record<string, any>)[ts.key]}
								<div class="flex items-center justify-between p-4 border-b border-base-200 last:border-b-0">
									<div class="setting-info">
										<div class="font-semibold text-base-content mb-0.5">{ts.label}</div>
										<div class="text-sm text-base-content/60 max-w-[400px]">{ts.description}</div>
									</div>
									<button
										class="relative w-12 h-[26px] {value ? 'bg-primary' : 'bg-base-300'} rounded-full border-none cursor-pointer transition-colors shrink-0 p-0"
										onclick={() => toggleSetting(ts.key)}
										disabled={settingsSaving}
										aria-label="Toggle {ts.label}"
									>
										<span class="absolute top-[3px] {value ? 'translate-x-[22px]' : 'translate-x-0'} left-[3px] w-5 h-5 bg-base-100 rounded-full transition-transform shadow-sm"></span>
									</button>
								</div>
							{/each}

							<!-- Rule mode settings (tri-state cycle) -->
							{#each ruleSettings as rs}
								{@const mode = (org as Record<string, any>)[rs.key] as string | null}
								{@const label = rs.labelFn ? rs.labelFn(mode) : ruleModeLabel(mode)}
								<div class="flex items-center justify-between p-4 border-b border-base-200 last:border-b-0">
									<div class="setting-info">
										<div class="font-semibold text-base-content mb-0.5">{rs.label}</div>
										<div class="text-sm text-base-content/60 max-w-[400px]">{rs.description}</div>
									</div>
									<button
										class="btn btn-sm min-w-[130px] font-semibold whitespace-nowrap {ruleModeButtonClass(mode)}"
										onclick={() => cycleSetting(rs.key)}
										disabled={settingsSaving}
									>
										{label}
									</button>
								</div>
							{/each}

							<!-- Visibility settings (Private / Admin Only / All Members) -->
							{#each visibilitySettings as vs}
								{@const mode = (org as Record<string, any>)[vs.key] as string | null}
								<div class="flex items-center justify-between p-4 border-b border-base-200 last:border-b-0">
									<div class="setting-info">
										<div class="font-semibold text-base-content mb-0.5">{vs.label}</div>
										<div class="text-sm text-base-content/60 max-w-[400px]">{vs.description}</div>
									</div>
									<button
										class="btn btn-sm min-w-[130px] font-semibold whitespace-nowrap {visibilityModeButtonClass(mode)}"
										onclick={() => cycleVisibilitySetting(vs.key)}
										disabled={settingsSaving}
									>
										{visibilityModeLabel(mode)}
									</button>
								</div>
							{/each}
						</div>
						<div class="bg-base-200/50 rounded-lg p-4 mt-4 border border-base-300">
							<div class="flex items-center justify-between">
								<div class="setting-info">
									<div class="font-semibold text-base-content mb-0.5">Default Vacation Days</div>
									<div class="text-sm text-base-content/60 max-w-[400px]">Default number of vacation days per year for new members. Existing members can be set individually.</div>
								</div>
								<div class="flex items-center gap-2">
									<input
										type="number"
										class="input input-bordered input-sm w-20 text-center"
										step="0.5"
										min="0"
										max="365"
										value={org.defaultVacationDays ?? 0}
										onchange={async (e) => {
											const val = parseFloat((e.target as HTMLInputElement).value);
											if (isNaN(val) || val < 0 || !org) return;
											settingsSaving = true;
											try {
												await organizationsApi.apiV1OrganizationsSlugSettingsPut(org.slug!, { defaultVacationDays: val });
												await loadOrg();
											} catch { /* ignore */ }
											settingsSaving = false;
										}}
										disabled={settingsSaving}
									/>
									<span class="text-sm text-base-content/60">days/year</span>
								</div>
							</div>
						</div>

						<!-- Pause Rules -->
						{#if org.autoPauseEnabled}
							<div class="mt-6">
								<div class="flex items-center justify-between mb-4">
									<h3 class="text-base font-bold text-base-content/70">Pause Rules</h3>
									<button class="btn btn-primary btn-sm" onclick={() => (showAddRule = !showAddRule)}>
										{showAddRule ? 'Cancel' : '+ Add Rule'}
									</button>
								</div>

								{#if showAddRule}
									<div class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300">
										{#if addRuleError}
											<div class="alert alert-error mb-4 text-sm">{addRuleError}</div>
										{/if}
										<form onsubmit={addPauseRule} class="flex gap-2 items-center flex-wrap">
											<div class="flex items-center gap-1.5">
												<!-- svelte-ignore a11y_label_has_associated_control -->
												<label class="text-sm text-base-content/70 font-medium whitespace-nowrap">When tracked &ge;</label>
												<input type="number" class="input input-bordered input-xs w-[70px] text-center" step="0.5" min="0.5" bind:value={newRuleMinHours} required disabled={addingRule} />
												<span class="text-sm text-base-content/60 whitespace-nowrap">hours</span>
											</div>
											<div class="flex items-center gap-1.5">
												<!-- svelte-ignore a11y_label_has_associated_control -->
												<label class="text-sm text-base-content/70 font-medium whitespace-nowrap">deduct</label>
												<input type="number" class="input input-bordered input-xs w-[70px] text-center" min="1" bind:value={newRulePauseMinutes} required disabled={addingRule} />
												<span class="text-sm text-base-content/60 whitespace-nowrap">min pause</span>
											</div>
											<button type="submit" class="btn btn-primary btn-sm" disabled={addingRule}>
												{addingRule ? 'Adding...' : 'Add Rule'}
											</button>
										</form>
									</div>
								{/if}

								{#if getPauseRules().length === 0}
									<p class="text-base-content/40">No pause rules configured. Add rules to automatically deduct break time.</p>
								{:else}
									<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
										{#each getPauseRules() as rule}
											<div class="flex items-center justify-between p-3 border-b border-base-200 last:border-b-0 flex-wrap gap-2">
												{#if editingRuleId === rule.id}
													{#if editRuleError}
														<div class="alert alert-error mb-4 text-sm" style="width:100%">{editRuleError}</div>
													{/if}
													<div class="flex items-center gap-3 flex-wrap w-full">
														<div class="flex items-center gap-1.5">
															<!-- svelte-ignore a11y_label_has_associated_control -->
															<label class="text-sm text-base-content/70 font-medium whitespace-nowrap">&ge;</label>
															<input type="number" class="input input-bordered input-xs w-[70px] text-center" step="0.5" min="0.5" bind:value={editRuleMinHours} disabled={editingRuleSaving} />
															<span class="text-sm text-base-content/60 whitespace-nowrap">h</span>
														</div>
														<div class="flex items-center gap-1.5">
															<!-- svelte-ignore a11y_label_has_associated_control -->
															<label class="text-sm text-base-content/70 font-medium whitespace-nowrap">&rarr;</label>
															<input type="number" class="input input-bordered input-xs w-[70px] text-center" min="1" bind:value={editRulePauseMinutes} disabled={editingRuleSaving} />
															<span class="text-sm text-base-content/60 whitespace-nowrap">min</span>
														</div>
														<div class="flex items-center gap-1.5">
															<button class="btn btn-primary btn-sm" onclick={() => saveEditRule(rule.id!)} disabled={editingRuleSaving}>Save</button>
															<button class="btn btn-ghost btn-sm" onclick={cancelEditRule}>Cancel</button>
														</div>
													</div>
												{:else}
													<div class="text-[0.9375rem] text-base-content/70">
														<strong>&ge; {rule.minHours}h</strong> tracked &rarr; <strong>{rule.pauseMinutes} min</strong> pause deducted
													</div>
													<div class="flex items-center gap-1.5">
														<button class="btn btn-ghost btn-sm" onclick={() => startEditRule(rule)}>Edit</button>
														<button class="btn btn-ghost btn-xs text-error" title="Delete rule" onclick={() => deleteRule(rule.id!)}>&times;</button>
													</div>
												{/if}
											</div>
										{/each}
									</div>
								{/if}
							</div>
						{/if}
					</section>

					<!-- Request History -->
					<section class="mt-8">
						<div class="flex items-center justify-between mb-4">
							<h2 class="text-xl font-bold text-base-content">Request History</h2>
						</div>

						{#if requestHistoryLoading}
							<p class="text-base-content/40">Loading requests...</p>
						{:else if requestHistoryLoaded}
							<div class="flex gap-1.5 mb-4 flex-wrap">
								<button class="btn btn-sm rounded-full {requestHistoryFilter === 'all' ? 'btn-neutral' : 'btn-outline'}" onclick={() => (requestHistoryFilter = 'all')}>All ({requestHistory.length})</button>
								<button class="btn btn-sm rounded-full {requestHistoryFilter === 'Pending' ? 'btn-neutral' : 'btn-outline'}" onclick={() => (requestHistoryFilter = 'Pending')}>Pending ({requestHistory.filter(r => r.status === 'Pending').length})</button>
								<button class="btn btn-sm rounded-full {requestHistoryFilter === 'Accepted' ? 'btn-neutral' : 'btn-outline'}" onclick={() => (requestHistoryFilter = 'Accepted')}>Accepted ({requestHistory.filter(r => r.status === 'Accepted').length})</button>
								<button class="btn btn-sm rounded-full {requestHistoryFilter === 'Declined' ? 'btn-neutral' : 'btn-outline'}" onclick={() => (requestHistoryFilter = 'Declined')}>Declined ({requestHistory.filter(r => r.status === 'Declined').length})</button>
							</div>

							{#if filteredRequests().length === 0}
								<p class="text-base-content/40">No requests found.</p>
							{:else}
								<div class="flex flex-col gap-3">
									{#each filteredRequests() as req}
										<div class="bg-base-200/30 border border-base-300 rounded-lg p-4">
											<div class="flex items-center gap-2 mb-2">
												<span class="badge badge-primary badge-sm">{formatRequestType(req.type)}</span>
												<span class="badge badge-sm {statusBadgeClass(req.status) === 'status-pending' ? 'badge-warning' : statusBadgeClass(req.status) === 'status-accepted' ? 'badge-success' : 'badge-error'}">{req.status}</span>
											</div>
											<div class="text-sm">
												<div class="mb-1">
													<strong>{req.userFirstName} {req.userLastName}</strong>
													<span class="text-base-content/40 text-sm ml-1.5">{req.userEmail}</span>
												</div>
												{#if req.requestData}
													<div class="text-primary text-sm my-1 py-1 px-2 bg-primary/10 rounded-md inline-block">{parseRequestData(req.type, req.requestData).join(', ')}</div>
												{/if}
												{#if req.message}
													<div class="text-base-content/60 italic text-sm my-1">"{req.message}"</div>
												{/if}
												<div class="text-base-content/40 text-xs mt-1.5">
													<span>Created {formatTimeAgo(req.createdAt)}</span>
													{#if req.respondedAt}
														<span>&middot; {req.status === 'Accepted' ? 'Accepted' : 'Declined'} by {req.respondedByName ?? 'Unknown'} {formatTimeAgo(req.respondedAt)}</span>
													{/if}
													{#if req.relatedEntityId}
														<span>&middot; Entry #{req.relatedEntityId}</span>
													{/if}
												</div>
											</div>
											{#if req.status === 'Pending'}
												<div class="flex gap-1.5 mt-3 pt-3 border-t border-base-300">
													<button class="btn btn-success btn-sm" onclick={async () => {
														try {
															await requestsApi.apiV1OrganizationsSlugRequestsIdPut(orgSlug, req.id!, { accept: true });
															requestHistoryLoaded = false;
															await loadRequestHistory();
														} catch (e) {
															actionError = extractErrorMessage(e, 'Failed to accept');
														}
													}}>Accept</button>
													<button class="btn btn-outline btn-error btn-sm" onclick={async () => {
														try {
															await requestsApi.apiV1OrganizationsSlugRequestsIdPut(orgSlug, req.id!, { accept: false });
															requestHistoryLoaded = false;
															await loadRequestHistory();
														} catch (e) {
															actionError = extractErrorMessage(e, 'Failed to decline');
														}
													}}>Decline</button>
												</div>
											{/if}
										</div>
									{/each}
								</div>
							{/if}
						{/if}
					</section>
				</div>

			<!-- ==================== RULES TAB (Member read-only) ==================== -->
			{:else if activeTab === 'settings'}
				<div class="pt-2">
					<p class="text-base-content/50 text-sm mt-2 mb-5 leading-relaxed">Current organization rules and policies. Only admins can change these settings.</p>

					<section class="mt-10">
						<h2 class="text-xl text-base-content/70 mb-4">Organization Rules</h2>

						<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
							<!-- Toggle settings (read-only) -->
							{#each toggleSettings as ts}
								{@const value = (org as Record<string, any>)[ts.key]}
								<div class="flex items-center justify-between p-4 border-b border-base-200 last:border-b-0">
									<div class="setting-info">
										<div class="font-semibold text-base-content mb-0.5">{ts.label}</div>
										<div class="text-sm text-base-content/60 max-w-[400px]">{ts.description}</div>
									</div>
									<span class="badge {value ? 'badge-success' : 'badge-ghost'}">{value ? 'On' : 'Off'}</span>
								</div>
							{/each}

							<!-- Rule mode settings (read-only) -->
							{#each ruleSettings as rs}
								{@const mode = (org as Record<string, any>)[rs.key] as string | null}
								{@const label = rs.labelFn ? rs.labelFn(mode) : ruleModeLabel(mode)}
								<div class="flex items-center justify-between p-4 border-b border-base-200 last:border-b-0">
									<div class="setting-info">
										<div class="font-semibold text-base-content mb-0.5">{rs.label}</div>
										<div class="text-sm text-base-content/60 max-w-[400px]">{rs.description}</div>
									</div>
									<span class="badge {ruleModeButtonClass(mode)}">{label}</span>
								</div>
							{/each}

							<!-- Visibility settings (read-only) -->
							{#each visibilitySettings as vs}
								{@const mode = (org as Record<string, any>)[vs.key] as string | null}
								<div class="flex items-center justify-between p-4 border-b border-base-200 last:border-b-0">
									<div class="setting-info">
										<div class="font-semibold text-base-content mb-0.5">{vs.label}</div>
										<div class="text-sm text-base-content/60 max-w-[400px]">{vs.description}</div>
									</div>
									<span class="badge {visibilityModeButtonClass(mode)}">{visibilityModeLabel(mode)}</span>
								</div>
							{/each}
						</div>

						{#if org.autoPauseEnabled}
							<div class="mt-6 pt-4 border-t border-base-300">
								<h3 class="text-base font-bold text-base-content mb-3">Pause Rules</h3>
								{#if (org.pauseRules ?? []).length === 0}
									<p class="text-base-content/40">No pause rules configured.</p>
								{:else}
									<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
										{#each (org.pauseRules ?? []) as rule}
											<div class="p-2 bg-base-200/50 border border-base-300 rounded-lg text-sm text-base-content/70 mb-2">
												<strong>&ge; {rule.minHours}h</strong> tracked &rarr; <strong>{rule.pauseMinutes} min</strong> pause deducted
											</div>
										{/each}
									</div>
								{/if}
							</div>
						{/if}
					</section>
				</div>
			{/if}
		{/if}
	{/if}
</div>

