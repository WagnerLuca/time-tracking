<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { groupApi, organizationsApi } from '$lib/apiClient';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { getInitials } from '$lib/utils/formatters';
	import ModeSelector from '$lib/components/ModeSelector.svelte';
	import { parseRuleMode, parseVisibilityMode, ruleModeOptions, visibilityModeOptions, withInherit } from '$lib/utils/orgRules';
	import type {
		GroupResponse,
		GroupDetailResponse,
		GroupMemberResponse,
		GroupPermissionsResponse,
		OrganizationDetailResponse
	} from '$lib/api';

	let orgSlug = $state('');
	let loading = $state(true);
	let error = $state('');

	const role = $derived(orgContext.selectedOrg?.role);
	const isAdmin = $derived(role === 'Owner' || role === 'Admin');

	// Groups list
	let groups = $state<GroupResponse[]>([]);

	// Selected group detail
	let selectedGroup = $state<GroupDetailResponse | null>(null);
	let selectedGroupLoading = $state(false);

	// Create group form
	let showCreateForm = $state(false);
	let createName = $state('');
	let createDescription = $state('');
	let createColor = $state('#6366f1');
	let creating = $state(false);
	let createError = $state('');

	// Edit group
	let editingGroup = $state(false);
	let editName = $state('');
	let editDescription = $state('');
	let editColor = $state('');
	let editSaving = $state(false);
	let editError = $state('');

	// Add member
	let showAddMember = $state(false);
	let addMemberUserId = $state<number | null>(null);
	let addingMember = $state(false);
	let addMemberError = $state('');

	// Org members for the add dropdown
	let orgMembers = $state<Array<{ id: number; firstName: string; lastName: string; email: string }>>([]);

	// Permissions
	let permissions = $state<GroupPermissionsResponse | null>(null);
	let permissionsSaving = $state(false);
	let permissionsError = $state('');
	let detailTab = $state<'members' | 'permissions'>('members');

	// Color presets
	const colorPresets = [
		'#6366f1', '#8b5cf6', '#ec4899', '#ef4444',
		'#f59e0b', '#10b981', '#06b6d4', '#3b82f6'
	];

	$effect(() => {
		orgSlug = $page.params.slug ?? '';
	});

	onMount(() => {
		orgSlug = $page.params.slug ?? '';
		loadGroups();
	});

	async function loadGroups() {
		loading = true;
		error = '';
		try {
			const { data } = await groupApi.apiV1OrganizationsSlugGroupsGet(orgSlug);
			groups = data;
		} catch (err) {
			error = extractErrorMessage(err, 'Failed to load groups.');
		} finally {
			loading = false;
		}
	}

	async function selectGroup(groupId: number) {
		if (selectedGroup?.id === groupId) {
			selectedGroup = null;
			return;
		}
		selectedGroupLoading = true;
		editingGroup = false;
		showAddMember = false;
		detailTab = 'members';
		permissions = null;
		try {
			const { data } = await groupApi.apiV1OrganizationsSlugGroupsGroupIdGet(orgSlug, groupId);
			selectedGroup = data;
			if (isAdmin) loadPermissions(groupId);
		} catch (err) {
			error = extractErrorMessage(err, 'Failed to load group details.');
		} finally {
			selectedGroupLoading = false;
		}
	}

	async function createGroup(e: Event) {
		e.preventDefault();
		creating = true;
		createError = '';
		try {
			await groupApi.apiV1OrganizationsSlugGroupsPost(orgSlug, {
				name: createName,
				description: createDescription || undefined,
				color: createColor
			});
			showCreateForm = false;
			createName = '';
			createDescription = '';
			createColor = '#6366f1';
			await loadGroups();
		} catch (err) {
			createError = extractErrorMessage(err, 'Failed to create group.');
		} finally {
			creating = false;
		}
	}

	function startEdit() {
		if (!selectedGroup) return;
		editName = selectedGroup.name ?? '';
		editDescription = selectedGroup.description ?? '';
		editColor = selectedGroup.color ?? '#6366f1';
		editingGroup = true;
		editError = '';
	}

	async function saveEdit() {
		if (!selectedGroup?.id) return;
		editSaving = true;
		editError = '';
		try {
			await groupApi.apiV1OrganizationsSlugGroupsGroupIdPut(orgSlug, selectedGroup.id, {
				name: editName,
				description: editDescription || undefined,
				color: editColor
			});
			editingGroup = false;
			await loadGroups();
			await selectGroup(selectedGroup.id);
		} catch (err) {
			editError = extractErrorMessage(err, 'Failed to update group.');
		} finally {
			editSaving = false;
		}
	}

	async function deleteGroup() {
		if (!selectedGroup?.id) return;
		if (!confirm(`Delete group "${selectedGroup.name}"? This cannot be undone.`)) return;
		try {
			await groupApi.apiV1OrganizationsSlugGroupsGroupIdDelete(orgSlug, selectedGroup.id);
			selectedGroup = null;
			await loadGroups();
		} catch (err) {
			error = extractErrorMessage(err, 'Failed to delete group.');
		}
	}

	async function loadOrgMembers() {
		if (orgMembers.length > 0) return;
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			orgMembers = (data.members ?? []).map((m) => ({
				id: m.id!,
				firstName: m.firstName ?? '',
				lastName: m.lastName ?? '',
				email: m.email ?? ''
			}));
		} catch {
			orgMembers = [];
		}
	}

	function availableMembers(): typeof orgMembers {
		const existingIds = new Set((selectedGroup?.members ?? []).map((m) => m.userId));
		return orgMembers.filter((m) => !existingIds.has(m.id));
	}

	async function openAddMember() {
		showAddMember = true;
		addMemberError = '';
		addMemberUserId = null;
		await loadOrgMembers();
	}

	async function addMember(e: Event) {
		e.preventDefault();
		if (!selectedGroup?.id || !addMemberUserId) return;
		addingMember = true;
		addMemberError = '';
		try {
			await groupApi.apiV1OrganizationsSlugGroupsGroupIdMembersPost(orgSlug, selectedGroup.id, {
				userId: addMemberUserId
			});
			showAddMember = false;
			addMemberUserId = null;
			await selectGroup(selectedGroup.id);
			await loadGroups();
		} catch (err) {
			addMemberError = extractErrorMessage(err, 'Failed to add member.');
		} finally {
			addingMember = false;
		}
	}

	async function removeMember(userId: number) {
		if (!selectedGroup?.id) return;
		const member = selectedGroup.members?.find((m) => m.userId === userId);
		if (!confirm(`Remove ${member?.firstName} ${member?.lastName} from this group?`)) return;
		try {
			await groupApi.apiV1OrganizationsSlugGroupsGroupIdMembersTargetUserIdDelete(orgSlug, selectedGroup.id, userId);
			await selectGroup(selectedGroup.id);
			await loadGroups();
		} catch (err) {
			error = extractErrorMessage(err, 'Failed to remove member.');
		}
	}

	async function toggleMemberRole(member: GroupMemberResponse) {
		if (!selectedGroup?.id || !member.userId) return;
		const newRole = member.role === 'Admin' ? 'Member' : 'Admin';
		try {
			await groupApi.apiV1OrganizationsSlugGroupsGroupIdMembersTargetUserIdPatch(orgSlug, selectedGroup.id, member.userId, {
				role: newRole
			});
			await selectGroup(selectedGroup.id);
		} catch (err) {
			error = extractErrorMessage(err, 'Failed to update role.');
		}
	}

	// Permission settings definitions
	const permRuleSettings = [
		{ key: 'editPastEntriesMode', label: 'Edit Past Entries', description: 'Can group members edit their own past time entries?' },
		{ key: 'editPauseMode', label: 'Edit Pause', description: 'Can group members edit pause durations?' },
		{ key: 'csvImportMode', label: 'CSV Import', description: 'Can group members import time entries via CSV?' },
		{ key: 'workScheduleChangeMode', label: 'Work Schedule Changes', description: 'Can members change their own work schedule?' },
		{ key: 'initialOvertimeMode', label: 'Initial Overtime', description: 'Can members set their initial overtime balance?' },
		{ key: 'absenceSelfServiceMode', label: 'Absences & Vacation', description: 'Can group members add their own absences directly, or must they request approval?' },
	];

	const permVisibilitySettings = [
		{ key: 'vacationVisibility', label: 'Vacation Visibility', description: 'Who can see vacation absences?' },
		{ key: 'sickDayVisibility', label: 'Sick Day Visibility', description: 'Who can see sick day absences?' },
		{ key: 'timeEntryVisibility', label: 'Time Entry Visibility', description: 'Who can see time entries of group members?' },
	];

	const permAdminFlags = [
		{ key: 'groupAdminCanApproveAbsences', label: 'Approve Absences', description: 'Group Admins can approve/reject absence requests' },
		{ key: 'groupAdminCanApproveTimeEdits', label: 'Approve Time Edits', description: 'Group Admins can approve time entry edit requests' },
		{ key: 'groupAdminCanApproveScheduleChanges', label: 'Approve Schedule Changes', description: 'Group Admins can approve schedule change requests' },
		{ key: 'groupAdminCanManageMembers', label: 'Manage Members', description: 'Group Admins can add/remove members from their group' },
		{ key: 'groupAdminCanViewAllTimeEntries', label: 'View All Time Entries', description: 'Group Admins can see all time entries of group members' },
		{ key: 'groupAdminCanExportData', label: 'Export Data', description: 'Group Admins can export data for their group' },
	];

	async function loadPermissions(groupId: number) {
		try {
			const { data } = await groupApi.apiV1OrganizationsSlugGroupsGroupIdPermissionsGet(orgSlug, groupId);
			permissions = data;
		} catch {
			permissions = null;
		}
	}

	async function setPermValue(key: string, value: number | boolean | null) {
		if (!selectedGroup?.id) return;
		permissionsSaving = true;
		permissionsError = '';
		try {
			// "Inherit" (null) is transmitted as -1; the backend clears the override
			const payload = value === null ? -1 : value;
			await groupApi.apiV1OrganizationsSlugGroupsGroupIdPermissionsPut(orgSlug, selectedGroup.id, { [key]: payload } as any);
			await loadPermissions(selectedGroup.id);
		} catch (err) {
			permissionsError = extractErrorMessage(err, 'Failed to update permission.');
		} finally {
			permissionsSaving = false;
		}
	}

