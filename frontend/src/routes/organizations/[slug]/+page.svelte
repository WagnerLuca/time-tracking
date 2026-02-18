<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { goto } from '$app/navigation';
	import { auth } from '$lib/stores/auth.svelte';
	import { organizationsApi, pauseRulesApi, usersApi, workScheduleApi, requestsApi } from '$lib/apiClient';
	import type {
		OrganizationDetailResponse,
		UpdateOrganizationRequest,
		AddMemberRequest,
		UpdateOrganizationSettingsRequest,
		PauseRuleResponse,
		CreatePauseRuleRequest,
		RuleMode,
		WorkScheduleResponse,
		OrgRequestResponse
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

	// Request history (admin)
	let requestHistory = $state<OrgRequestResponse[]>([]);
	let requestHistoryLoading = $state(false);
	let requestHistoryLoaded = $state(false);
	let requestHistoryFilter = $state<string>('all'); // 'all', 'Pending', 'Accepted', 'Declined'

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
			// Load work schedule for the current user
			try {
				const { data: ws } = await workScheduleApi.apiOrganizationsSlugWorkScheduleGet(orgSlug);
				workSchedule = ws;
			} catch {
				workSchedule = null;
			}
		} catch (err: any) {
			error = err.response?.status === 404 ? 'Organization not found.' : 'Failed to load organization.';
		} finally {
			loading = false;
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
			const payload = {
				weeklyWorkHours: memberWeeklyHours ?? undefined,
				distributeEvenly: memberDistributeEvenly,
				targetMon: memberDistributeEvenly ? undefined : memberTargetMon,
				targetTue: memberDistributeEvenly ? undefined : memberTargetTue,
				targetWed: memberDistributeEvenly ? undefined : memberTargetWed,
				targetThu: memberDistributeEvenly ? undefined : memberTargetThu,
				targetFri: memberDistributeEvenly ? undefined : memberTargetFri,
				initialOvertimeHours: memberOvertimeHours
			};
			const { data } = await workScheduleApi.apiOrganizationsSlugMembersMemberIdWorkSchedulePut(orgSlug, editingMemberSchedule, payload);
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

	async function loadUsersForDropdown() {
		if (usersLoaded) return;
		try {
			const { data: users } = await usersApi.apiUsersGet();
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
			await loadOrg();
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
			await loadOrg();
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
			await organizationsApi.apiOrganizationsSlugMembersUserIdPut(orgSlug, userId, { role: newRole as any });
			await loadOrg();
		} catch (err: any) {
			actionError = err.response?.data?.message || 'Failed to update member role.';
		}
	}

	async function removeMember(userId: number, memberName: string) {
		if (!confirm(`Remove ${memberName} from this organization?`)) return;
		actionError = '';
		try {
			await organizationsApi.apiOrganizationsSlugMembersUserIdDelete(orgSlug, userId);
			await loadOrg();
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
			await loadOrg();
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
			await loadOrg();
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
			await loadOrg();
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
			await loadOrg();
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
			await loadOrg();
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
			await loadOrg();
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
			await loadOrg();
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
			await loadOrg();
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
			await organizationsApi.apiOrganizationsSlugMembersUserIdInitialOvertimePut(
				orgSlug, userId, { initialOvertimeHours: editOvertimeMinutes }
			);
			editingOvertimeMemberId = null;
			await loadOrg();
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
</script>

<svelte:head>
	<title>{org ? org.name : 'Organization'} - Time Tracking</title>
</svelte:head>

<div class="page">
	<a href="/organizations" class="back-link">&larr; Back to Organizations</a>

	{#if loading}
		<p class="muted">Loading...</p>
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
			<!-- Organization details -->
			<div class="org-header">
				<div>
					<h1>{org.name}</h1>
					<span class="slug">/{org.slug}</span>
				</div>
				{#if canEdit}
					<div class="header-actions">
						<a href="/organizations/{orgSlug}/time-overview" class="btn-secondary">Time Overview</a>
						<button class="btn-secondary" onclick={startEdit}>Edit</button>
						{#if isOwner}
							<button class="btn-danger" onclick={deleteOrg}>Delete</button>
						{/if}
					</div>
				{/if}
			</div>

			{#if org.description}
				<p class="description">{org.description}</p>
			{/if}

			{#if org.website}
				<a href={org.website} target="_blank" class="website-link">{org.website}</a>
			{/if}

			<!-- Members section -->
			<section class="members-section">
				<div class="section-header">
					<h2>Members ({(org.members ?? []).length})</h2>
					{#if canEdit}
						<button class="btn-primary-sm" onclick={() => { showAddMember = !showAddMember; if (showAddMember) loadUsersForDropdown(); }}>
							{showAddMember ? 'Cancel' : '+ Add Member'}
						</button>
					{/if}
				</div>

				{#if showAddMember}
					<div class="add-member-form">
						{#if addMemberError}
							<div class="error-banner">{addMemberError}</div>
						{/if}
						<form onsubmit={addMember} class="inline-form">
							<select
								bind:value={selectedUserId}
								disabled={addingMember}
								class="user-select"
							>
								<option value={null}>Select a user...</option>
								{#each getAvailableUsers() as user}
									<option value={user.id}>{user.firstName} {user.lastName} ({user.email})</option>
								{/each}
							</select>
							<select bind:value={newMemberRole} disabled={addingMember}>
								<option value={0}>Member</option>
								<option value={1}>Admin</option>
								{#if isOwner}
									<option value={2}>Owner</option>
								{/if}
							</select>
							<button type="submit" class="btn-primary-sm" disabled={addingMember}>
								{addingMember ? 'Adding...' : 'Add'}
							</button>
						</form>
					</div>
				{/if}

				<div class="members-list">
					{#each (org.members ?? []) as member}
						<div class="member-wrapper">
							<div class="member-row">
								<div class="member-info">
									<div class="member-name">
										{member.firstName} {member.lastName}
										{#if member.id === auth.user?.id}
											<span class="you-badge">You</span>
										{/if}
									</div>
									<div class="member-email">{member.email}</div>
								</div>
								<div class="member-actions">
									{#if canEdit}
										<button class="btn-schedule-sm" title="View/Edit Schedule" onclick={() => openMemberSchedule(member.id!)}>
											{editingMemberSchedule === member.id ? '▾' : '▸'} Schedule
										</button>
									{/if}
									{#if canEdit && member.role !== 'Owner' && member.id !== auth.user?.id}
										<select
											value={member.role === 'Admin' ? 1 : 0}
											onchange={(e) => updateMemberRole(member.id!, parseInt(e.currentTarget.value))}
										>
											<option value={0}>Member</option>
											<option value={1}>Admin</option>
											{#if isOwner}
												<option value={2}>Owner</option>
											{/if}
										</select>
										<button
											class="btn-icon-danger"
											title="Remove member"
											onclick={() => removeMember(member.id!, `${member.firstName} ${member.lastName}`)}
										>
											&times;
										</button>
									{:else}
										<span class="role-badge role-{(member.role?.toLowerCase() ?? 'member')}">{member.role}</span>
									{/if}
								</div>
							</div>

							{#if canEdit && editingMemberSchedule === member.id}
								<div class="member-schedule-panel">
									{#if memberScheduleLoading}
										<p class="muted">Loading schedule...</p>
									{:else}
										{#if memberScheduleError}
											<div class="error-banner">{memberScheduleError}</div>
										{/if}
										<div class="member-schedule-form">
											<div class="schedule-form-row">
												<!-- svelte-ignore a11y_label_has_associated_control -->
												<label>Weekly Hours</label>
												<input type="number" step="0.5" min="0" max="80" bind:value={memberWeeklyHours} class="input input-xs" disabled={memberScheduleSaving} />
											</div>
											<div class="schedule-form-row">
												<label class="checkbox-label-sm">
													<input type="checkbox" bind:checked={memberDistributeEvenly} disabled={memberScheduleSaving} />
													Distribute evenly
												</label>
											</div>
											{#if !memberDistributeEvenly}
												<div class="schedule-day-targets">
													<div class="day-target-sm">
														<span>Mon</span>
														<input type="number" step="0.5" min="0" max="24" bind:value={memberTargetMon} disabled={memberScheduleSaving} />
													</div>
													<div class="day-target-sm">
														<span>Tue</span>
														<input type="number" step="0.5" min="0" max="24" bind:value={memberTargetTue} disabled={memberScheduleSaving} />
													</div>
													<div class="day-target-sm">
														<span>Wed</span>
														<input type="number" step="0.5" min="0" max="24" bind:value={memberTargetWed} disabled={memberScheduleSaving} />
													</div>
													<div class="day-target-sm">
														<span>Thu</span>
														<input type="number" step="0.5" min="0" max="24" bind:value={memberTargetThu} disabled={memberScheduleSaving} />
													</div>
													<div class="day-target-sm">
														<span>Fri</span>
														<input type="number" step="0.5" min="0" max="24" bind:value={memberTargetFri} disabled={memberScheduleSaving} />
													</div>
												</div>
											{/if}
											<div class="schedule-form-row">
												<!-- svelte-ignore a11y_label_has_associated_control -->
												<label>Initial Overtime (h)</label>
												<input type="number" step="0.5" bind:value={memberOvertimeHours} class="input input-xs" disabled={memberScheduleSaving} />
											</div>
											<div class="schedule-form-actions">
												<button class="btn-primary-sm" onclick={saveMemberSchedule} disabled={memberScheduleSaving}>
													{memberScheduleSaving ? 'Saving...' : 'Save'}
												</button>
												<button class="btn-secondary-sm" onclick={() => (editingMemberSchedule = null)}>Close</button>
											</div>
										</div>
									{/if}
								</div>
							{/if}
						</div>
					{/each}
				</div>
			</section>

			<!-- My Work Schedule (visible to all members) -->
			{#if myRole && workSchedule}
				<section class="schedule-overview-section">
					<h2>My Work Schedule</h2>
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
					<p class="schedule-hint">Configure your schedule in <a href="/settings">Settings</a></p>
				</section>

				{#if workSchedule.initialOvertimeMode !== 'Disabled'}
					<section class="schedule-overview-section overtime-section">
						<h2>Initial Overtime Balance</h2>
						<div class="schedule-overview-grid">
							<div class="schedule-stat overtime-stat">
								<span class="schedule-stat-label">Hours</span>
								<span class="schedule-stat-value">{workSchedule.initialOvertimeHours ?? 0}h</span>
							</div>
							<div class="schedule-stat">
								<span class="schedule-stat-label">Mode</span>
								<span class="schedule-stat-value">{workSchedule.initialOvertimeMode}</span>
							</div>
						</div>
						{#if workSchedule.initialOvertimeMode === 'Allowed'}
							<p class="schedule-hint">Set your overtime balance in <a href="/settings">Settings</a></p>
						{:else}
							<p class="schedule-hint">Requires admin approval to change</p>
						{/if}
					</section>
				{/if}
			{/if}

			<!-- Settings section (Admin+) -->
			{#if canEdit}
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
								<div class="setting-desc">Control whether members can edit start/end times of completed time entries (e.g. if they forgot to track).</div>
							</div>
							<button
								class="rule-mode-btn {ruleModeColor(org.editPastEntriesMode)}"
								onclick={cycleEditPastEntriesMode}
								disabled={settingsSaving}
								aria-label="Cycle edit past entries mode"
							>
								{ruleModeLabel(org.editPastEntriesMode)}
							</button>
						</div>

						<div class="setting-row">
							<div class="setting-info">
								<div class="setting-label">Edit Pause Duration</div>
								<div class="setting-desc">Control whether members can override the auto-deducted break time on their entries (e.g. if they took a shorter or longer break).</div>
							</div>
							<button
								class="rule-mode-btn {ruleModeColor(org.editPauseMode)}"
								onclick={cycleEditPauseMode}
								disabled={settingsSaving}
								aria-label="Cycle edit pause duration mode"
							>
								{ruleModeLabel(org.editPauseMode)}
							</button>
						</div>

						<div class="setting-row">
							<div class="setting-info">
								<div class="setting-label">Initial Overtime</div>
								<div class="setting-desc">Control whether members can set their own initial overtime balance (e.g. hours carried over from a previous system).</div>
							</div>
							<button
								class="rule-mode-btn {ruleModeColor(org.initialOvertimeMode)}"
								onclick={cycleInitialOvertimeMode}
								disabled={settingsSaving}
								aria-label="Cycle initial overtime mode"
							>
								{ruleModeLabel(org.initialOvertimeMode)}
							</button>
						</div>

						<div class="setting-row">
							<div class="setting-info">
								<div class="setting-label">Join Policy</div>
								<div class="setting-desc">Control how new members can join. Open: anyone can join. Requires Approval: users submit a request. Admin Only: only admins can add members.</div>
							</div>
							<button
								class="rule-mode-btn {ruleModeColor(org.joinPolicy)}"
								onclick={cycleJoinPolicy}
								disabled={settingsSaving}
								aria-label="Cycle join policy"
							>
								{joinPolicyLabel(org.joinPolicy)}
							</button>
						</div>
					</div>

					<!-- Pause Rules (only when auto-pause is enabled) -->
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
			{/if}

			<!-- Request History (Admin+) -->
			{#if canEdit}
				<section class="request-history-section">
					<div class="section-header-row">
						<h2>Request History</h2>
						{#if !requestHistoryLoaded}
							<button class="btn-secondary" onclick={loadRequestHistory} disabled={requestHistoryLoading}>
								{requestHistoryLoading ? 'Loading...' : 'Load Requests'}
							</button>
						{/if}
					</div>

					{#if requestHistoryLoaded}
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
			{/if}
		{/if}
	{/if}
</div>

<style>
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
</style>
