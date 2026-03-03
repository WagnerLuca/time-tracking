<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { goto } from '$app/navigation';
	import { auth } from '$lib/stores/auth.svelte';
	import { organizationsApi, pauseRulesApi, workScheduleApi, requestsApi, holidayApi, absenceDayApi, notificationsApi } from '$lib/apiClient';
	import type {
		OrganizationDetailResponse,
		UpdateOrganizationRequest,
		AddMemberRequest,
		UpdateOrganizationSettingsRequest,
		PauseRuleResponse,
		CreatePauseRuleRequest,
		RuleMode,
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
	let activeTab = $state<'my-schedule' | 'team' | 'settings'>('my-schedule');

	// Team overview data (admin)
	let teamOverview = $state<MemberTimeOverviewResponse[]>([]);
	let teamOverviewLoading = $state(false);
	let teamOverviewLoaded = $state(false);

	// Work schedule (for member view)
	let workSchedule = $state<WorkScheduleResponse | null>(null);

	// Admin: member schedule editing
	let editingMemberSchedule = $state<number | null>(null);
	let memberSchedule = $state<WorkScheduleResponse | null>(null);
	let memberScheduleLoading = $state(false);
	let memberScheduleSaving = $state(false);
	let memberScheduleError = $state('');
	let memberWeeklyHours = $state<number | null>(null);
	let memberDistributeEvenly = $state(true);
	let memberTargetMon = $state(0);
	let memberTargetTue = $state(0);
	let memberTargetWed = $state(0);
	let memberTargetThu = $state(0);
	let memberTargetFri = $state(0);
	let memberOvertimeHours = $state(0);

	// Self schedule editing (inline on org page)
	let editingMySchedule = $state(false);
	let myWeeklyHours = $state<number | null>(null);
	let myDistributeEvenly = $state(true);
	let myTargetMon = $state(0);
	let myTargetTue = $state(0);
	let myTargetWed = $state(0);
	let myTargetThu = $state(0);
	let myTargetFri = $state(0);
	let myScheduleSaving = $state(false);
	let myScheduleError = $state('');

	// Initial overtime editing (inline)
	let editingMyOvertime = $state(false);
	let myOvertimeHours = $state(0);
	let myOvertimeSaving = $state(false);
	let myOvertimeError = $state('');

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
	let editHolidayRecurring = $state(false);
	let importingHolidays = $state(false);
	let importPreset = $state('de');
	let importYear = $state(new Date().getFullYear());

	// Absences / sick days
	let absences = $state<AbsenceDayResponse[]>([]);
	let absencesLoading = $state(false);
	let absencesLoaded = $state(false);
	let showAddAbsence = $state(false);
	let newAbsenceDate = $state('');
	let newAbsenceToDate = $state('');
	let newAbsenceType = $state(0); // SickDay
	let newAbsenceNote = $state('');
	let addingAbsence = $state(false);
	let absenceError = $state('');
	// Admin absence
	let showAdminAddAbsence = $state(false);
	let adminAbsenceUserId = $state<number | null>(null);
	let adminAbsenceDate = $state('');
	let adminAbsenceType = $state(0);
	let adminAbsenceNote = $state('');
	let addingAdminAbsence = $state(false);
	let adminAbsenceError = $state('');
	let adminAbsenceFilter = $state<number | null>(null); // userId filter

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
	// Admin schedule periods for member
	let adminPeriodsForMember = $state<number | null>(null);
	let adminPeriods = $state<WorkScheduleResponse[]>([]);
	let adminPeriodsLoading = $state(false);
	let showAdminAddPeriod = $state(false);
	let adminNewPeriodFrom = $state('');
	let adminNewPeriodTo = $state('');
	let adminNewPeriodWeeklyHours = $state<number | null>(null);
	let adminNewPeriodDistributeEvenly = $state(true);
	let adminNewPeriodMon = $state(0);
	let adminNewPeriodTue = $state(0);
	let adminNewPeriodWed = $state(0);
	let adminNewPeriodThu = $state(0);
	let adminNewPeriodFri = $state(0);
	let addingAdminPeriod = $state(false);
	let adminPeriodError = $state('');

	// Settings change notification
	let showSettingsChangedBanner = $state(false);
	let settingsNotificationIds: number[] = $state([]);

	let orgSlug: string;

	onMount(() => {
		orgSlug = $page.params.slug ?? '';
		loadOrg();
	});

	async function loadOrg() {
		loading = true;
		error = '';
		try {
			const { data } = await organizationsApi.apiOrganizationsSlugGet(orgSlug);
			org = data;

			// Check for unread settings-change notifications from backend
			try {
				const resp = await notificationsApi.apiNotificationsGet(true) as any;
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
				const { data: ws } = await workScheduleApi.apiOrganizationsSlugWorkScheduleGet(orgSlug);
				workSchedule = ws;
			} catch {
				workSchedule = null;
			}
			// Auto-load all lazy sections in parallel
			loadAllSections();
		} catch (err: any) {
			error = err.response?.status === 404 ? 'Organization not found.' : 'Failed to load organization.';
		} finally {
			loading = false;
		}
	}

	async function dismissSettingsNotifications() {
		showSettingsChangedBanner = false;
		// Mark all settings-change notifications as read on the backend
		for (const nid of settingsNotificationIds) {
			try {
				await notificationsApi.apiNotificationsIdReadPut(nid);
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
			const { data } = await organizationsApi.apiOrganizationsSlugTimeOverviewGet(orgSlug, weekStart.toISOString(), weekEnd.toISOString());
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
			const { data } = await organizationsApi.apiOrganizationsSlugGet(orgSlug);
			org = data;
			try {
				const { data: ws } = await workScheduleApi.apiOrganizationsSlugWorkScheduleGet(orgSlug);
				workSchedule = ws;
			} catch {
				workSchedule = null;
			}
		} catch (err: any) {
			actionError = err.response?.data?.message || 'Failed to reload organization.';
		}
	}

	async function loadRequestHistory() {
		if (requestHistoryLoaded) return;
		requestHistoryLoading = true;
		try {
			const { data } = await requestsApi.apiOrganizationsSlugRequestsGet(orgSlug);
			requestHistory = data as OrgRequestResponse[];
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

	function formatRequestType(type?: string | null): string {
		switch (type) {
			case 'JoinOrganization': return 'Join';
			case 'EditPastEntry': return 'Edit Entry';
			case 'EditPause': return 'Edit Pause';
			case 'SetInitialOvertime': return 'Set Overtime';
			default: return type ?? 'Unknown';
		}
	}

	function formatTimeAgo(dateStr?: string): string {
		if (!dateStr) return '';
		const diff = Date.now() - new Date(dateStr).getTime();
		const mins = Math.floor(diff / 60000);
		if (mins < 1) return 'just now';
		if (mins < 60) return `${mins}m ago`;
		const hours = Math.floor(mins / 60);
		if (hours < 24) return `${hours}h ago`;
		const days = Math.floor(hours / 24);
		return `${days}d ago`;
	}

	function statusBadgeClass(status?: string | null): string {
		switch (status) {
			case 'Accepted': return 'status-accepted';
			case 'Declined': return 'status-declined';
			case 'Pending': return 'status-pending';
			default: return '';
		}
	}

	function parseRequestData(type?: string | null, data?: string | null): string {
		if (!data) return '';
		try {
			if (type === 'EditPastEntry') {
				const obj = JSON.parse(data);
				const parts: string[] = [];
				if (obj.startTime) parts.push(`Start: ${new Date(obj.startTime).toLocaleString()}`);
				if (obj.endTime) parts.push(`End: ${new Date(obj.endTime).toLocaleString()}`);
				if (obj.description !== undefined) parts.push(`Note: ${obj.description}`);
				return parts.join(', ');
			} else if (type === 'EditPause') {
				return `Pause: ${data} min`;
			} else if (type === 'SetInitialOvertime') {
				return `Overtime: ${data}h`;
			}
		} catch { /* fallback */ }
		return data;
	}

	// ── Admin: Member Schedule ──
	async function openMemberSchedule(memberId: number) {
		if (editingMemberSchedule === memberId) {
			editingMemberSchedule = null;
			return;
		}
		editingMemberSchedule = memberId;
		memberScheduleLoading = true;
		memberScheduleError = '';
		try {
			const { data } = await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkScheduleGet(orgSlug, memberId);
			memberSchedule = data;
			memberWeeklyHours = data.weeklyWorkHours ?? null;
			memberTargetMon = data.targetMon ?? 0;
			memberTargetTue = data.targetTue ?? 0;
			memberTargetWed = data.targetWed ?? 0;
			memberTargetThu = data.targetThu ?? 0;
			memberTargetFri = data.targetFri ?? 0;
			memberOvertimeHours = data.initialOvertimeHours ?? 0;
			const allEqual = memberTargetMon === memberTargetTue && memberTargetTue === memberTargetWed && memberTargetWed === memberTargetThu && memberTargetThu === memberTargetFri;
			memberDistributeEvenly = allEqual;
		} catch {
			memberSchedule = null;
		} finally {
			memberScheduleLoading = false;
		}
	}

	async function saveMemberSchedule() {
		if (!editingMemberSchedule) return;
		memberScheduleSaving = true;
		memberScheduleError = '';
		try {
			const scheduleId = memberSchedule?.id;
			let data: WorkScheduleResponse;
			if (scheduleId && scheduleId > 0) {
				// Update existing schedule
				const payload = {
					weeklyWorkHours: memberWeeklyHours ?? undefined,
					distributeEvenly: memberDistributeEvenly,
					targetMon: memberDistributeEvenly ? undefined : memberTargetMon,
					targetTue: memberDistributeEvenly ? undefined : memberTargetTue,
					targetWed: memberDistributeEvenly ? undefined : memberTargetWed,
					targetThu: memberDistributeEvenly ? undefined : memberTargetThu,
					targetFri: memberDistributeEvenly ? undefined : memberTargetFri,
				};
				const resp = await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkSchedulesIdPut(orgSlug, editingMemberSchedule, scheduleId, payload);
				data = resp.data;
			} else {
				// Create new schedule
				const today = new Date();
				const validFrom = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
				const payload = {
					validFrom,
					weeklyWorkHours: memberWeeklyHours ?? undefined,
					distributeEvenly: memberDistributeEvenly,
					targetMon: memberDistributeEvenly ? undefined : memberTargetMon,
					targetTue: memberDistributeEvenly ? undefined : memberTargetTue,
					targetWed: memberDistributeEvenly ? undefined : memberTargetWed,
					targetThu: memberDistributeEvenly ? undefined : memberTargetThu,
					targetFri: memberDistributeEvenly ? undefined : memberTargetFri,
				};
				const resp = await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkSchedulesPost(orgSlug, editingMemberSchedule, payload);
				data = resp.data;
			}
			memberSchedule = data;
			memberWeeklyHours = data.weeklyWorkHours ?? null;
			memberTargetMon = data.targetMon ?? 0;
			memberTargetTue = data.targetTue ?? 0;
			memberTargetWed = data.targetWed ?? 0;
			memberTargetThu = data.targetThu ?? 0;
			memberTargetFri = data.targetFri ?? 0;
			memberOvertimeHours = data.initialOvertimeHours ?? 0;
			actionError = '';
		} catch (err: any) {
			memberScheduleError = err.response?.data?.message || 'Failed to save schedule.';
		} finally {
			memberScheduleSaving = false;
		}
	}

	function startEditMySchedule() {
		if (!workSchedule) return;
		myWeeklyHours = workSchedule.weeklyWorkHours ?? null;
		myTargetMon = workSchedule.targetMon ?? 0;
		myTargetTue = workSchedule.targetTue ?? 0;
		myTargetWed = workSchedule.targetWed ?? 0;
		myTargetThu = workSchedule.targetThu ?? 0;
		myTargetFri = workSchedule.targetFri ?? 0;
		myDistributeEvenly = myTargetMon === myTargetTue && myTargetTue === myTargetWed && myTargetWed === myTargetThu && myTargetThu === myTargetFri;
		myScheduleError = '';
		editingMySchedule = true;
	}

	async function saveMySchedule() {
		myScheduleSaving = true;
		myScheduleError = '';
		try {
			const scheduleId = workSchedule?.id;
			let data: WorkScheduleResponse;
			if (scheduleId && scheduleId > 0) {
				const payload = {
					weeklyWorkHours: myWeeklyHours ?? undefined,
					distributeEvenly: myDistributeEvenly,
					targetMon: myDistributeEvenly ? undefined : myTargetMon,
					targetTue: myDistributeEvenly ? undefined : myTargetTue,
					targetWed: myDistributeEvenly ? undefined : myTargetWed,
					targetThu: myDistributeEvenly ? undefined : myTargetThu,
					targetFri: myDistributeEvenly ? undefined : myTargetFri,
				};
				const resp = await workScheduleApi.apiOrganizationsSlugWorkSchedulesIdPut(orgSlug, scheduleId, payload);
				data = resp.data;
			} else {
				const today = new Date();
				const validFrom = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
				const payload = {
					validFrom,
					weeklyWorkHours: myWeeklyHours ?? undefined,
					distributeEvenly: myDistributeEvenly,
					targetMon: myDistributeEvenly ? undefined : myTargetMon,
					targetTue: myDistributeEvenly ? undefined : myTargetTue,
					targetWed: myDistributeEvenly ? undefined : myTargetWed,
					targetThu: myDistributeEvenly ? undefined : myTargetThu,
					targetFri: myDistributeEvenly ? undefined : myTargetFri,
				};
				const resp = await workScheduleApi.apiOrganizationsSlugWorkSchedulesPost(orgSlug, payload);
				data = resp.data;
			}
			workSchedule = data;
			editingMySchedule = false;
		} catch (err: any) {
			myScheduleError = err.response?.data?.message || 'Failed to save schedule.';
		} finally {
			myScheduleSaving = false;
		}
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
			await workScheduleApi.apiOrganizationsSlugInitialOvertimePut(orgSlug, { initialOvertimeHours: myOvertimeHours });
			// Reload work schedule
			const { data: ws } = await workScheduleApi.apiOrganizationsSlugWorkScheduleGet(orgSlug);
			workSchedule = ws;
			editingMyOvertime = false;
		} catch (err: any) {
			myOvertimeError = err.response?.data?.message || 'Failed to save overtime.';
		} finally {
			myOvertimeSaving = false;
		}
	}

	async function loadUsersForDropdown() {
		if (usersLoaded) return;
		try {
			const { data: users } = await organizationsApi.apiOrganizationsSlugGet(orgSlug).then(r => r.data?.members ?? []) as any[];
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
			await organizationsApi.apiOrganizationsSlugPut(orgSlug, payload);
			await reloadOrg();
			editing = false;
		} catch (err: any) {
			editError = err.response?.data?.message || 'Failed to update organization.';
		} finally {
			editSaving = false;
		}
	}

	async function deleteOrg() {
		if (!confirm('Are you sure you want to delete this organization? This cannot be undone.'))
			return;
		try {
			await organizationsApi.apiOrganizationsSlugDelete(orgSlug);
			goto('/organizations');
		} catch (err: any) {
			actionError = err.response?.data?.message || 'Failed to delete organization.';
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
			await organizationsApi.apiOrganizationsSlugMembersPost(orgSlug, payload);
			await reloadOrg();
			showAddMember = false;
			selectedUserId = null;
			newMemberRole = 0;
		} catch (err: any) {
			addMemberError = err.response?.data?.message || 'Failed to add member.';
		} finally {
			addingMember = false;
		}
	}

	async function updateMemberRole(userId: number, newRole: number) {
		actionError = '';
		try {
			await organizationsApi.apiOrganizationsSlugMembersMemberIdPut(orgSlug, userId, { role: newRole as any });
			await reloadOrg();
		} catch (err: any) {
			actionError = err.response?.data?.message || 'Failed to update member role.';
		}
	}

	async function removeMember(userId: number, memberName: string) {
		if (!confirm(`Remove ${memberName} from this organization?`)) return;
		actionError = '';
		try {
			await organizationsApi.apiOrganizationsSlugMembersMemberIdDelete(orgSlug, userId);
			await reloadOrg();
		} catch (err: any) {
			actionError = err.response?.data?.message || 'Failed to remove member.';
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

	async function toggleAutoPause() {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const payload: UpdateOrganizationSettingsRequest = {
				autoPauseEnabled: !org.autoPauseEnabled
			};
			await organizationsApi.apiOrganizationsSlugSettingsPut(orgSlug, payload);
			await reloadOrg();
		} catch (err: any) {
			settingsError = err.response?.data?.message || 'Failed to update setting.';
		} finally {
			settingsSaving = false;
		}
	}

	async function cycleEditPastEntriesMode() {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const modes: RuleMode[] = [0, 1, 2]; // Disabled, RequiresApproval, Allowed
			const current = parseRuleMode(org.editPastEntriesMode);
			const next = modes[(current + 1) % 3] as RuleMode;
			const payload: UpdateOrganizationSettingsRequest = {
				editPastEntriesMode: next
			};
			await organizationsApi.apiOrganizationsSlugSettingsPut(orgSlug, payload);
			await reloadOrg();
		} catch (err: any) {
			settingsError = err.response?.data?.message || 'Failed to update setting.';
		} finally {
			settingsSaving = false;
		}
	}

	async function cycleEditPauseMode() {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const modes: RuleMode[] = [0, 1, 2];
			const current = parseRuleMode(org.editPauseMode);
			const next = modes[(current + 1) % 3] as RuleMode;
			const payload: UpdateOrganizationSettingsRequest = {
				editPauseMode: next
			};
			await organizationsApi.apiOrganizationsSlugSettingsPut(orgSlug, payload);
			await reloadOrg();
		} catch (err: any) {
			settingsError = err.response?.data?.message || 'Failed to update setting.';
		} finally {
			settingsSaving = false;
		}
	}

	async function cycleInitialOvertimeMode() {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const modes: RuleMode[] = [0, 1, 2];
			const current = parseRuleMode(org.initialOvertimeMode);
			const next = modes[(current + 1) % 3] as RuleMode;
			const payload: UpdateOrganizationSettingsRequest = {
				initialOvertimeMode: next
			};
			await organizationsApi.apiOrganizationsSlugSettingsPut(orgSlug, payload);
			await reloadOrg();
		} catch (err: any) {
			settingsError = err.response?.data?.message || 'Failed to update setting.';
		} finally {
			settingsSaving = false;
		}
	}

	async function cycleJoinPolicy() {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const modes: RuleMode[] = [0, 1, 2];
			const current = parseRuleMode(org.joinPolicy);
			const next = modes[(current + 1) % 3] as RuleMode;
			const payload: UpdateOrganizationSettingsRequest = {
				joinPolicy: next
			};
			await organizationsApi.apiOrganizationsSlugSettingsPut(orgSlug, payload);
			await reloadOrg();
		} catch (err: any) {
			settingsError = err.response?.data?.message || 'Failed to update setting.';
		} finally {
			settingsSaving = false;
		}
	}

	function parseRuleMode(mode: string | null | undefined): number {
		if (mode === 'Disabled') return 0;
		if (mode === 'RequiresApproval') return 1;
		if (mode === 'Allowed') return 2;
		return 2; // default to Allowed
	}

	function ruleModeLabel(mode: string | null | undefined): string {
		if (mode === 'Disabled') return 'Disabled';
		if (mode === 'RequiresApproval') return 'Requires Approval';
		return 'Allowed';
	}

	function joinPolicyLabel(mode: string | null | undefined): string {
		if (mode === 'Disabled') return 'Admin Only';
		if (mode === 'RequiresApproval') return 'Requires Approval';
		return 'Open';
	}

	function ruleModeColor(mode: string | null | undefined): string {
		if (mode === 'Disabled') return 'mode-disabled';
		if (mode === 'RequiresApproval') return 'mode-approval';
		return 'mode-allowed';
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
			await pauseRulesApi.apiOrganizationsSlugPauseRulesPost(orgSlug, payload);
			await reloadOrg();
			showAddRule = false;
			newRuleMinHours = 6;
			newRulePauseMinutes = 30;
		} catch (err: any) {
			addRuleError = err.response?.data?.message || err.response?.data || 'Failed to add rule.';
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
			await pauseRulesApi.apiOrganizationsSlugPauseRulesRuleIdPut(orgSlug, ruleId, {
				minHours: editRuleMinHours,
				pauseMinutes: editRulePauseMinutes
			});
			await reloadOrg();
			editingRuleId = null;
		} catch (err: any) {
			editRuleError = err.response?.data?.message || err.response?.data || 'Failed to update rule.';
		} finally {
			editingRuleSaving = false;
		}
	}

	async function deleteRule(ruleId: number) {
		if (!confirm('Delete this pause rule?')) return;
		try {
			await pauseRulesApi.apiOrganizationsSlugPauseRulesRuleIdDelete(orgSlug, ruleId);
			await reloadOrg();
		} catch (err: any) {
			settingsError = err.response?.data?.message || 'Failed to delete rule.';
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
			await organizationsApi.apiOrganizationsSlugMembersMemberIdInitialOvertimePut(
				orgSlug, userId, { initialOvertimeHours: editOvertimeMinutes }
			);
			editingOvertimeMemberId = null;
			await reloadOrg();
		} catch (err: any) {
			editOvertimeError = err.response?.data?.message || 'Failed to update overtime.';
		} finally {
			editOvertimeSaving = false;
		}
	}

	function formatOvertimeHours(minutes: number): string {
		if (minutes === 0) return '±0h';
		const sign = minutes > 0 ? '+' : '';
		return sign + (minutes / 60).toFixed(1) + 'h';
	}

	// ── Holidays ──
	async function loadHolidays() {
		if (holidaysLoaded) return;
		holidaysLoading = true;
		holidayError = '';
		try {
			const { data } = await holidayApi.apiOrganizationsSlugHolidaysGet(orgSlug);
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
			await holidayApi.apiOrganizationsSlugHolidaysPost(orgSlug, {
				date: newHolidayDate,
				name: newHolidayName,
				isRecurring: newHolidayRecurring
			});
			holidaysLoaded = false;
			await loadHolidays();
			showAddHoliday = false;
			newHolidayName = '';
			newHolidayDate = '';
			newHolidayRecurring = false;
		} catch (err: any) {
			holidayError = err.response?.data?.message || 'Failed to add holiday.';
		} finally {
			addingHoliday = false;
		}
	}

	function startEditHoliday(h: HolidayResponse) {
		editingHolidayId = h.id ?? null;
		editHolidayName = h.name ?? '';
		editHolidayDate = h.date ?? '';
		editHolidayRecurring = h.isRecurring ?? false;
	}

	async function saveEditHoliday(id: number) {
		editHolidaySaving = true;
		holidayError = '';
		try {
			await holidayApi.apiOrganizationsSlugHolidaysIdPut(orgSlug, id, {
				date: editHolidayDate,
				name: editHolidayName,
				isRecurring: editHolidayRecurring
			});
			editingHolidayId = null;
			holidaysLoaded = false;
			await loadHolidays();
		} catch (err: any) {
			holidayError = err.response?.data?.message || 'Failed to update holiday.';
		} finally {
			editHolidaySaving = false;
		}
	}

	async function deleteHoliday(id: number) {
		if (!confirm('Delete this holiday?')) return;
		try {
			await holidayApi.apiOrganizationsSlugHolidaysIdDelete(orgSlug, id);
			holidaysLoaded = false;
			await loadHolidays();
		} catch (err: any) {
			holidayError = err.response?.data?.message || 'Failed to delete holiday.';
		}
	}

	function formatDateDisplay(dateStr?: string): string {
		if (!dateStr) return '';
		try {
			const d = new Date(dateStr + 'T00:00:00');
			return d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });
		} catch { return dateStr; }
	}

	// ── Absences ──
	async function loadAbsences() {
		if (absencesLoaded) return;
		absencesLoading = true;
		absenceError = '';
		try {
			const { data } = await absenceDayApi.apiOrganizationsSlugAbsencesGet(orgSlug);
			absences = (data as AbsenceDayResponse[]).sort((a, b) => (b.date ?? '').localeCompare(a.date ?? ''));
			absencesLoaded = true;
		} catch {
			absences = [];
		} finally {
			absencesLoading = false;
		}
	}

	function absenceTypeLabel(type?: string | null): string {
		switch (type) {
			case 'SickDay': return 'Sick Day';
			case 'Vacation': return 'Vacation';
			case 'Other': return 'Other';
			default: return type ?? 'Unknown';
		}
	}

	function absenceTypeBadge(type?: string | null): string {
		switch (type) {
			case 'SickDay': return 'absence-sick';
			case 'Vacation': return 'absence-vacation';
			default: return 'absence-other';
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
				await absenceDayApi.apiOrganizationsSlugAbsencesPost(orgSlug, {
					date: dateStr,
					type: newAbsenceType as AbsenceType,
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
		} catch (err: any) {
			absenceError = err.response?.data?.message || 'Failed to add absence.';
		} finally {
			addingAbsence = false;
		}
	}

	async function deleteAbsence(id: number) {
		if (!confirm('Delete this absence?')) return;
		try {
			await absenceDayApi.apiOrganizationsSlugAbsencesIdDelete(orgSlug, id);
			absencesLoaded = false;
			await loadAbsences();
		} catch (err: any) {
			absenceError = err.response?.data?.message || 'Failed to delete absence.';
		}
	}

	// Admin absence
	async function addAdminAbsence(e: Event) {
		e.preventDefault();
		if (!adminAbsenceUserId) return;
		addingAdminAbsence = true;
		adminAbsenceError = '';
		try {
			await absenceDayApi.apiOrganizationsSlugAbsencesAdminPost(orgSlug, {
				userId: adminAbsenceUserId,
				date: adminAbsenceDate,
				type: adminAbsenceType as AbsenceType,
				note: adminAbsenceNote || undefined
			});
			absencesLoaded = false;
			await loadAbsences();
			showAdminAddAbsence = false;
			adminAbsenceDate = '';
			adminAbsenceType = 0;
			adminAbsenceNote = '';
			adminAbsenceUserId = null;
		} catch (err: any) {
			adminAbsenceError = err.response?.data?.message || 'Failed to add absence.';
		} finally {
			addingAdminAbsence = false;
		}
	}

	function filteredAbsences(): AbsenceDayResponse[] {
		if (!adminAbsenceFilter) return absences;
		return absences.filter(a => a.userId === adminAbsenceFilter);
	}

	// ── Schedule Periods ──
	async function loadSchedulePeriods() {
		if (schedulePeriodsLoaded) return;
		schedulePeriodsLoading = true;
		periodError = '';
		try {
			const { data } = await workScheduleApi.apiOrganizationsSlugWorkSchedulesGet(orgSlug);
			schedulePeriods = (data as WorkScheduleResponse[]).sort((a, b) => (b.validFrom ?? '').localeCompare(a.validFrom ?? ''));
			schedulePeriodsLoaded = true;
		} catch {
			schedulePeriods = [];
		} finally {
			schedulePeriodsLoading = false;
		}
	}

	async function addSchedulePeriod(e: Event) {
		e.preventDefault();
		addingPeriod = true;
		periodError = '';
		try {
			await workScheduleApi.apiOrganizationsSlugWorkSchedulesPost(orgSlug, {
				validFrom: newPeriodFrom,
				validTo: newPeriodTo || undefined,
				weeklyWorkHours: newPeriodWeeklyHours,
				distributeEvenly: newPeriodDistributeEvenly,
				targetMon: newPeriodDistributeEvenly ? undefined : newPeriodMon,
				targetTue: newPeriodDistributeEvenly ? undefined : newPeriodTue,
				targetWed: newPeriodDistributeEvenly ? undefined : newPeriodWed,
				targetThu: newPeriodDistributeEvenly ? undefined : newPeriodThu,
				targetFri: newPeriodDistributeEvenly ? undefined : newPeriodFri
			});
			schedulePeriodsLoaded = false;
			await loadSchedulePeriods();
			showAddPeriod = false;
			newPeriodFrom = '';
			newPeriodTo = '';
			newPeriodWeeklyHours = null;
		} catch (err: any) {
			periodError = err.response?.data?.message || 'Failed to add schedule period.';
		} finally {
			addingPeriod = false;
		}
	}

	async function deleteSchedulePeriod(id: number) {
		if (!confirm('Delete this schedule period?')) return;
		try {
			await workScheduleApi.apiOrganizationsSlugWorkSchedulesIdDelete(orgSlug, id);
			schedulePeriodsLoaded = false;
			await loadSchedulePeriods();
		} catch (err: any) {
			periodError = err.response?.data?.message || 'Failed to delete period.';
		}
	}

	// Admin: member schedule periods
	async function openMemberPeriods(memberId: number) {
		if (adminPeriodsForMember === memberId) {
			adminPeriodsForMember = null;
			return;
		}
		adminPeriodsForMember = memberId;
		adminPeriodsLoading = true;
		adminPeriodError = '';
		try {
			const { data } = await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkSchedulesGet(orgSlug, memberId);
			adminPeriods = (data as WorkScheduleResponse[]).sort((a, b) => (b.validFrom ?? '').localeCompare(a.validFrom ?? ''));
		} catch {
			adminPeriods = [];
		} finally {
			adminPeriodsLoading = false;
		}
	}

	async function addAdminSchedulePeriod(e: Event) {
		e.preventDefault();
		if (!adminPeriodsForMember) return;
		addingAdminPeriod = true;
		adminPeriodError = '';
		try {
			await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkSchedulesPost(orgSlug, adminPeriodsForMember, {
				validFrom: adminNewPeriodFrom,
				validTo: adminNewPeriodTo || undefined,
				weeklyWorkHours: adminNewPeriodWeeklyHours,
				distributeEvenly: adminNewPeriodDistributeEvenly,
				targetMon: adminNewPeriodDistributeEvenly ? undefined : adminNewPeriodMon,
				targetTue: adminNewPeriodDistributeEvenly ? undefined : adminNewPeriodTue,
				targetWed: adminNewPeriodDistributeEvenly ? undefined : adminNewPeriodWed,
				targetThu: adminNewPeriodDistributeEvenly ? undefined : adminNewPeriodThu,
				targetFri: adminNewPeriodDistributeEvenly ? undefined : adminNewPeriodFri
			});
			// reload
			const { data } = await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkSchedulesGet(orgSlug, adminPeriodsForMember);
			adminPeriods = (data as WorkScheduleResponse[]).sort((a, b) => (b.validFrom ?? '').localeCompare(a.validFrom ?? ''));
			showAdminAddPeriod = false;
			adminNewPeriodFrom = '';
			adminNewPeriodTo = '';
			adminNewPeriodWeeklyHours = null;
		} catch (err: any) {
			adminPeriodError = err.response?.data?.message || 'Failed to add schedule period.';
		} finally {
			addingAdminPeriod = false;
		}
	}

	async function deleteAdminSchedulePeriod(memberId: number, periodId: number) {
		if (!confirm('Delete this schedule period?')) return;
		try {
			await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkSchedulesIdDelete(orgSlug, memberId, periodId);
			const { data } = await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkSchedulesGet(orgSlug, memberId);
			adminPeriods = (data as WorkScheduleResponse[]).sort((a, b) => (b.validFrom ?? '').localeCompare(a.validFrom ?? ''));
		} catch (err: any) {
			adminPeriodError = err.response?.data?.message || 'Failed to delete period.';
		}
	}

	// Settings: cycle work schedule change mode
	async function cycleWorkScheduleChangeMode() {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const modes: RuleMode[] = [0, 1, 2];
			const current = parseRuleMode(org.workScheduleChangeMode);
			const next = modes[(current + 1) % 3] as RuleMode;
			const payload: UpdateOrganizationSettingsRequest = {
				workScheduleChangeMode: next
			};
			await organizationsApi.apiOrganizationsSlugSettingsPut(orgSlug, payload);
			await reloadOrg();
		} catch (err: any) {
			settingsError = err.response?.data?.message || 'Failed to update setting.';
		} finally {
			settingsSaving = false;
		}
	}

	async function toggleMemberTimeEntryVisibility() {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			const payload: UpdateOrganizationSettingsRequest = {
				memberTimeEntryVisibility: !org.memberTimeEntryVisibility
			};
			await organizationsApi.apiOrganizationsSlugSettingsPut(orgSlug, payload);
			await reloadOrg();
		} catch (err: any) {
			settingsError = err.response?.data?.message || 'Failed to update setting.';
		} finally {
			settingsSaving = false;
		}
	}

	async function importHolidays() {
		importingHolidays = true;
		holidayError = '';
		try {
			await holidayApi.apiOrganizationsSlugHolidaysImportPresetPost(orgSlug, importPreset, importYear);
			holidaysLoaded = false;
			await loadHolidays();
		} catch (err: any) {
			holidayError = err.response?.data?.message || 'Failed to import holidays.';
		} finally {
			importingHolidays = false;
		}
	}