</script>

<svelte:head>
	<title>Groups - {orgContext.selectedOrg?.name ?? 'Organization'} - Time Tracking</title>
</svelte:head>

<div class="max-w-5xl mx-auto px-6 pb-6">
	{#if loading}
		<div class="flex items-center gap-3 justify-center py-12 text-base-content/40">
			<span class="loading loading-spinner loading-sm"></span><span>Loading...</span>
		</div>
	{:else if error && groups.length === 0}
		<div class="alert alert-error">{error}</div>
	{:else}
		<div class="pt-4">
			<!-- Header -->
			<div class="flex items-center justify-between mb-4">
				<h2 class="text-xl font-bold text-base-content">Groups</h2>
				{#if isAdmin}
					<button class="btn btn-primary btn-sm" onclick={() => { showCreateForm = !showCreateForm; createError = ''; }}>
						{showCreateForm ? 'Cancel' : '+ Create Group'}
					</button>
				{/if}
			</div>

			{#if error}
				<div class="alert alert-error mb-4 text-sm">{error}</div>
			{/if}

			<!-- Create form -->
			{#if showCreateForm && isAdmin}
				<div class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300">
					{#if createError}
						<div class="alert alert-error mb-3 text-sm">{createError}</div>
					{/if}
					<form onsubmit={createGroup} class="flex gap-3 items-end flex-wrap">
						<div class="form-control">
							<label for="groupName" class="label text-xs">Name</label>
							<input id="groupName" type="text" class="input input-bordered input-sm" bind:value={createName} required disabled={creating} placeholder="e.g. Backend Team" />
						</div>
						<div class="form-control">
							<label for="groupDesc" class="label text-xs">Description</label>
							<input id="groupDesc" type="text" class="input input-bordered input-sm w-56" bind:value={createDescription} disabled={creating} placeholder="Optional description" />
						</div>
						<div class="form-control">
							<label for="groupColor" class="label text-xs">Color</label>
							<div class="flex items-center gap-1.5">
								{#each colorPresets as c}
									<button
										type="button"
										class="w-6 h-6 rounded-full border-2 transition-transform"
										style="background-color: {c}; border-color: {createColor === c ? 'var(--bc)' : 'transparent'}"
										class:scale-110={createColor === c}
										onclick={() => (createColor = c)}
									></button>
								{/each}
								<input type="color" class="w-6 h-6 rounded cursor-pointer border-0 p-0" bind:value={createColor} />
							</div>
						</div>
						<button type="submit" class="btn btn-primary btn-sm" disabled={creating}>
							{creating ? 'Creating...' : 'Create'}
						</button>
					</form>
				</div>
			{/if}

			{#if groups.length === 0}
				<p class="text-base-content/40 text-sm">No groups yet.{#if isAdmin} Create a group to organize your team.{/if}</p>
			{:else}
				<div class="grid gap-3">
					{#each groups as group (group.id)}
						{@const isSelected = selectedGroup?.id === group.id}
						<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden transition-shadow" class:shadow-md={isSelected}>
							<!-- Group card header -->
							<button
								class="w-full flex items-center gap-3 p-4 text-left hover:bg-base-200/50 transition-colors cursor-pointer"
								onclick={() => selectGroup(group.id!)}
							>
								<div class="w-3 h-8 rounded-full shrink-0" style="background-color: {group.color ?? '#6366f1'}"></div>
								<div class="flex-1 min-w-0">
									<div class="font-semibold text-base-content">{group.name}</div>
									{#if group.description}
										<div class="text-sm text-base-content/50 truncate">{group.description}</div>
									{/if}
								</div>
								<div class="flex items-center gap-2 text-sm text-base-content/50">
									<svg class="w-4 h-4" viewBox="0 0 20 20" fill="currentColor"><path d="M13 6a3 3 0 11-6 0 3 3 0 016 0zM18 8a2 2 0 11-4 0 2 2 0 014 0zM14 15a4 4 0 00-8 0v3h8v-3zM6 8a2 2 0 11-4 0 2 2 0 014 0zM16 18v-3a5.972 5.972 0 00-.75-2.906A3.005 3.005 0 0119 15v3h-3zM4.75 12.094A5.973 5.973 0 004 15v3H1v-3a3 3 0 013.75-2.906z"/></svg>
									<span>{group.memberCount ?? 0}</span>
								</div>
								{#if group.isGroupAdmin}
									<span class="badge badge-sm badge-primary badge-outline">Admin</span>
								{/if}
								<svg class="w-4 h-4 text-base-content/30 transition-transform" class:rotate-180={isSelected} viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clip-rule="evenodd"/></svg>
							</button>

							<!-- Expanded detail panel -->
							{#if isSelected}
								<div class="border-t border-base-300 p-4">
									{#if selectedGroupLoading}
										<div class="flex items-center gap-2 justify-center py-4 text-base-content/40">
											<span class="loading loading-spinner loading-xs"></span><span class="text-sm">Loading...</span>
										</div>
									{:else if selectedGroup}
										<!-- Edit form or group info + actions -->
										{#if editingGroup}
											<div class="mb-4 bg-base-200/50 p-3 rounded-lg border border-base-300">
												{#if editError}
													<div class="alert alert-error mb-3 text-sm">{editError}</div>
												{/if}
												<div class="flex gap-3 items-end flex-wrap">
													<div class="form-control">
														<label for="editName" class="label text-xs">Name</label>
														<input id="editName" type="text" class="input input-bordered input-sm" bind:value={editName} disabled={editSaving} />
													</div>
													<div class="form-control">
														<label for="editDesc" class="label text-xs">Description</label>
														<input id="editDesc" type="text" class="input input-bordered input-sm w-56" bind:value={editDescription} disabled={editSaving} />
													</div>
													<div class="form-control">
														<label for="editColor" class="label text-xs">Color</label>
														<div class="flex items-center gap-1.5">
															{#each colorPresets as c}
																<button
																	type="button"
																	class="w-6 h-6 rounded-full border-2 transition-transform"
																	style="background-color: {c}; border-color: {editColor === c ? 'var(--bc)' : 'transparent'}"
																	class:scale-110={editColor === c}
																	onclick={() => (editColor = c)}
																></button>
															{/each}
															<input type="color" class="w-6 h-6 rounded cursor-pointer border-0 p-0" bind:value={editColor} />
														</div>
													</div>
													<div class="flex gap-1.5">
														<button class="btn btn-primary btn-sm" onclick={saveEdit} disabled={editSaving}>
															{editSaving ? 'Saving...' : 'Save'}
														</button>
														<button class="btn btn-ghost btn-sm" onclick={() => (editingGroup = false)}>Cancel</button>
													</div>
												</div>
											</div>
										{:else if isAdmin}
											<div class="flex items-center gap-2 mb-4">
												<button class="btn btn-ghost btn-sm" onclick={startEdit}>Edit</button>
												<button class="btn btn-ghost btn-sm text-error" onclick={deleteGroup}>Delete</button>
											</div>
										{/if}

										<!-- Tab switcher (Members / Permissions) -->
										{#if isAdmin}
											<div class="tabs tabs-bordered mb-4">
												<button class="tab {detailTab === 'members' ? 'tab-active' : ''}" onclick={() => (detailTab = 'members')}>Members</button>
												<button class="tab {detailTab === 'permissions' ? 'tab-active' : ''}" onclick={() => (detailTab = 'permissions')}>Permissions</button>
											</div>
										{/if}

										{#if detailTab === 'members'}
											<!-- Members section -->
											<div class="flex items-center justify-between mb-3">
												<h3 class="text-sm font-semibold text-base-content/70">Members ({selectedGroup.members?.length ?? 0})</h3>
												{#if isAdmin || group.isGroupAdmin}
													<button class="btn btn-ghost btn-xs" onclick={openAddMember}>
														+ Add Member
													</button>
												{/if}
											</div>

											<!-- Add member form -->
											{#if showAddMember}
												<div class="bg-base-200/50 p-3 rounded-lg border border-base-300 mb-3">
													{#if addMemberError}
														<div class="alert alert-error mb-2 text-sm">{addMemberError}</div>
													{/if}
													<form onsubmit={addMember} class="flex gap-2 items-end">
														<div class="form-control flex-1">
															<label for="addMemberId" class="label text-xs">Select Member</label>
															<select id="addMemberId" class="select select-bordered select-sm w-full" bind:value={addMemberUserId} disabled={addingMember}>
																<option value={null}>-- Select a member --</option>
																{#each availableMembers() as m}
																	<option value={m.id}>{m.firstName} {m.lastName} ({m.email})</option>
																{/each}
															</select>
														</div>
														<button type="submit" class="btn btn-primary btn-sm" disabled={addingMember || !addMemberUserId}>
															{addingMember ? 'Adding...' : 'Add'}
														</button>
														<button type="button" class="btn btn-ghost btn-sm" onclick={() => (showAddMember = false)}>Cancel</button>
													</form>
												</div>
											{/if}

											<!-- Member list -->
											{#if (selectedGroup.members?.length ?? 0) === 0}
												<p class="text-base-content/40 text-sm">No members in this group.</p>
											{:else}
												<div class="space-y-1">
													{#each selectedGroup.members ?? [] as member}
														<div class="flex items-center gap-3 p-2 rounded-lg hover:bg-base-200/50 transition-colors">
															{#if member.profileImageUrl}
																<img src={member.profileImageUrl} alt="" class="w-8 h-8 rounded-full object-cover" />
															{:else}
																<div class="w-8 h-8 rounded-full bg-base-300 flex items-center justify-center text-xs font-medium text-base-content/60">
																	{getInitials(member.firstName, member.lastName)}
																</div>
															{/if}
															<div class="flex-1 min-w-0">
																{#if isAdmin}
																	<a href="/organizations/{orgSlug}/members/{member.userId}" class="text-sm font-medium link link-hover text-base-content">{member.firstName} {member.lastName}</a>
																{:else}
																	<div class="text-sm font-medium text-base-content">{member.firstName} {member.lastName}</div>
																{/if}
																<div class="text-xs text-base-content/50">{member.email}</div>
															</div>
															{#if member.role === 'Admin'}
																<span class="badge badge-xs badge-primary">Admin</span>
															{/if}
															{#if isAdmin}
																<div class="flex items-center gap-1">
																	<button
																		class="btn btn-ghost btn-xs"
																		title={member.role === 'Admin' ? 'Demote to Member' : 'Promote to Admin'}
																		onclick={() => toggleMemberRole(member)}
																	>
																		{member.role === 'Admin' ? 'Demote' : 'Promote'}
																	</button>
																	<button
																		class="btn btn-ghost btn-xs text-error"
																		title="Remove from group"
																		onclick={() => removeMember(member.userId!)}
																	>
																		&times;
																	</button>
																</div>
															{/if}
														</div>
													{/each}
												</div>
											{/if}
										{:else if detailTab === 'permissions' && isAdmin && permissions}
											<!-- Permissions section -->
											{#if permissionsError}
												<div class="alert alert-error mb-3 text-sm">{permissionsError}</div>
											{/if}
											<p class="text-xs text-base-content/50 mb-4">Override organization defaults for this group. "Inherit" uses the org-level setting.</p>

											<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
												<!-- Rule mode overrides -->
												<div class="px-3 py-2 bg-base-200/50 border-b border-base-300">
													<span class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">Rules</span>
												</div>
												{#each permRuleSettings as rs}
													{@const value = (permissions as Record<string, any>)[rs.key]}
													<div class="flex items-center justify-between gap-3 p-3 border-b border-base-200 last:border-b-0 flex-wrap">
														<div>
															<div class="text-sm font-medium text-base-content">{rs.label}</div>
															<div class="text-xs text-base-content/50">{rs.description}</div>
														</div>
														<ModeSelector
															options={withInherit(ruleModeOptions)}
															value={value == null ? null : parseRuleMode(value)}
															onselect={(v) => setPermValue(rs.key, v as number | null)}
															disabled={permissionsSaving}
															size="xs"
														/>
													</div>
												{/each}

												<!-- Visibility overrides -->
												<div class="px-3 py-2 bg-base-200/50 border-b border-base-300">
													<span class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">Visibility</span>
												</div>
												{#each permVisibilitySettings as vs}
													{@const value = (permissions as Record<string, any>)[vs.key]}
													<div class="flex items-center justify-between gap-3 p-3 border-b border-base-200 last:border-b-0 flex-wrap">
														<div>
															<div class="text-sm font-medium text-base-content">{vs.label}</div>
															<div class="text-xs text-base-content/50">{vs.description}</div>
														</div>
														<ModeSelector
															options={withInherit(visibilityModeOptions)}
															value={value == null ? null : parseVisibilityMode(value)}
															onselect={(v) => setPermValue(vs.key, v as number | null)}
															disabled={permissionsSaving}
															size="xs"
														/>
													</div>
												{/each}

												<!-- Group Admin capability flags -->
												<div class="px-3 py-2 bg-base-200/50 border-b border-base-300">
													<span class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">Group Admin Capabilities</span>
												</div>
												{#each permAdminFlags as flag}
													{@const value = (permissions as Record<string, any>)[flag.key]}
													<div class="flex items-center justify-between gap-3 p-3 border-b border-base-200 last:border-b-0 flex-wrap">
														<div>
															<div class="text-sm font-medium text-base-content">{flag.label}</div>
															<div class="text-xs text-base-content/50">{flag.description}</div>
														</div>
														<ModeSelector
															options={[
																{ value: 0, label: 'Disabled', activeClass: 'btn-neutral' },
																{ value: 1, label: 'Enabled', activeClass: 'btn-success' }
															]}
															value={value === true ? 1 : 0}
															onselect={(v) => setPermValue(flag.key, v === 1)}
															disabled={permissionsSaving}
															size="xs"
														/>
													</div>
												{/each}
											</div>
										{:else if detailTab === 'permissions' && isAdmin && !permissions}
											<div class="flex items-center gap-2 justify-center py-4 text-base-content/40">
												<span class="loading loading-spinner loading-xs"></span><span class="text-sm">Loading permissions...</span>
											</div>
										{/if}
									{/if}
								</div>
							{/if}
						</div>
					{/each}
				</div>
			{/if}
		</div>
	{/if}
</div>