</script>

<svelte:head>
	<title>{org ? org.name : 'Organization'} - Time Tracking</title>
</svelte:head>

<div class="page">
	<a href="/organizations" class="back-link">&larr; Back to Organizations</a>

	{#if loading}
		<div class="loading-state"><div class="spinner"></div><span>Loading...</span></div>
	{:else if error}
		<div class="error-msg">{error}</div>
	{:else if org}
		{#if actionError}
			<div class="error-banner">{actionError}</div>
		{/if}

		{#if editing}
			<!-- Edit form -->
			<div class="card">
				<h2>Edit Organization</h2>
				{#if editError}
					<div class="error-banner">{editError}</div>
				{/if}
				<form onsubmit={saveEdit}>
					<div class="field">
						<label for="editName">Name</label>
						<input id="editName" type="text" bind:value={editName} required disabled={editSaving} />
					</div>
					<div class="field">
						<label for="editSlug">Slug</label>
						<input id="editSlug" type="text" bind:value={editSlug} required disabled={editSaving} />
					</div>
					<div class="field">
						<label for="editDesc">Description</label>
						<textarea id="editDesc" bind:value={editDescription} rows="3" disabled={editSaving}></textarea>
					</div>
					<div class="field">
						<label for="editWeb">Website</label>
						<input id="editWeb" type="url" bind:value={editWebsite} disabled={editSaving} />
					</div>
					<div class="form-actions">
						<button type="button" class="btn-secondary" onclick={cancelEdit}>Cancel</button>
						<button type="submit" class="btn-primary" disabled={editSaving}>
							{editSaving ? 'Saving...' : 'Save Changes'}
						</button>
					</div>
				</form>
			</div>
		{:else}
			<!-- Organization header -->
			<div class="org-header">
				<div>
					<h1>{org.name}</h1>
					<span class="slug">/{org.slug}</span>
				</div>
				{#if canEdit}
					<div class="header-actions">
						{#if org.memberTimeEntryVisibility}
							<a href="/organizations/{orgSlug}/time-overview" class="btn-secondary">Time Overview</a>
						{/if}
						<button class="btn-secondary" onclick={startEdit}>Edit</button>
						{#if isOwner}
							<button class="btn-danger" onclick={deleteOrg}>Delete</button>
						{/if}
					</div>
				{/if}
			</div>

			{#if !canEdit && org.memberTimeEntryVisibility}
				<div class="visibility-warning">
					<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
					Your administrator can view your tracked working hours.
				</div>
			{/if}

			{#if showSettingsChangedBanner}
				<div class="settings-changed-banner">
					<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
					Organization rules have been updated. Check the <button class="link-btn" onclick={() => { activeTab = 'settings'; dismissSettingsNotifications(); }}>Rules</button> tab for details.
					<button class="dismiss-btn" onclick={() => dismissSettingsNotifications()}>&times;</button>
				</div>
			{/if}

			{#if org.description}
				<p class="description">{org.description}</p>
			{/if}

			{#if org.website}
				<a href={org.website} target="_blank" class="website-link">{org.website}</a>
			{/if}

			<!-- Tab navigation -->
			<div class="tab-bar">
				<button class="tab-btn" class:active={activeTab === 'my-schedule'} onclick={() => (activeTab = 'my-schedule')}>
					<svg class="tab-icon" viewBox="0 0 20 20" fill="currentColor"><path d="M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z"/></svg>
					My Schedule
				</button>
				<button class="tab-btn" class:active={activeTab === 'team'} onclick={() => (activeTab = 'team')}>
					<svg class="tab-icon" viewBox="0 0 20 20" fill="currentColor"><path d="M13 6a3 3 0 11-6 0 3 3 0 016 0zM18 8a2 2 0 11-4 0 2 2 0 014 0zM14 15a4 4 0 00-8 0v3h8v-3zM6 8a2 2 0 11-4 0 2 2 0 014 0zM16 18v-3a5.972 5.972 0 00-.75-2.906A3.005 3.005 0 0119 15v3h-3zM4.75 12.094A5.973 5.973 0 004 15v3H1v-3a3 3 0 013.75-2.906z"/></svg>
					Team
				</button>
				<button class="tab-btn" class:active={activeTab === 'settings'} onclick={() => (activeTab = 'settings')}>
					<svg class="tab-icon" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M11.49 3.17c-.38-1.56-2.6-1.56-2.98 0a1.532 1.532 0 01-2.286.948c-1.372-.836-2.942.734-2.106 2.106.54.886.061 2.042-.947 2.287-1.561.379-1.561 2.6 0 2.978a1.532 1.532 0 01.947 2.287c-.836 1.372.734 2.942 2.106 2.106a1.532 1.532 0 012.287.947c.379 1.561 2.6 1.561 2.978 0a1.533 1.533 0 012.287-.947c1.372.836 2.942-.734 2.106-2.106a1.533 1.533 0 01.947-2.287c1.561-.379 1.561-2.6 0-2.978a1.532 1.532 0 01-.947-2.287c.836-1.372-.734-2.942-2.106-2.106a1.532 1.532 0 01-2.287-.947zM10 13a3 3 0 100-6 3 3 0 000 6z" clip-rule="evenodd"/></svg>
					{canEdit ? 'Settings' : 'Rules'}
				</button>
			</div>

			<!-- ==================== MY SCHEDULE TAB ==================== -->
			{#if activeTab === 'my-schedule'}
				<div class="tab-content">
					<p class="tab-description">Your personal work schedule, overtime balance, absences and time period configurations.</p>
					<!-- My Work Schedule -->
					{#if myRole && workSchedule}
						<section class="schedule-overview-section">
							<div class="section-header-row">
								<h2>My Work Schedule</h2>
								{#if !editingMySchedule && workSchedule.workScheduleChangeMode !== 'Disabled'}
									<button class="btn-schedule-sm" onclick={startEditMySchedule}>Edit</button>
								{/if}
							</div>

							{#if editingMySchedule}
								<div class="my-schedule-form">
									{#if myScheduleError}
										<div class="inline-error">{myScheduleError}</div>
									{/if}
									<div class="schedule-form-row">
										<label>Weekly Hours</label>
										<input type="number" class="input-xs" bind:value={myWeeklyHours} min="0" max="168" step="0.5" />
									</div>
									<div class="schedule-form-row">
										<label class="checkbox-label-sm">
											<input type="checkbox" bind:checked={myDistributeEvenly} />
											Distribute evenly (Mon–Fri)
										</label>
									</div>
									{#if !myDistributeEvenly}
										<div class="schedule-day-targets">
											<div class="day-target-sm"><span>Mon</span><input type="number" bind:value={myTargetMon} min="0" max="24" step="0.5" /></div>
											<div class="day-target-sm"><span>Tue</span><input type="number" bind:value={myTargetTue} min="0" max="24" step="0.5" /></div>
											<div class="day-target-sm"><span>Wed</span><input type="number" bind:value={myTargetWed} min="0" max="24" step="0.5" /></div>
											<div class="day-target-sm"><span>Thu</span><input type="number" bind:value={myTargetThu} min="0" max="24" step="0.5" /></div>
											<div class="day-target-sm"><span>Fri</span><input type="number" bind:value={myTargetFri} min="0" max="24" step="0.5" /></div>
										</div>
									{/if}
									<div class="schedule-form-actions">
										<button class="btn-primary-sm" onclick={saveMySchedule} disabled={myScheduleSaving}>
											{myScheduleSaving ? 'Saving...' : 'Save'}
										</button>
										<button class="btn-secondary-sm" onclick={() => (editingMySchedule = false)}>Cancel</button>
									</div>
								</div>
							{:else}
								<div class="schedule-overview-grid">
									<div class="schedule-stat">
										<span class="schedule-stat-label">Weekly Hours</span>
										<span class="schedule-stat-value">{workSchedule.weeklyWorkHours ?? '—'}h</span>
									</div>
									<div class="schedule-stat">
										<span class="schedule-stat-label">Mon</span>
										<span class="schedule-stat-value">{workSchedule.targetMon ?? 0}h</span>
									</div>
									<div class="schedule-stat">
										<span class="schedule-stat-label">Tue</span>
										<span class="schedule-stat-value">{workSchedule.targetTue ?? 0}h</span>
									</div>
									<div class="schedule-stat">
										<span class="schedule-stat-label">Wed</span>
										<span class="schedule-stat-value">{workSchedule.targetWed ?? 0}h</span>
									</div>
									<div class="schedule-stat">
										<span class="schedule-stat-label">Thu</span>
										<span class="schedule-stat-value">{workSchedule.targetThu ?? 0}h</span>
									</div>
									<div class="schedule-stat">
										<span class="schedule-stat-label">Fri</span>
										<span class="schedule-stat-value">{workSchedule.targetFri ?? 0}h</span>
									</div>
								</div>
							{/if}
						</section>

						<!-- Initial Overtime Balance -->
						{#if workSchedule.initialOvertimeMode !== 'Disabled'}
							<section class="schedule-overview-section overtime-section">
								<div class="section-header-row">
									<h2>Initial Overtime Balance</h2>
									{#if !editingMyOvertime && workSchedule.initialOvertimeMode === 'Allowed'}
										<button class="btn-schedule-sm" onclick={startEditMyOvertime}>Edit</button>
									{/if}
								</div>

								{#if editingMyOvertime}
									<div class="my-schedule-form">
										{#if myOvertimeError}
											<div class="inline-error">{myOvertimeError}</div>
										{/if}
										<div class="schedule-form-row">
											<label>Hours</label>
											<input type="number" class="input-xs" bind:value={myOvertimeHours} step="0.5" />
										</div>
										<div class="schedule-form-actions">
											<button class="btn-primary-sm" onclick={saveMyOvertime} disabled={myOvertimeSaving}>
												{myOvertimeSaving ? 'Saving...' : 'Save'}
											</button>
											<button class="btn-secondary-sm" onclick={() => (editingMyOvertime = false)}>Cancel</button>
										</div>
									</div>
								{:else}
									<div class="schedule-overview-grid">
										<div class="schedule-stat overtime-stat">
											<span class="schedule-stat-label">Hours</span>
											<span class="schedule-stat-value">{workSchedule.initialOvertimeHours ?? 0}h</span>
										</div>
										<div class="schedule-stat">
											<span class="schedule-stat-label">Mode</span>
											<span class="schedule-stat-value schedule-stat-value-sm">{ruleModeLabel(workSchedule.initialOvertimeMode)}</span>
										</div>
									</div>
									{#if workSchedule.initialOvertimeMode === 'RequiresApproval'}
										<p class="schedule-hint">Requires admin approval to change</p>
									{/if}
								{/if}
							</section>
						{/if}
					{/if}

					<!-- My Absences -->
					{#if myRole}
						<section class="schedule-overview-section">
							<div class="section-header-row">
								<h2>My Absences</h2>
								{#if absencesLoaded}
									<button class="btn-primary-sm" onclick={() => (showAddAbsence = !showAddAbsence)}>
										{showAddAbsence ? 'Cancel' : '+ Add Absence'}
									</button>
								{/if}
							</div>

							{#if absencesLoading}
								<p class="muted">Loading absences...</p>
							{:else if absencesLoaded}
								{#if absenceError}
									<div class="inline-error">{absenceError}</div>
								{/if}

								{#if showAddAbsence}
									<form onsubmit={addAbsence} class="period-form">
										<div class="period-form-row">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label>From</label>
											<input type="date" bind:value={newAbsenceDate} required disabled={addingAbsence} />
										</div>
										<div class="period-form-row">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label>To (optional)</label>
											<input type="date" bind:value={newAbsenceToDate} min={newAbsenceDate} disabled={addingAbsence} />
										</div>
										{#if newAbsenceDate && newAbsenceToDate && newAbsenceDate !== newAbsenceToDate}
											<p class="range-hint">Absences will be created for workdays (Mon–Fri) only.</p>
										{/if}
										<div class="period-form-row">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label>Type</label>
											<select bind:value={newAbsenceType} disabled={addingAbsence}>
												<option value={0}>Sick Day</option>
												<option value={1}>Vacation</option>
												<option value={2}>Other</option>
											</select>
										</div>
										<div class="period-form-row">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label>Note</label>
											<input type="text" bind:value={newAbsenceNote} placeholder="Optional note" disabled={addingAbsence} />
										</div>
										<div class="schedule-form-actions">
											<button type="submit" class="btn-primary-sm" disabled={addingAbsence}>
												{addingAbsence ? 'Adding...' : 'Add'}
											</button>
										</div>
									</form>
								{/if}

								{@const myAbsences = (absences ?? []).filter(a => a.userId === auth.user?.id)}
								{#if myAbsences.length === 0}
									<p class="muted">No absences recorded.</p>
								{:else}
									<div class="absences-list">
										{#each myAbsences as a}
											<div class="absence-row">
												<span class="absence-date">{formatDateDisplay(a.date)}</span>
												<span class="absence-badge {absenceTypeBadge(a.type)}">{absenceTypeLabel(a.type)}</span>
												{#if a.note}
													<span class="absence-note">{a.note}</span>
												{/if}
												<button class="btn-icon-danger" title="Delete" onclick={() => deleteAbsence(a.id!)}>&times;</button>
											</div>
										{/each}
									</div>
								{/if}
							{/if}
						</section>
					{/if}

					<!-- My Schedule Periods -->
					{#if myRole && workSchedule && workSchedule.workScheduleChangeMode !== 'Disabled'}
						<section class="schedule-overview-section">
							<div class="section-header-row">
								<h2>Schedule Periods</h2>
								{#if schedulePeriodsLoaded && workSchedule.workScheduleChangeMode === 'Allowed'}
									<button class="btn-primary-sm" onclick={() => (showAddPeriod = !showAddPeriod)}>
										{showAddPeriod ? 'Cancel' : '+ Add Period'}
									</button>
								{/if}
							</div>

							{#if workSchedule.workScheduleChangeMode === 'RequiresApproval'}
								<p class="schedule-hint">Schedule period changes require admin approval</p>
							{/if}

							{#if schedulePeriodsLoading}
								<p class="muted">Loading schedule periods...</p>
							{:else if schedulePeriodsLoaded}
								{#if periodError}
									<div class="inline-error">{periodError}</div>
								{/if}

								{#if showAddPeriod && workSchedule.workScheduleChangeMode === 'Allowed'}
									<form onsubmit={addSchedulePeriod} class="period-form">
										<div class="period-form-row">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label>From</label>
											<input type="date" bind:value={newPeriodFrom} required disabled={addingPeriod} />
										</div>
										<div class="period-form-row">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label>To (optional)</label>
											<input type="date" bind:value={newPeriodTo} disabled={addingPeriod} />
										</div>
										<div class="period-form-row">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label>Weekly Hours</label>
											<input type="number" bind:value={newPeriodWeeklyHours} step="0.5" min="0" max="168" disabled={addingPeriod} />
										</div>
										<div class="schedule-form-row">
											<label class="checkbox-label-sm">
												<input type="checkbox" bind:checked={newPeriodDistributeEvenly} disabled={addingPeriod} />
												Distribute evenly (Mon–Fri)
											</label>
										</div>
										{#if !newPeriodDistributeEvenly}
											<div class="schedule-day-targets">
												<div class="day-target-sm"><span>Mon</span><input type="number" bind:value={newPeriodMon} step="0.5" min="0" max="24" disabled={addingPeriod} /></div>
												<div class="day-target-sm"><span>Tue</span><input type="number" bind:value={newPeriodTue} step="0.5" min="0" max="24" disabled={addingPeriod} /></div>
												<div class="day-target-sm"><span>Wed</span><input type="number" bind:value={newPeriodWed} step="0.5" min="0" max="24" disabled={addingPeriod} /></div>
												<div class="day-target-sm"><span>Thu</span><input type="number" bind:value={newPeriodThu} step="0.5" min="0" max="24" disabled={addingPeriod} /></div>
												<div class="day-target-sm"><span>Fri</span><input type="number" bind:value={newPeriodFri} step="0.5" min="0" max="24" disabled={addingPeriod} /></div>
											</div>
										{/if}
										<div class="schedule-form-actions">
											<button type="submit" class="btn-primary-sm" disabled={addingPeriod}>
												{addingPeriod ? 'Adding...' : 'Add'}
											</button>
										</div>
									</form>
								{/if}

								{#if schedulePeriods.length === 0}
									<p class="muted">No schedule periods configured. The base work schedule is used.</p>
								{:else}
									<div class="periods-list">
										{#each schedulePeriods as p}
											{@const today = new Date().toISOString().slice(0, 10)}
											{@const isActive = p.validFrom && p.validFrom <= today && (!p.validTo || p.validTo >= today)}
											<div class="period-row" class:period-active={isActive}>
												<div class="period-dates">
													<span>{formatDateDisplay(p.validFrom)}</span>
													<span class="period-arrow">&rarr;</span>
													<span>{p.validTo ? formatDateDisplay(p.validTo) : 'ongoing'}</span>
												</div>
												{#if isActive}
													<span class="active-badge">Active</span>
												{/if}
												<span class="period-hours">{p.weeklyWorkHours ?? '—'}h/week</span>
												{#if workSchedule.workScheduleChangeMode === 'Allowed'}
													<button class="btn-icon-danger" title="Delete" onclick={() => deleteSchedulePeriod(p.id!)}>&times;</button>
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
				<div class="tab-content">
					<p class="tab-description">Your team at a glance. {#if canEdit}Click a member for details, schedule editing, and absence history.{:else}Click a member to see their profile.{/if}</p>

					<!-- Members -->
					<section class="members-section">
						<div class="section-header">
							<h2>Members ({(org.members ?? []).length})</h2>
							{#if canEdit}
								<button class="btn-primary-sm" onclick={() => { showAddMember = !showAddMember; if (showAddMember) loadUsersForDropdown(); }}>
									{showAddMember ? 'Cancel' : '+ Add Member'}
								</button>
							{/if}
						</div>

						{#if showAddMember && canEdit}
							<div class="add-member-form">
								{#if addMemberError}
									<div class="error-banner">{addMemberError}</div>
								{/if}
								<form onsubmit={addMember} class="inline-form">
									<select bind:value={selectedUserId} disabled={addingMember} class="user-select">
										<option value={null}>Select a user...</option>
										{#each getAvailableUsers() as user}
											<option value={user.id}>{user.firstName} {user.lastName} ({user.email})</option>
										{/each}
									</select>
									<select bind:value={newMemberRole} disabled={addingMember}>
										<option value={0}>Member</option>
										<option value={1}>Admin</option>
										{#if isOwner}<option value={2}>Owner</option>{/if}
									</select>
									<button type="submit" class="btn-primary-sm" disabled={addingMember}>
										{addingMember ? 'Adding...' : 'Add'}
									</button>
								</form>
							</div>
						{/if}

						<div class="team-cards">
							{#each (org.members ?? []) as member}
								{@const overview = getMemberOverview(member.id!)}
								<a
									class="team-card"
									href="/organizations/{orgSlug}/members/{member.id}"
									title="View {member.firstName}'s details"
								>
									<div class="team-card-avatar">
										{(member.firstName?.[0] ?? '').toUpperCase()}{(member.lastName?.[0] ?? '').toUpperCase()}
									</div>
									<div class="team-card-body">
										<div class="team-card-name">
											{member.firstName} {member.lastName}
											{#if member.id === auth.user?.id}
												<span class="you-badge">You</span>
											{/if}
										</div>
										<div class="team-card-email">{member.email}</div>
										{#if canEdit && org.memberTimeEntryVisibility && overview}
											<div class="team-card-stats">
												<span class="stat" title="Tracked this week">
													<svg viewBox="0 0 16 16" fill="currentColor" class="stat-icon"><path d="M8 3.5a.5.5 0 00-1 0V8a.5.5 0 00.252.434l3.5 2a.5.5 0 00.496-.868L8 7.71V3.5z"/><path d="M8 16A8 8 0 108 0a8 8 0 000 16zm7-8A7 7 0 111 8a7 7 0 0114 0z"/></svg>
													{formatMinutesToHours(overview.netTrackedMinutes)}
												</span>
												<span class="stat" title="Entries this week">
													<svg viewBox="0 0 16 16" fill="currentColor" class="stat-icon"><path d="M5 3.5h6A1.5 1.5 0 0112.5 5v6a1.5 1.5 0 01-1.5 1.5H5A1.5 1.5 0 013.5 11V5A1.5 1.5 0 015 3.5z"/></svg>
													{overview.entryCount ?? 0} entries
												</span>
												{#if overview.weeklyWorkHours}
													<span class="stat" title="Weekly target">
														<svg viewBox="0 0 16 16" fill="currentColor" class="stat-icon"><path d="M8 15A7 7 0 118 1a7 7 0 010 14zm0 1A8 8 0 108 0a8 8 0 000 16z"/><path d="M10.97 4.97a.235.235 0 00-.02.022L7.477 9.417 5.384 7.323a.75.75 0 00-1.06 1.06L6.97 11.03a.75.75 0 001.079-.02l3.992-4.99a.75.75 0 00-1.071-1.05z"/></svg>
														{overview.weeklyWorkHours}h/w
													</span>
												{/if}
											</div>
										{/if}
									</div>
									<div class="team-card-right">
										<span class="role-badge role-{(member.role?.toLowerCase() ?? 'member')}">{member.role}</span>
										<svg class="team-card-arrow" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clip-rule="evenodd"/></svg>
									</div>
								</a>
							{/each}
						</div>
					</section>

					<!-- Holidays -->
					{#if myRole}
						<section class="schedule-overview-section">
							<div class="section-header-row">
								<h2>Holidays</h2>
								{#if holidaysLoaded && canEdit}
									<button class="btn-primary-sm" onclick={() => (showAddHoliday = !showAddHoliday)}>
										{showAddHoliday ? 'Cancel' : '+ Add Holiday'}
									</button>
								{/if}
							</div>

							{#if holidaysLoading}
								<p class="muted">Loading holidays...</p>
							{:else if holidaysLoaded}
								{#if holidayError}
									<div class="inline-error">{holidayError}</div>
								{/if}

								{#if canEdit}
									<div class="import-holidays-row">
										<select bind:value={importPreset} class="input-xs" disabled={importingHolidays}>
											<option value="de">Germany</option>
											<option value="at">Austria</option>
											<option value="ch">Switzerland</option>
										</select>
										<input type="number" bind:value={importYear} class="input-xs" min="2020" max="2099" style="width:80px" disabled={importingHolidays} />
										<button class="btn-secondary-sm" onclick={importHolidays} disabled={importingHolidays}>
											{importingHolidays ? 'Importing...' : 'Import Holidays'}
										</button>
									</div>
								{/if}

								{#if showAddHoliday && canEdit}
									<form onsubmit={addHoliday} class="period-form">
										<div class="period-form-row">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label>Date</label>
											<input type="date" bind:value={newHolidayDate} required disabled={addingHoliday} />
										</div>
										<div class="period-form-row">
											<!-- svelte-ignore a11y_label_has_associated_control -->
											<label>Name</label>
											<input type="text" bind:value={newHolidayName} placeholder="e.g. Christmas" required disabled={addingHoliday} />
										</div>
										<div class="period-form-row">
											<label class="checkbox-label">
												<input type="checkbox" bind:checked={newHolidayRecurring} disabled={addingHoliday} />
												Yearly recurring
											</label>
										</div>
										<div class="schedule-form-actions">
											<button type="submit" class="btn-primary-sm" disabled={addingHoliday}>
												{addingHoliday ? 'Adding...' : 'Add'}
											</button>
										</div>
									</form>
								{/if}

								{#if holidays.length === 0}
									<p class="muted">No holidays configured.</p>
								{:else}
									<div class="holidays-list">
										{#each holidays as h}
											<div class="holiday-row">
												{#if editingHolidayId === h.id && canEdit}
													<input type="date" bind:value={editHolidayDate} class="input-xs" disabled={editHolidaySaving} />
													<input type="text" bind:value={editHolidayName} class="input-xs" disabled={editHolidaySaving} />
													<label class="checkbox-label compact">
														<input type="checkbox" bind:checked={editHolidayRecurring} disabled={editHolidaySaving} />
														Yearly
													</label>
													<div class="rule-actions">
														<button class="btn-primary-sm" onclick={() => saveEditHoliday(h.id!)} disabled={editHolidaySaving}>Save</button>
														<button class="btn-secondary-sm" onclick={() => (editingHolidayId = null)}>Cancel</button>
													</div>
												{:else}
													<span class="holiday-date">{formatDateDisplay(h.date)}</span>
													<span class="holiday-name">{h.name}</span>
													{#if h.isRecurring}
														<span class="recurring-badge" title="Repeats every year">&#x1f501; Yearly</span>
													{/if}
													{#if canEdit}
														<div class="rule-actions">
															<button class="btn-secondary-sm" onclick={() => startEditHoliday(h)}>Edit</button>
															<button class="btn-icon-danger" title="Delete" onclick={() => deleteHoliday(h.id!)}>&times;</button>
														</div>
													{/if}
												{/if}
											</div>
										{/each}
									</div>
								{/if}
							{/if}
						</section>
					{/if}
				</div>

			<!-- ==================== SETTINGS TAB (Admin) ==================== -->
			{:else if activeTab === 'settings' && canEdit}
				<div class="tab-content">
					<p class="tab-description">Organization-wide rules, permissions, and approval workflows.</p>

					<!-- Organization Settings -->
					<section class="settings-section">
						<h2>Organization Settings</h2>
						{#if settingsError}
							<div class="error-banner">{settingsError}</div>
						{/if}

						<div class="settings-grid">
							<div class="setting-row">
								<div class="setting-info">
									<div class="setting-label">Auto-Pause Tracking</div>
									<div class="setting-desc">Automatically deduct break time from tracked hours based on configurable rules.</div>
								</div>
								<button
									class="toggle-switch"
									class:active={org.autoPauseEnabled}
									onclick={toggleAutoPause}
									disabled={settingsSaving}
									aria-label="Toggle auto-pause"
								>
									<span class="toggle-knob"></span>
								</button>
							</div>

							<div class="setting-row">
								<div class="setting-info">
									<div class="setting-label">Edit Past Entries</div>
									<div class="setting-desc">Control whether members can edit start/end times of completed time entries.</div>
								</div>
								<button
									class="rule-mode-btn {ruleModeColor(org.editPastEntriesMode)}"
									onclick={cycleEditPastEntriesMode}
									disabled={settingsSaving}
								>
									{ruleModeLabel(org.editPastEntriesMode)}
								</button>
							</div>

							<div class="setting-row">
								<div class="setting-info">
									<div class="setting-label">Edit Pause Duration</div>
									<div class="setting-desc">Control whether members can override auto-deducted break time.</div>
								</div>
								<button
									class="rule-mode-btn {ruleModeColor(org.editPauseMode)}"
									onclick={cycleEditPauseMode}
									disabled={settingsSaving}
								>
									{ruleModeLabel(org.editPauseMode)}
								</button>
							</div>

							<div class="setting-row">
								<div class="setting-info">
									<div class="setting-label">Initial Overtime</div>
									<div class="setting-desc">Control whether members can set their own initial overtime balance.</div>
								</div>
								<button
									class="rule-mode-btn {ruleModeColor(org.initialOvertimeMode)}"
									onclick={cycleInitialOvertimeMode}
									disabled={settingsSaving}
								>
									{ruleModeLabel(org.initialOvertimeMode)}
								</button>
							</div>

							<div class="setting-row">
								<div class="setting-info">
									<div class="setting-label">Join Policy</div>
									<div class="setting-desc">Control how new members can join the organization.</div>
								</div>
								<button
									class="rule-mode-btn {ruleModeColor(org.joinPolicy)}"
									onclick={cycleJoinPolicy}
									disabled={settingsSaving}
								>
									{joinPolicyLabel(org.joinPolicy)}
								</button>
							</div>

							<div class="setting-row">
								<div class="setting-info">
									<div class="setting-label">Schedule Periods</div>
									<div class="setting-desc">Control whether members can create/modify their own schedule periods.</div>
								</div>
								<button
									class="rule-mode-btn {ruleModeColor(org.workScheduleChangeMode)}"
									onclick={cycleWorkScheduleChangeMode}
									disabled={settingsSaving}
								>
									{ruleModeLabel(org.workScheduleChangeMode)}
								</button>
							</div>

							<div class="setting-row">
								<div class="setting-info">
									<div class="setting-label">Member Time Visibility</div>
									<div class="setting-desc">When enabled, admins can view members' tracked working hours. Members will see a notification about this.</div>
								</div>
								<button
									class="toggle-switch"
									class:active={org.memberTimeEntryVisibility}
									onclick={toggleMemberTimeEntryVisibility}
									disabled={settingsSaving}
									aria-label="Toggle member time entry visibility"
								>
									<span class="toggle-knob"></span>
								</button>
							</div>
						</div>

						<!-- Pause Rules -->
						{#if org.autoPauseEnabled}
							<div class="pause-rules-section">
								<div class="section-header">
									<h3>Pause Rules</h3>
									<button class="btn-primary-sm" onclick={() => (showAddRule = !showAddRule)}>
										{showAddRule ? 'Cancel' : '+ Add Rule'}
									</button>
								</div>

								{#if showAddRule}
									<div class="add-rule-form">
										{#if addRuleError}
											<div class="error-banner">{addRuleError}</div>
										{/if}
										<form onsubmit={addPauseRule} class="inline-form">
											<div class="rule-field">
												<!-- svelte-ignore a11y_label_has_associated_control -->
												<label>When tracked &ge;</label>
												<input type="number" step="0.5" min="0.5" bind:value={newRuleMinHours} required disabled={addingRule} />
												<span>hours</span>
											</div>
											<div class="rule-field">
												<!-- svelte-ignore a11y_label_has_associated_control -->
												<label>deduct</label>
												<input type="number" min="1" bind:value={newRulePauseMinutes} required disabled={addingRule} />
												<span>min pause</span>
											</div>
											<button type="submit" class="btn-primary-sm" disabled={addingRule}>
												{addingRule ? 'Adding...' : 'Add Rule'}
											</button>
										</form>
									</div>
								{/if}

								{#if getPauseRules().length === 0}
									<p class="muted">No pause rules configured. Add rules to automatically deduct break time.</p>
								{:else}
									<div class="rules-list">
										{#each getPauseRules() as rule}
											<div class="rule-row">
												{#if editingRuleId === rule.id}
													{#if editRuleError}
														<div class="error-banner" style="width:100%">{editRuleError}</div>
													{/if}
													<div class="rule-edit-form">
														<div class="rule-field">
															<!-- svelte-ignore a11y_label_has_associated_control -->
															<label>&ge;</label>
															<input type="number" step="0.5" min="0.5" bind:value={editRuleMinHours} disabled={editingRuleSaving} />
															<span>h</span>
														</div>
														<div class="rule-field">
															<!-- svelte-ignore a11y_label_has_associated_control -->
															<label>&rarr;</label>
															<input type="number" min="1" bind:value={editRulePauseMinutes} disabled={editingRuleSaving} />
															<span>min</span>
														</div>
														<div class="rule-actions">
															<button class="btn-primary-sm" onclick={() => saveEditRule(rule.id!)} disabled={editingRuleSaving}>Save</button>
															<button class="btn-secondary-sm" onclick={cancelEditRule}>Cancel</button>
														</div>
													</div>
												{:else}
													<div class="rule-text">
														<strong>&ge; {rule.minHours}h</strong> tracked &rarr; <strong>{rule.pauseMinutes} min</strong> pause deducted
													</div>
													<div class="rule-actions">
														<button class="btn-secondary-sm" onclick={() => startEditRule(rule)}>Edit</button>
														<button class="btn-icon-danger" title="Delete rule" onclick={() => deleteRule(rule.id!)}>&times;</button>
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
					<section class="request-history-section">
						<div class="section-header-row">
							<h2>Request History</h2>
						</div>

						{#if requestHistoryLoading}
							<p class="muted">Loading requests...</p>
						{:else if requestHistoryLoaded}
							<div class="request-filters">
								<button class="filter-btn" class:active={requestHistoryFilter === 'all'} onclick={() => (requestHistoryFilter = 'all')}>All ({requestHistory.length})</button>
								<button class="filter-btn" class:active={requestHistoryFilter === 'Pending'} onclick={() => (requestHistoryFilter = 'Pending')}>Pending ({requestHistory.filter(r => r.status === 'Pending').length})</button>
								<button class="filter-btn" class:active={requestHistoryFilter === 'Accepted'} onclick={() => (requestHistoryFilter = 'Accepted')}>Accepted ({requestHistory.filter(r => r.status === 'Accepted').length})</button>
								<button class="filter-btn" class:active={requestHistoryFilter === 'Declined'} onclick={() => (requestHistoryFilter = 'Declined')}>Declined ({requestHistory.filter(r => r.status === 'Declined').length})</button>
							</div>

							{#if filteredRequests().length === 0}
								<p class="muted">No requests found.</p>
							{:else}
								<div class="request-list">
									{#each filteredRequests() as req}
										<div class="request-item">
											<div class="request-item-header">
												<span class="request-type-tag">{formatRequestType(req.type)}</span>
												<span class="request-status {statusBadgeClass(req.status)}">{req.status}</span>
											</div>
											<div class="request-item-body">
												<div class="request-user">
													<strong>{req.userFirstName} {req.userLastName}</strong>
													<span class="request-email">{req.userEmail}</span>
												</div>
												{#if req.requestData}
													<div class="request-data">{parseRequestData(req.type, req.requestData)}</div>
												{/if}
												{#if req.message}
													<div class="request-message">"{req.message}"</div>
												{/if}
												<div class="request-meta">
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
												<div class="request-item-actions">
													<button class="btn-accept-sm" onclick={async () => {
														try {
															await requestsApi.apiOrganizationsSlugRequestsIdPut(orgSlug, req.id!, { accept: true });
															requestHistoryLoaded = false;
															await loadRequestHistory();
														} catch (e: any) {
															actionError = e.response?.data?.message || 'Failed to accept';
														}
													}}>Accept</button>
													<button class="btn-decline-sm" onclick={async () => {
														try {
															await requestsApi.apiOrganizationsSlugRequestsIdPut(orgSlug, req.id!, { accept: false });
															requestHistoryLoaded = false;
															await loadRequestHistory();
														} catch (e: any) {
															actionError = e.response?.data?.message || 'Failed to decline';
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
				<div class="tab-content">
					<p class="tab-description">Current organization rules and policies. Only admins can change these settings.</p>

					<section class="settings-section">
						<h2>Organization Rules</h2>

						<div class="settings-grid">
							<div class="setting-row">
								<div class="setting-info">
									<div class="setting-label">Auto-Pause Tracking</div>
									<div class="setting-desc">Automatically deduct break time from tracked hours based on configurable rules.</div>
								</div>
								<span class="rule-status" class:active={org.autoPauseEnabled}>{org.autoPauseEnabled ? 'On' : 'Off'}</span>
							</div>

							<div class="setting-row">
								<div class="setting-info">
									<div class="setting-label">Edit Past Entries</div>
									<div class="setting-desc">Control whether members can edit start/end times of completed time entries.</div>
								</div>
								<span class="rule-status-badge {ruleModeColor(org.editPastEntriesMode)}">{ruleModeLabel(org.editPastEntriesMode)}</span>
							</div>

							<div class="setting-row">
								<div class="setting-info">
									<div class="setting-label">Edit Pause Duration</div>
									<div class="setting-desc">Control whether members can override auto-deducted break time.</div>
								</div>
								<span class="rule-status-badge {ruleModeColor(org.editPauseMode)}">{ruleModeLabel(org.editPauseMode)}</span>
							</div>

							<div class="setting-row">
								<div class="setting-info">
									<div class="setting-label">Initial Overtime</div>
									<div class="setting-desc">Control whether members can set their own initial overtime balance.</div>
								</div>
								<span class="rule-status-badge {ruleModeColor(org.initialOvertimeMode)}">{ruleModeLabel(org.initialOvertimeMode)}</span>
							</div>

							<div class="setting-row">
								<div class="setting-info">
									<div class="setting-label">Join Policy</div>
									<div class="setting-desc">Control how new members can join the organization.</div>
								</div>
								<span class="rule-status-badge {ruleModeColor(org.joinPolicy)}">{joinPolicyLabel(org.joinPolicy)}</span>
							</div>

							<div class="setting-row">
								<div class="setting-info">
									<div class="setting-label">Schedule Periods</div>
									<div class="setting-desc">Control whether members can create/modify their own schedule periods.</div>
								</div>
								<span class="rule-status-badge {ruleModeColor(org.workScheduleChangeMode)}">{ruleModeLabel(org.workScheduleChangeMode)}</span>
							</div>

							<div class="setting-row">
								<div class="setting-info">
									<div class="setting-label">Member Time Visibility</div>
									<div class="setting-desc">Whether admins can view members' tracked working hours.</div>
								</div>
								<span class="rule-status" class:active={org.memberTimeEntryVisibility}>{org.memberTimeEntryVisibility ? 'On' : 'Off'}</span>
							</div>
						</div>

						{#if org.autoPauseEnabled}
							<div class="pause-rules-readonly">
								<h3>Pause Rules</h3>
								{#if (org.pauseRules ?? []).length === 0}
									<p class="muted">No pause rules configured.</p>
								{:else}
									<div class="rules-list">
										{#each (org.pauseRules ?? []) as rule}
											<div class="rule-item-readonly">
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

<style>
	.visibility-warning {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.75rem 1rem;
		background: #fffbeb;
		border: 1px solid #f59e0b;
		border-radius: 8px;
		color: #92400e;
		font-size: 0.875rem;
		margin-bottom: 1rem;
	}
	.visibility-warning svg {
		flex-shrink: 0;
		color: #f59e0b;
	}

	.settings-changed-banner {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.75rem 1rem;
		background: #eff6ff;
		border: 1px solid #3b82f6;
		border-radius: 8px;
		color: #1e40af;
		font-size: 0.875rem;
		margin-bottom: 1rem;
	}
	.settings-changed-banner svg {
		flex-shrink: 0;
		color: #3b82f6;
	}
	.settings-changed-banner .link-btn {
		background: none;
		border: none;
		color: #2563eb;
		font-weight: 600;
		text-decoration: underline;
		cursor: pointer;
		padding: 0;
		font-size: inherit;
	}
	.settings-changed-banner .dismiss-btn {
		margin-left: auto;
		background: none;
		border: none;
		color: #6b7280;
		cursor: pointer;
		font-size: 1.25rem;
		padding: 0 0.25rem;
		line-height: 1;
	}
	.settings-changed-banner .dismiss-btn:hover { color: #1e40af; }

	.rule-status {
		font-size: 0.8125rem;
		font-weight: 600;
		padding: 0.25rem 0.75rem;
		border-radius: 999px;
		background: #f3f4f6;
		color: #6b7280;
	}
	.rule-status.active {
		background: #dcfce7;
		color: #16a34a;
	}

	.rule-status-badge {
		font-size: 0.75rem;
		font-weight: 600;
		padding: 0.25rem 0.75rem;
		border-radius: 999px;
	}

	.pause-rules-readonly {
		margin-top: 1.5rem;
		padding-top: 1rem;
		border-top: 1px solid #e5e7eb;
	}
	.pause-rules-readonly h3 {
		font-size: 1rem;
		margin: 0 0 0.75rem;
		color: #1a1a2e;
	}
	.rule-item-readonly {
		padding: 0.5rem 0.75rem;
		background: #f9fafb;
		border: 1px solid #e5e7eb;
		border-radius: 8px;
		font-size: 0.875rem;
		color: #374151;
		margin-bottom: 0.5rem;
	}

	.back-link {
		color: #6b7280;
		text-decoration: none;
		font-size: 0.875rem;
		display: inline-block;
		margin-bottom: 0.75rem;
	}

	.back-link:hover {
		color: #3b82f6;
	}

	.muted {
		color: #9ca3af;
	}

	.error-msg {
		color: #dc2626;
		background: #fef2f2;
		padding: 0.75rem 1rem;
		border-radius: 8px;
		border-left: 3px solid #dc2626;
	}

	.error-banner {
		background: #fef2f2;
		color: #dc2626;
		padding: 0.75rem 1rem;
		border-radius: 8px;
		margin-bottom: 1rem;
		font-size: 0.875rem;
		border-left: 3px solid #dc2626;
	}

	/* ====== Tab Navigation ====== */
	.tab-bar {
		display: flex;
		gap: 0;
		background: transparent;
		border-bottom: 2px solid #e2e8f0;
		margin: 1.25rem 0 0;
		padding: 0;
	}

	.tab-btn {
		position: relative;
		flex: 1;
		padding: 0.875rem 1.25rem;
		border: none;
		border-radius: 0;
		background: transparent;
		color: #64748b;
		font-size: 0.9rem;
		font-weight: 500;
		cursor: pointer;
		transition: color 0.2s ease, background 0.15s ease;
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 0.5rem;
		white-space: nowrap;
		letter-spacing: 0.01em;
	}

	.tab-btn::after {
		content: '';
		position: absolute;
		bottom: -2px;
		left: 0;
		right: 0;
		height: 3px;
		background: transparent;
		border-radius: 3px 3px 0 0;
		transition: background 0.2s ease;
	}

	.tab-btn:hover {
		color: #1e293b;
		background: #f8fafc;
	}

	.tab-btn.active {
		color: #2563eb;
		font-weight: 600;
	}

	.tab-btn.active::after {
		background: #2563eb;
	}

	.tab-icon {
		width: 1.125rem;
		height: 1.125rem;
		flex-shrink: 0;
	}

	.tab-content {
		animation: fadeTab 0.25s ease;
		padding-top: 0.5rem;
	}

	.tab-description {
		color: #64748b;
		font-size: 0.875rem;
		margin: 0.5rem 0 1.25rem;
		line-height: 1.5;
	}

	@keyframes fadeTab {
		from { opacity: 0; transform: translateY(6px); }
		to   { opacity: 1; transform: translateY(0); }
	}

	.loading-state {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		justify-content: center;
		padding: 3rem 0;
		color: #94a3b8;
	}

	.spinner {
		width: 1.25rem;
		height: 1.25rem;
		border: 2px solid #e2e8f0;
		border-top-color: #2563eb;
		border-radius: 50%;
		animation: spin 0.6s linear infinite;
	}

	@keyframes spin {
		to { transform: rotate(360deg); }
	}

	/* Organization header */
	.org-header {
		display: flex;
		align-items: flex-start;
		justify-content: space-between;
		margin-bottom: 0.5rem;
	}

	.org-header h1 {
		margin: 0;
		font-size: 1.75rem;
		color: #1a1a2e;
	}

	.slug {
		color: #9ca3af;
		font-size: 0.875rem;
	}

	.header-actions {
		display: flex;
		gap: 0.5rem;
	}

	.description {
		color: #4b5563;
		margin: 0.75rem 0;
	}

	.website-link {
		color: #3b82f6;
		font-size: 0.875rem;
		display: inline-block;
		margin-bottom: 1.5rem;
	}

	/* Members */
	.members-section {
		margin-top: 2rem;
	}

	/* Schedule overview */
	.schedule-overview-section {
		margin-top: 2rem;
		background: #f8fafc;
		border-radius: 10px;
		padding: 1.25rem;
		border: 1px solid #e5e7eb;
	}

	.schedule-overview-section h2 {
		margin: 0 0 0.75rem 0;
		font-size: 1.125rem;
	}

	.schedule-overview-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(90px, 1fr));
		gap: 0.5rem;
	}

	.schedule-stat {
		display: flex;
		flex-direction: column;
		align-items: center;
		background: white;
		padding: 0.5rem;
		border-radius: 8px;
		border: 1px solid #e5e7eb;
	}

	.schedule-stat-label {
		font-size: 0.75rem;
		color: #6b7280;
		font-weight: 500;
	}

	.schedule-stat-value {
		font-size: 1.1rem;
		font-weight: 700;
		color: #1a1a2e;
	}

	.schedule-stat-value-sm {
		font-size: 0.85rem;
		word-break: break-word;
	}

	.overtime-stat .schedule-stat-value {
		color: #3b82f6;
	}

	.schedule-hint {
		margin: 0.75rem 0 0;
		font-size: 0.8rem;
		color: #9ca3af;
	}

	.schedule-hint a {
		color: #3b82f6;
		text-decoration: none;
	}

	.schedule-hint a:hover {
		text-decoration: underline;
	}

	.section-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		margin-bottom: 1rem;
	}

	.section-header h2 {
		margin: 0;
		font-size: 1.25rem;
		color: #374151;
	}

	.add-member-form {
		background: #f9fafb;
		padding: 1rem;
		border-radius: 8px;
		margin-bottom: 1rem;
		border: 1px solid #e5e7eb;
	}

	.inline-form {
		display: flex;
		gap: 0.5rem;
		align-items: center;
		flex-wrap: wrap;
	}

	.inline-form input {
		flex: 1;
		min-width: 200px;
		padding: 0.5rem 0.75rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.875rem;
	}

	.inline-form select {
		padding: 0.5rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.875rem;
	}

	.user-select {
		flex: 1;
		min-width: 200px;
	}

	.members-list {
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		overflow: hidden;
	}

	/* Team card grid */
	.team-cards {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.team-card {
		display: flex;
		align-items: center;
		gap: 1rem;
		padding: 0.875rem 1rem;
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		cursor: pointer;
		transition: border-color 0.15s, box-shadow 0.15s, transform 0.1s;
		text-decoration: none;
		color: inherit;
	}

	.team-card:hover {
		border-color: #93c5fd;
		box-shadow: 0 2px 8px rgba(37, 99, 235, 0.08);
		transform: translateY(-1px);
	}

	.team-card-avatar {
		width: 2.5rem;
		height: 2.5rem;
		border-radius: 50%;
		background: linear-gradient(135deg, #3b82f6, #6366f1);
		color: white;
		display: flex;
		align-items: center;
		justify-content: center;
		font-size: 0.8125rem;
		font-weight: 600;
		flex-shrink: 0;
		letter-spacing: 0.03em;
	}

	.team-card-body {
		flex: 1;
		min-width: 0;
	}

	.team-card-name {
		font-weight: 600;
		color: #1e293b;
		font-size: 0.9375rem;
		display: flex;
		align-items: center;
		gap: 0.375rem;
	}

	.team-card-email {
		font-size: 0.8125rem;
		color: #94a3b8;
		margin-top: 0.125rem;
	}

	.team-card-stats {
		display: flex;
		gap: 0.75rem;
		margin-top: 0.375rem;
		flex-wrap: wrap;
	}

	.team-card-stats .stat {
		display: flex;
		align-items: center;
		gap: 0.25rem;
		font-size: 0.75rem;
		color: #64748b;
	}

	.stat-icon {
		width: 0.875rem;
		height: 0.875rem;
		opacity: 0.6;
	}

	.team-card-right {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		flex-shrink: 0;
	}

	.team-card-arrow {
		width: 1.25rem;
		height: 1.25rem;
		color: #cbd5e1;
		transition: color 0.15s;
	}

	.team-card:hover .team-card-arrow {
		color: #3b82f6;
	}

	.member-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.75rem 1rem;
		border-bottom: 1px solid #f3f4f6;
	}

	.member-row:last-child {
		border-bottom: none;
	}

	.member-name {
		font-weight: 500;
		color: #1a1a2e;
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.member-email {
		font-size: 0.8125rem;
		color: #9ca3af;
	}

	.member-overtime {
		margin-top: 0.25rem;
	}

	.overtime-badge {
		font-size: 0.6875rem;
		background: #f3f4f6;
		color: #6b7280;
		border: 1px solid #e5e7eb;
		padding: 0.125rem 0.5rem;
		border-radius: 999px;
		cursor: pointer;
		transition: background 0.15s, border-color 0.15s;
	}

	.overtime-badge:hover {
		background: #eff6ff;
		border-color: #93c5fd;
		color: #3b82f6;
	}

	.overtime-edit-inline {
		display: flex;
		align-items: center;
		gap: 0.375rem;
		flex-wrap: wrap;
	}

	.overtime-edit-inline label {
		font-size: 0.6875rem;
		color: #6b7280;
		white-space: nowrap;
	}

	.overtime-input {
		width: 70px;
		padding: 0.25rem 0.375rem;
		border: 1px solid #d1d5db;
		border-radius: 4px;
		font-size: 0.75rem;
		text-align: center;
	}

	.overtime-input:focus {
		outline: none;
		border-color: #3b82f6;
	}

	.btn-save-xs, .btn-cancel-xs {
		padding: 0.125rem 0.375rem;
		border-radius: 4px;
		font-size: 0.75rem;
		cursor: pointer;
		border: 1px solid #d1d5db;
		line-height: 1;
	}

	.btn-save-xs {
		background: #22c55e;
		color: white;
		border-color: #22c55e;
	}

	.btn-cancel-xs {
		background: white;
		color: #6b7280;
	}

	.overtime-error {
		font-size: 0.6875rem;
		color: #dc2626;
	}

	.member-actions {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.member-actions select {
		padding: 0.375rem 0.5rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.8125rem;
	}

	.you-badge {
		font-size: 0.6875rem;
		background: #dbeafe;
		color: #1e40af;
		padding: 0.0625rem 0.375rem;
		border-radius: 999px;
		font-weight: 600;
	}

	.role-badge {
		font-size: 0.75rem;
		font-weight: 600;
		padding: 0.125rem 0.5rem;
		border-radius: 999px;
		text-transform: uppercase;
		letter-spacing: 0.025em;
	}

	.role-owner {
		background: #fef3c7;
		color: #92400e;
	}

	.role-admin {
		background: #dbeafe;
		color: #1e40af;
	}

	.role-member {
		background: #f3f4f6;
		color: #4b5563;
	}

	.btn-icon-danger {
		background: none;
		border: none;
		color: #dc2626;
		font-size: 1.25rem;
		cursor: pointer;
		padding: 0.25rem 0.5rem;
		border-radius: 4px;
		line-height: 1;
	}

	.btn-icon-danger:hover {
		background: #fef2f2;
	}

	/* Buttons */
	.btn-primary {
		background: #3b82f6;
		color: white;
		padding: 0.5rem 1rem;
		border-radius: 8px;
		font-size: 0.875rem;
		font-weight: 600;
		border: none;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-primary:hover:not(:disabled) {
		background: #2563eb;
	}

	.btn-primary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-primary-sm {
		background: #3b82f6;
		color: white;
		padding: 0.375rem 0.75rem;
		border-radius: 6px;
		font-size: 0.8125rem;
		font-weight: 600;
		border: none;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-primary-sm:hover:not(:disabled) {
		background: #2563eb;
	}

	.btn-primary-sm:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-secondary {
		background: white;
		color: #4b5563;
		padding: 0.5rem 1rem;
		border-radius: 8px;
		font-size: 0.875rem;
		font-weight: 500;
		border: 1px solid #d1d5db;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-secondary:hover {
		background: #f9fafb;
	}

	.btn-danger {
		background: white;
		color: #dc2626;
		padding: 0.5rem 1rem;
		border-radius: 8px;
		font-size: 0.875rem;
		font-weight: 500;
		border: 1px solid #fecaca;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-danger:hover {
		background: #fef2f2;
	}

	/* Card / form */
	.card {
		background: white;
		border-radius: 12px;
		padding: 2rem;
		border: 1px solid #e5e7eb;
		max-width: 560px;
	}

	.card h2 {
		margin: 0 0 1.25rem;
		color: #1a1a2e;
	}

	.field {
		margin-bottom: 1.25rem;
	}

	.field label {
		display: block;
		font-weight: 500;
		margin-bottom: 0.375rem;
		color: #374151;
		font-size: 0.875rem;
	}

	.field input,
	.field textarea {
		width: 100%;
		padding: 0.625rem 0.75rem;
		border: 1px solid #d1d5db;
		border-radius: 8px;
		font-size: 0.9375rem;
		box-sizing: border-box;
		font-family: inherit;
	}

	.field input:focus,
	.field textarea:focus {
		outline: none;
		border-color: #3b82f6;
		box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
	}

	.form-actions {
		display: flex;
		gap: 0.75rem;
		justify-content: flex-end;
		margin-top: 1.5rem;
	}

	/* Settings section */
	.settings-section {
		margin-top: 2.5rem;
	}

	.settings-section h2 {
		font-size: 1.25rem;
		color: #374151;
		margin: 0 0 1rem;
	}

	.settings-grid {
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		overflow: hidden;
	}

	.setting-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 1rem 1.25rem;
		border-bottom: 1px solid #f3f4f6;
	}

	.setting-row:last-child {
		border-bottom: none;
	}

	.setting-label {
		font-weight: 600;
		color: #1a1a2e;
		margin-bottom: 0.125rem;
	}

	.setting-desc {
		font-size: 0.8125rem;
		color: #6b7280;
		max-width: 400px;
	}

	/* Toggle switch */
	.toggle-switch {
		position: relative;
		width: 48px;
		height: 26px;
		background: #d1d5db;
		border: none;
		border-radius: 999px;
		cursor: pointer;
		transition: background 0.2s;
		flex-shrink: 0;
		padding: 0;
	}

	.toggle-switch.active {
		background: #3b82f6;
	}

	.toggle-switch:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.toggle-knob {
		position: absolute;
		top: 3px;
		left: 3px;
		width: 20px;
		height: 20px;
		background: white;
		border-radius: 50%;
		transition: transform 0.2s;
		box-shadow: 0 1px 3px rgba(0, 0, 0, 0.15);
	}

	.toggle-switch.active .toggle-knob {
		transform: translateX(22px);
	}

	/* Rule mode buttons */
	.rule-mode-btn {
		padding: 0.35rem 0.85rem;
		border: none;
		border-radius: 6px;
		font-size: 0.8rem;
		font-weight: 600;
		cursor: pointer;
		transition: background 0.2s, color 0.2s;
		white-space: nowrap;
		min-width: 130px;
		text-align: center;
	}

	.rule-mode-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.rule-mode-btn.mode-allowed {
		background: #22c55e;
		color: white;
	}

	.rule-mode-btn.mode-approval {
		background: #f59e0b;
		color: white;
	}

	.rule-mode-btn.mode-disabled {
		background: #6b7280;
		color: white;
	}

	/* Pause rules */
	.pause-rules-section {
		margin-top: 1.5rem;
	}

	.pause-rules-section h3 {
		margin: 0;
		font-size: 1.05rem;
		color: #374151;
	}

	.add-rule-form {
		background: #f9fafb;
		padding: 1rem;
		border-radius: 8px;
		margin-bottom: 1rem;
		border: 1px solid #e5e7eb;
	}

	.rule-field {
		display: flex;
		align-items: center;
		gap: 0.375rem;
	}

	.rule-field label {
		font-size: 0.875rem;
		color: #4b5563;
		font-weight: 500;
		white-space: nowrap;
	}

	.rule-field input {
		width: 70px;
		padding: 0.375rem 0.5rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.875rem;
		text-align: center;
	}

	.rule-field span {
		font-size: 0.8125rem;
		color: #6b7280;
		white-space: nowrap;
	}

	.rules-list {
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		overflow: hidden;
	}

	.rule-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.75rem 1rem;
		border-bottom: 1px solid #f3f4f6;
		flex-wrap: wrap;
		gap: 0.5rem;
	}

	.rule-row:last-child {
		border-bottom: none;
	}

	.rule-text {
		font-size: 0.9375rem;
		color: #374151;
	}

	.rule-edit-form {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		flex-wrap: wrap;
		width: 100%;
	}

	.rule-actions {
		display: flex;
		align-items: center;
		gap: 0.375rem;
	}

	.btn-secondary-sm {
		background: white;
		color: #4b5563;
		padding: 0.375rem 0.75rem;
		border-radius: 6px;
		font-size: 0.8125rem;
		font-weight: 500;
		border: 1px solid #d1d5db;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-secondary-sm:hover {
		background: #f9fafb;
	}

	/* Request History */
	.request-history-section {
		margin-top: 2rem;
	}

	.section-header-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		margin-bottom: 1rem;
	}

	.section-header-row h2 {
		margin: 0;
		font-size: 1.25rem;
		color: #1a1a2e;
	}

	.request-filters {
		display: flex;
		gap: 0.375rem;
		margin-bottom: 1rem;
		flex-wrap: wrap;
	}

	.filter-btn {
		padding: 0.375rem 0.75rem;
		border-radius: 20px;
		border: 1px solid #d1d5db;
		background: white;
		color: #6b7280;
		font-size: 0.8125rem;
		cursor: pointer;
		transition: all 0.15s;
	}

	.filter-btn.active {
		background: #1a1a2e;
		color: white;
		border-color: #1a1a2e;
	}

	.filter-btn:hover:not(.active) {
		background: #f3f4f6;
	}

	.request-list {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}

	.request-item {
		background: #f9fafb;
		border: 1px solid #e5e7eb;
		border-radius: 10px;
		padding: 1rem;
	}

	.request-item-header {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		margin-bottom: 0.5rem;
	}

	.request-type-tag {
		background: #e0e7ff;
		color: #3730a3;
		padding: 0.125rem 0.5rem;
		border-radius: 10px;
		font-size: 0.75rem;
		font-weight: 600;
	}

	.request-status {
		font-size: 0.75rem;
		font-weight: 600;
		padding: 0.125rem 0.5rem;
		border-radius: 10px;
	}

	.status-pending {
		background: #fef3c7;
		color: #b45309;
	}

	.status-accepted {
		background: #d1fae5;
		color: #065f46;
	}

	.status-declined {
		background: #fee2e2;
		color: #991b1b;
	}

	.request-item-body {
		font-size: 0.875rem;
	}

	.request-user {
		margin-bottom: 0.25rem;
	}

	.request-email {
		color: #9ca3af;
		font-size: 0.8125rem;
		margin-left: 0.375rem;
	}

	.request-data {
		color: #3b82f6;
		font-size: 0.8125rem;
		margin: 0.25rem 0;
		padding: 0.25rem 0.5rem;
		background: #eff6ff;
		border-radius: 6px;
		display: inline-block;
	}

	.request-message {
		color: #6b7280;
		font-style: italic;
		font-size: 0.8125rem;
		margin: 0.25rem 0;
	}

	.request-meta {
		color: #9ca3af;
		font-size: 0.75rem;
		margin-top: 0.375rem;
	}

	.request-item-actions {
		display: flex;
		gap: 0.375rem;
		margin-top: 0.75rem;
		padding-top: 0.75rem;
		border-top: 1px solid #e5e7eb;
	}

	.btn-accept-sm {
		background: #10b981;
		color: white;
		border: none;
		padding: 0.375rem 0.75rem;
		border-radius: 6px;
		font-size: 0.8125rem;
		font-weight: 500;
		cursor: pointer;
	}

	.btn-accept-sm:hover {
		background: #059669;
	}

	.btn-decline-sm {
		background: white;
		color: #dc2626;
		border: 1px solid #fca5a5;
		padding: 0.375rem 0.75rem;
		border-radius: 6px;
		font-size: 0.8125rem;
		font-weight: 500;
		cursor: pointer;
	}

	.btn-decline-sm:hover {
		background: #fef2f2;
	}

	/* Member Schedule Panel */
	.member-wrapper {
		border: 1px solid #e5e7eb;
		border-radius: 10px;
		overflow: hidden;
		transition: box-shadow 0.15s;
	}

	.member-wrapper:hover {
		box-shadow: 0 1px 4px rgba(0,0,0,0.06);
	}

	.btn-schedule-sm {
		background: #eff6ff;
		color: #2563eb;
		border: 1px solid #bfdbfe;
		padding: 0.25rem 0.625rem;
		border-radius: 6px;
		font-size: 0.75rem;
		font-weight: 500;
		cursor: pointer;
		transition: all 0.15s;
	}

	.btn-schedule-sm:hover {
		background: #dbeafe;
		border-color: #93c5fd;
	}

	.member-schedule-panel {
		background: #f8fafc;
		border-top: 1px solid #e5e7eb;
		padding: 1rem 1.25rem;
	}

	.member-schedule-form {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}

	.schedule-form-row {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}

	.schedule-form-row label {
		font-size: 0.8125rem;
		color: #374151;
		font-weight: 500;
		min-width: 100px;
	}

	.input-xs {
		padding: 0.3rem 0.5rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.8125rem;
		width: 80px;
	}

	.input-xs:focus {
		outline: none;
		border-color: #6366f1;
		box-shadow: 0 0 0 2px rgba(99,102,241,0.15);
	}

	.checkbox-label-sm {
		display: flex;
		align-items: center;
		gap: 0.375rem;
		font-size: 0.8125rem;
		color: #374151;
		cursor: pointer;
	}

	.checkbox-label-sm input[type="checkbox"] {
		accent-color: #6366f1;
	}

	.schedule-day-targets {
		display: flex;
		gap: 0.5rem;
		flex-wrap: wrap;
	}

	.day-target-sm {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 0.25rem;
	}

	.day-target-sm span {
		font-size: 0.75rem;
		color: #6b7280;
		font-weight: 500;
	}

	.day-target-sm input {
		width: 56px;
		padding: 0.25rem 0.375rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.8125rem;
		text-align: center;
	}

	.day-target-sm input:focus {
		outline: none;
		border-color: #6366f1;
		box-shadow: 0 0 0 2px rgba(99,102,241,0.15);
	}

	.schedule-form-actions {
		display: flex;
		gap: 0.5rem;
		margin-top: 0.25rem;
	}

	.overtime-section {
		margin-top: 1rem;
	}

	.overtime-stat .schedule-stat-value {
		color: #2563eb;
		font-weight: 700;
	}

	.my-schedule-form {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
		padding: 0.5rem 0;
	}

	.inline-error {
		background: #fef2f2;
		color: #dc2626;
		font-size: 0.8125rem;
		padding: 0.375rem 0.625rem;
		border-radius: 6px;
		border-left: 3px solid #dc2626;
	}

	.range-hint {
		font-size: 0.75rem;
		color: #6b7280;
		margin: 0.25rem 0;
		font-style: italic;
	}

	/* Holidays */
	.holidays-list {
		display: flex;
		flex-direction: column;
		gap: 0.375rem;
	}

	.holiday-row {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		padding: 0.5rem 0.75rem;
		background: #f8fafc;
		border-radius: 6px;
		font-size: 0.875rem;
	}

	.holiday-date {
		font-weight: 500;
		color: #374151;
		min-width: 90px;
	}

	.holiday-name {
		flex: 1;
		color: #6b7280;
	}

	.recurring-badge {
		font-size: 0.7rem;
		background: #dbeafe;
		color: #1d4ed8;
		padding: 0.125rem 0.5rem;
		border-radius: 999px;
		white-space: nowrap;
	}

	.import-holidays-row {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		margin-bottom: 0.75rem;
		flex-wrap: wrap;
	}

	.checkbox-label {
		display: flex;
		align-items: center;
		gap: 0.375rem;
		font-size: 0.85rem;
		color: #374151;
		cursor: pointer;
	}

	.checkbox-label.compact {
		font-size: 0.8rem;
		white-space: nowrap;
	}

	.checkbox-label input[type="checkbox"] {
		margin: 0;
	}

	/* Absences */
	.absences-list {
		display: flex;
		flex-direction: column;
		gap: 0.375rem;
	}

	.absence-row {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		padding: 0.5rem 0.75rem;
		background: #f8fafc;
		border-radius: 6px;
		font-size: 0.875rem;
	}

	.absence-date {
		font-weight: 500;
		color: #374151;
		min-width: 90px;
	}

	.absence-badge {
		padding: 0.125rem 0.5rem;
		border-radius: 9999px;
		font-size: 0.75rem;
		font-weight: 500;
	}

	.absence-sick {
		background: #fef2f2;
		color: #dc2626;
	}

	.absence-vacation {
		background: #eff6ff;
		color: #2563eb;
	}

	.absence-other {
		background: #f3f4f6;
		color: #6b7280;
	}

	.absence-user {
		color: #6b7280;
		font-size: 0.8125rem;
	}

	.absence-note {
		color: #9ca3af;
		font-size: 0.8125rem;
		font-style: italic;
		flex: 1;
	}

	.absence-toolbar {
		margin-bottom: 0.75rem;
	}

	.absence-admin-row {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		margin-bottom: 0.5rem;
	}

	.filter-select {
		padding: 0.25rem 0.5rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.8125rem;
		background: white;
	}

	/* Schedule Periods */
	.periods-list {
		display: flex;
		flex-direction: column;
		gap: 0.375rem;
	}

	.period-row {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		padding: 0.5rem 0.75rem;
		background: #f8fafc;
		border-radius: 6px;
		font-size: 0.875rem;
	}

	.period-row.period-active {
		background: #ecfdf5;
		border: 1px solid #10b981;
	}

	.active-badge {
		font-size: 0.7rem;
		background: #10b981;
		color: #fff;
		padding: 0.125rem 0.5rem;
		border-radius: 999px;
		font-weight: 600;
		white-space: nowrap;
	}

	.period-dates {
		display: flex;
		align-items: center;
		gap: 0.375rem;
		font-weight: 500;
		color: #374151;
	}

	.period-arrow {
		color: #9ca3af;
	}

	.period-hours {
		color: #3b82f6;
		font-weight: 600;
		margin-left: auto;
	}

	.period-form {
		display: flex;
		flex-direction: column;
		gap: 0.625rem;
		padding: 0.75rem;
		background: #f9fafb;
		border-radius: 8px;
		margin-bottom: 0.75rem;
		border: 1px solid #e5e7eb;
	}

	.period-form.compact {
		padding: 0.5rem;
		gap: 0.5rem;
	}

	.period-form-row {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.period-form-row label {
		min-width: 80px;
		font-size: 0.8125rem;
		font-weight: 500;
		color: #374151;
	}

	.period-form-row input,
	.period-form-row select {
		flex: 1;
		padding: 0.375rem 0.5rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.8125rem;
	}

	.member-periods-section {
		margin-top: 0.75rem;
		padding-top: 0.75rem;
		border-top: 1px solid #e5e7eb;
	}

	.member-periods-section h4 {
		margin: 0;
		font-size: 0.875rem;
		color: #374151;
	}

	/* Responsive */
	@media (max-width: 640px) {
		.page {
			padding: 1rem;
		}

		.org-header {
			flex-direction: column;
			align-items: flex-start;
			gap: 0.75rem;
		}

		.org-header .org-actions {
			width: 100%;
			flex-wrap: wrap;
		}

		.holiday-row,
		.absence-row,
		.period-row {
			flex-wrap: wrap;
		}

		.import-holidays-row {
			flex-direction: column;
			align-items: stretch;
		}

		.settings-grid {
			grid-template-columns: 1fr !important;
		}

		.schedule-overview-grid {
			grid-template-columns: repeat(3, 1fr) !important;
		}

		.schedule-day-targets {
			flex-wrap: wrap;
		}

		.member-row {
			flex-wrap: wrap;
		}

		.request-item {
			flex-direction: column;
		}

		.request-filters {
			flex-wrap: wrap;
		}
	}
</style>
