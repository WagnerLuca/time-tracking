<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { auth } from '$lib/stores/auth.svelte';
	import { organizationsApi, pauseRulesApi, requestsApi, absenceDayApi, groupApi } from '$lib/apiClient';
	import { formatRequestType, formatTimeAgo, statusBadgeClass, parseRequestData } from '$lib/utils/formatters';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import {
		parseRuleMode, ruleModeLabel, joinPolicyLabel, ruleModeButtonClass,
		ruleSettings, toggleSettings,
		visibilitySettings, parseVisibilityMode, visibilityModeLabel, visibilityModeButtonClass,
		ruleModeOptions, joinPolicyOptions, visibilityModeOptions, withInherit
	} from '$lib/utils/orgRules';
	import ModeSelector from '$lib/components/ModeSelector.svelte';
	import ApiKeysPanel from '$lib/components/ApiKeysPanel.svelte';
	import type {
		OrganizationDetailResponse,
		UpdateOrganizationSettingsRequest,
		PauseRuleResponse,
		CreatePauseRuleRequest,
		RuleMode,
		VisibilityMode,
		OrgRequestResponse,
		AbsenceTypeDefinitionResponse,
		AbsenceCreditMode,
		GroupResponse,
		GroupPermissionsResponse,
	} from '$lib/api';

	let org = $state<OrganizationDetailResponse | null>(null);
	let loading = $state(true);
	let error = $state('');
	let actionError = $state('');

	let myRole = $derived(
		org?.members?.find((m) => m.id === auth.user?.id)?.role ?? null
	);
	let canEdit = $derived(myRole === 'Owner' || myRole === 'Admin');
	let isOwner = $derived(myRole === 'Owner');

	// Settings
	let settingsSaving = $state(false);
	let settingsError = $state('');

	// Pause Rules
	let showAddRule = $state(false);
	let newRuleMinHours = $state(6);
	let newRulePauseMinutes = $state(30);
	let addRuleError = $state('');
	let addingRule = $state(false);
	let editingRuleId = $state<number | null>(null);
	let editRuleMinHours = $state(0);
	let editRulePauseMinutes = $state(0);
	let editRuleError = $state('');
	let editingRuleSaving = $state(false);

	// Request history
	let requestHistory = $state<OrgRequestResponse[]>([]);
	let requestHistoryLoading = $state(false);
	let requestHistoryLoaded = $state(false);
	let requestHistoryFilter = $state<string>('all');

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
			loadRequestHistory();
			loadAbsenceTypes();
			loadPermGroups();
		} catch (err) {
			if (getErrorStatus(err) === 404) { error = 'Organization not found.'; }
			else { error = 'Failed to load organization.'; }
		} finally {
			loading = false;
		}
	}

	async function reloadOrg() {
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;
		} catch {}
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

	// ── Custom absence types ──
	let absenceTypes = $state<AbsenceTypeDefinitionResponse[]>([]);
	let showAddType = $state(false);
	let newTypeName = $state('');
	let newTypeColor = $state('#0ea5e9');
	let newTypeCreditMode = $state(1);
	let newTypeCountsAsVacation = $state(false);
	let typeSaving = $state(false);
	let typeError = $state('');

	const creditModeOptions = [
		{ value: 0, label: 'Keep hours as is', hint: 'Informational only — no time credit.' },
		{ value: 1, label: 'Fill hours to target', hint: 'Tracked hours are topped up to the expected daily target (e.g. Business Trip).' },
		{ value: 2, label: 'Credit full target', hint: 'The full daily target is credited, like vacation or sick days.' }
	];

	async function loadAbsenceTypes() {
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesGet(orgSlug);
			absenceTypes = data;
		} catch { absenceTypes = []; }
	}

	async function addAbsenceType(e: Event) {
		e.preventDefault();
		if (!newTypeName.trim()) return;
		typeSaving = true;
		typeError = '';
		try {
			await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesPost(orgSlug, {
				name: newTypeName.trim(),
				color: newTypeColor,
				creditMode: newTypeCreditMode as AbsenceCreditMode,
				countsAsVacation: newTypeCountsAsVacation
			});
			newTypeName = '';
			newTypeCountsAsVacation = false;
			showAddType = false;
			await loadAbsenceTypes();
		} catch (err) {
			typeError = extractErrorMessage(err, 'Failed to add absence type.');
		}
		typeSaving = false;
	}

	async function updateAbsenceType(t: AbsenceTypeDefinitionResponse, patch: Partial<{ creditMode: number; countsAsVacation: boolean }>) {
		typeSaving = true;
		typeError = '';
		try {
			await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesTypeIdPut(orgSlug, t.id!, {
				name: t.name!,
				color: t.color ?? undefined,
				creditMode: (patch.creditMode ?? parseCreditMode(t.creditMode)) as AbsenceCreditMode,
				countsAsVacation: patch.countsAsVacation ?? t.countsAsVacation ?? false
			});
			await loadAbsenceTypes();
		} catch (err) {
			typeError = extractErrorMessage(err, 'Failed to update absence type.');
		}
		typeSaving = false;
	}

	async function deleteAbsenceType(typeId: number) {
		if (!confirm('Deactivate this absence type? Existing absences keep it.')) return;
		typeSaving = true;
		typeError = '';
		try {
			await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesTypeIdDelete(orgSlug, typeId);
			await loadAbsenceTypes();
		} catch (err) {
			typeError = extractErrorMessage(err, 'Failed to delete absence type.');
		}
		typeSaving = false;
	}

	function parseCreditMode(mode: string | null | undefined): number {
		if (mode === 'None') return 0;
		if (mode === 'FillToTarget') return 1;
		return 2;
	}

	// ── Unified permissions overview (org defaults x group overrides) ──
	interface GroupPermsColumn { group: GroupResponse; perms: GroupPermissionsResponse }
	let permGroups = $state<GroupPermsColumn[]>([]);
	let permGroupsLoading = $state(false);
	let permMatrixError = $state('');

	const matrixRuleRows = [
		{ key: 'editPastEntriesMode', label: 'Edit past entries' },
		{ key: 'editPauseMode', label: 'Edit pause' },
		{ key: 'initialOvertimeMode', label: 'Initial overtime' },
		{ key: 'workScheduleChangeMode', label: 'Schedule periods' },
		{ key: 'csvImportMode', label: 'CSV import' },
		{ key: 'absenceSelfServiceMode', label: 'Absences & vacation' }
	];
	const matrixVisibilityRows = [
		{ key: 'vacationVisibility', label: 'Vacation visibility' },
		{ key: 'sickDayVisibility', label: 'Sick day visibility' },
		{ key: 'timeEntryVisibility', label: 'Time entry visibility' }
	];
	const matrixCapabilityRows = [
		{ key: 'groupAdminCanApproveAbsences', label: 'Approve absences' },
		{ key: 'groupAdminCanApproveTimeEdits', label: 'Approve time edits' },
		{ key: 'groupAdminCanApproveScheduleChanges', label: 'Approve schedule changes' },
		{ key: 'groupAdminCanManageMembers', label: 'Manage members' },
		{ key: 'groupAdminCanViewAllTimeEntries', label: 'View all time entries' },
		{ key: 'groupAdminCanExportData', label: 'Export data' }
	];

	async function loadPermGroups() {
		permGroupsLoading = true;
		try {
			const { data: groups } = await groupApi.apiV1OrganizationsSlugGroupsGet(orgSlug);
			const columns = await Promise.all(
				(groups as GroupResponse[]).map(async (group) => {
					const { data: perms } = await groupApi.apiV1OrganizationsSlugGroupsGroupIdPermissionsGet(orgSlug, group.id!);
					return { group, perms };
				})
			);
			permGroups = columns.sort((a, b) => (a.group.name ?? '').localeCompare(b.group.name ?? ''));
		} catch { permGroups = []; }
		permGroupsLoading = false;
	}

	/** Cycles an org-level mode (3 states) from the matrix. */
	async function cycleOrgMatrixCell(key: string, isVisibility: boolean) {
		if (!org) return;
		if (key === 'timeEntryVisibility') {
			// Org level is a boolean (admins may see member time)
			await toggleSetting('memberTimeEntryVisibility');
			return;
		}
		const current = isVisibility
			? parseVisibilityMode((org as Record<string, any>)[key])
			: parseRuleMode((org as Record<string, any>)[key]);
		await setSetting(key, (current + 1) % 3);
	}

	/** Cycles a group override: Inherit -> Disabled/Private -> Approval/Admins -> Allowed/Everyone -> Inherit. */
	async function cycleGroupMatrixCell(col: GroupPermsColumn, key: string, isVisibility: boolean) {
		const raw = (col.perms as Record<string, any>)[key];
		let next: number;
		if (raw == null) next = 0;
		else {
			const current = isVisibility ? parseVisibilityMode(raw) : parseRuleMode(raw);
			next = current >= 2 ? -1 : current + 1; // -1 = back to inherit
		}
		await saveGroupPerm(col, key, next);
	}

	async function toggleGroupCapability(col: GroupPermsColumn, key: string) {
		const current = (col.perms as Record<string, any>)[key] === true;
		await saveGroupPerm(col, key, !current);
	}

	async function saveGroupPerm(col: GroupPermsColumn, key: string, value: number | boolean) {
		permMatrixError = '';
		try {
			await groupApi.apiV1OrganizationsSlugGroupsGroupIdPermissionsPut(orgSlug, col.group.id!, { [key]: value } as any);
			const { data } = await groupApi.apiV1OrganizationsSlugGroupsGroupIdPermissionsGet(orgSlug, col.group.id!);
			col.perms = data;
			permGroups = [...permGroups];
		} catch (err) {
			permMatrixError = extractErrorMessage(err, 'Failed to update permission.');
		}
	}

	/** Badge classes for a mode value in the matrix. */
	function matrixBadge(mode: string | null | undefined, isVisibility: boolean): { label: string; cls: string } {
		if (mode == null) return { label: '—', cls: 'badge-ghost' };
		if (isVisibility) {
			if (mode === 'Private') return { label: 'Private', cls: 'badge-neutral' };
			if (mode === 'AdminOnly') return { label: 'Admins', cls: 'badge-warning' };
			return { label: 'Everyone', cls: 'badge-success' };
		}
		if (mode === 'Disabled') return { label: 'Disabled', cls: 'badge-neutral' };
		if (mode === 'RequiresApproval') return { label: 'Approval', cls: 'badge-warning' };
		return { label: 'Allowed', cls: 'badge-success' };
	}

	function filteredRequests(): OrgRequestResponse[] {
		if (requestHistoryFilter === 'all') return requestHistory;
		return requestHistory.filter(r => r.status === requestHistoryFilter);
	}

	async function setSetting(key: string, value: number) {
		if (!org) return;
		settingsSaving = true;
		settingsError = '';
		try {
			await organizationsApi.apiV1OrganizationsSlugSettingsPut(orgSlug, { [key]: value } as UpdateOrganizationSettingsRequest);
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

	function getPauseRules(): PauseRuleResponse[] {
		return org?.pauseRules ? [...org.pauseRules].sort((a, b) => (a.minHours ?? 0) - (b.minHours ?? 0)) : [];
	}

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

</script>

<svelte:head>
	<title>{org ? `Settings - ${org.name}` : 'Settings'} - Time Tracking</title>
</svelte:head>

<div class="max-w-5xl mx-auto px-6 pb-6">
	<div class="flex items-center gap-2 mb-6">
		<a href="/organizations/{orgSlug}" class="text-base-content/60 no-underline text-sm hover:text-primary">&larr; {org?.name ?? 'Organization'}</a>
	</div>

	{#if loading}
		<div class="flex items-center gap-3 justify-center py-12 text-base-content/40"><span class="loading loading-spinner loading-sm"></span><span>Loading...</span></div>
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if org}
		<h1 class="text-2xl font-bold text-base-content mb-1">{canEdit ? 'Settings' : 'Rules'}</h1>
		<p class="text-base-content/50 text-sm mb-6 leading-relaxed">
			{canEdit ? 'Organization-wide rules, permissions, and approval workflows.' : 'Current organization rules and policies. Only admins can change these settings.'}
		</p>

		{#if actionError}
			<div class="alert alert-error mb-4 text-sm">{actionError}</div>
		{/if}

		{#if canEdit}
			<!-- ==================== Admin Settings ==================== -->
			<section>
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

					<!-- Rule mode settings (segmented: click an option) -->
					{#each ruleSettings as rs}
						{@const mode = (org as Record<string, any>)[rs.key] as string | null}
						<div class="flex items-center justify-between gap-4 p-4 border-b border-base-200 last:border-b-0 flex-wrap">
							<div class="setting-info">
								<div class="font-semibold text-base-content mb-0.5">{rs.label}</div>
								<div class="text-sm text-base-content/60 max-w-[400px]">{rs.description}</div>
							</div>
							<ModeSelector
								options={rs.key === 'joinPolicy' ? joinPolicyOptions : ruleModeOptions}
								value={parseRuleMode(mode)}
								onselect={(v) => setSetting(rs.key, v as number)}
								disabled={settingsSaving}
							/>
						</div>
					{/each}

					<!-- Visibility settings -->
					{#each visibilitySettings as vs}
						{@const mode = (org as Record<string, any>)[vs.key] as string | null}
						<div class="flex items-center justify-between gap-4 p-4 border-b border-base-200 last:border-b-0 flex-wrap">
							<div class="setting-info">
								<div class="font-semibold text-base-content mb-0.5">{vs.label}</div>
								<div class="text-sm text-base-content/60 max-w-[400px]">{vs.description}</div>
							</div>
							<ModeSelector
								options={visibilityModeOptions}
								value={parseVisibilityMode(mode)}
								onselect={(v) => setSetting(vs.key, v as number)}
								disabled={settingsSaving}
							/>
						</div>
					{/each}
				</div>

				<!-- Default Vacation Days -->
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

				<!-- Custom Absence Types -->
				<div class="mt-6">
					<div class="flex items-center justify-between mb-4">
						<div>
							<h3 class="text-base font-bold text-base-content/70 m-0">Absence Types</h3>
							<p class="text-sm text-base-content/50 m-0">Org-defined absence types like "Business Trip" with configurable effect on expected hours.</p>
						</div>
						<button class="btn btn-primary btn-sm" onclick={() => (showAddType = !showAddType)}>
							{showAddType ? 'Cancel' : '+ Add Type'}
						</button>
					</div>

					{#if typeError}
						<div class="alert alert-error mb-4 text-sm">{typeError}</div>
					{/if}

					{#if showAddType}
						<form onsubmit={addAbsenceType} class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300 flex gap-3 items-end flex-wrap">
							<div class="flex flex-col gap-1">
								<span class="text-xs font-medium text-base-content/60">Name</span>
								<input type="text" class="input input-bordered input-sm w-44" placeholder="e.g. Business Trip" bind:value={newTypeName} required disabled={typeSaving} />
							</div>
							<div class="flex flex-col gap-1">
								<span class="text-xs font-medium text-base-content/60">Hours handling</span>
								<select class="select select-bordered select-sm" bind:value={newTypeCreditMode} disabled={typeSaving}>
									{#each creditModeOptions as o}
										<option value={o.value}>{o.label}</option>
									{/each}
								</select>
							</div>
							<div class="flex flex-col gap-1">
								<span class="text-xs font-medium text-base-content/60">Color</span>
								<input type="color" class="input input-bordered input-sm w-14 p-1" bind:value={newTypeColor} disabled={typeSaving} />
							</div>
							<label class="label cursor-pointer flex items-center gap-2 text-sm pb-1.5">
								<input type="checkbox" class="checkbox checkbox-sm" bind:checked={newTypeCountsAsVacation} disabled={typeSaving} />
								Counts against vacation budget
							</label>
							<button type="submit" class="btn btn-primary btn-sm" disabled={typeSaving}>
								{typeSaving ? 'Adding...' : 'Add Type'}
							</button>
							<p class="text-xs text-base-content/40 w-full m-0">{creditModeOptions.find(o => o.value === newTypeCreditMode)?.hint}</p>
						</form>
					{/if}

					{#if absenceTypes.length === 0}
						<p class="text-base-content/40">No custom absence types defined. Sick Day, Vacation and Other are always available.</p>
					{:else}
						<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
							{#each absenceTypes as t}
								<div class="flex items-center justify-between p-3 border-b border-base-200 last:border-b-0 flex-wrap gap-2">
									<div class="flex items-center gap-2 min-w-[160px]">
										<span class="w-3 h-3 rounded-full shrink-0" style="background: {t.color ?? '#888'}"></span>
										<span class="font-semibold text-sm">{t.name}</span>
										{#if t.countsAsVacation}<span class="badge badge-xs badge-info badge-outline">vacation budget</span>{/if}
									</div>
									<div class="flex items-center gap-2">
										<select
											class="select select-bordered select-xs"
											value={parseCreditMode(t.creditMode)}
											onchange={(e) => updateAbsenceType(t, { creditMode: parseInt((e.target as HTMLSelectElement).value) })}
											disabled={typeSaving}
											title={creditModeOptions.find(o => o.value === parseCreditMode(t.creditMode))?.hint}
										>
											{#each creditModeOptions as o}
												<option value={o.value}>{o.label}</option>
											{/each}
										</select>
										<label class="flex items-center gap-1 text-xs text-base-content/60 cursor-pointer" title="Deduct days of this type from the vacation budget">
											<input type="checkbox" class="checkbox checkbox-xs" checked={t.countsAsVacation ?? false}
												onchange={(e) => updateAbsenceType(t, { countsAsVacation: (e.target as HTMLInputElement).checked })}
												disabled={typeSaving} />
											vac. budget
										</label>
										<button class="btn btn-ghost btn-xs text-error" title="Deactivate type" onclick={() => deleteAbsenceType(t.id!)}>&times;</button>
									</div>
								</div>
							{/each}
						</div>
					{/if}
				</div>

				<!-- Unified Permissions Overview -->
				<div class="mt-6">
					<div class="flex items-center justify-between mb-1">
						<h3 class="text-base font-bold text-base-content/70 m-0">Permissions Overview</h3>
						<a href="/organizations/{orgSlug}/groups" class="btn btn-ghost btn-xs">Open groups →</a>
					</div>
					<p class="text-sm text-base-content/50 mb-3">
						Organization defaults and per-group overrides in one place. Click a cell to change it —
						group cells cycle <span class="italic">Inherit → Disabled → Approval → Allowed</span>.
					</p>
					{#if permMatrixError}
						<div class="alert alert-error text-sm mb-3">{permMatrixError}</div>
					{/if}
					{#if permGroupsLoading}
						<p class="text-base-content/40 text-sm">Loading groups...</p>
					{:else}
						<div class="overflow-x-auto bg-base-100 border border-base-300 rounded-xl">
							<table class="table table-sm">
								<thead>
									<tr>
										<th class="bg-base-200/50 text-xs">Permission</th>
										<th class="bg-base-200/50 text-center text-xs">Organization</th>
										{#each permGroups as col}
											<th class="bg-base-200/50 text-center text-xs">
												<span class="inline-flex items-center gap-1.5">
													<span class="w-2.5 h-2.5 rounded-full shrink-0" style="background: {col.group.color ?? '#888'}"></span>
													{col.group.name}
												</span>
											</th>
										{/each}
									</tr>
								</thead>
								<tbody>
									<tr><td colspan={2 + permGroups.length} class="text-[10px] font-semibold uppercase tracking-wide text-base-content/40 bg-base-200/30 py-1">Rules — what members may do</td></tr>
									{#each matrixRuleRows as row}
										<tr>
											<td class="font-medium text-sm whitespace-nowrap">{row.label}</td>
											<td class="text-center">
												<button class="badge badge-sm cursor-pointer {matrixBadge((org as Record<string, any>)[row.key], false).cls}" title="Organization default — click to change" onclick={() => cycleOrgMatrixCell(row.key, false)} disabled={settingsSaving}>
													{matrixBadge((org as Record<string, any>)[row.key], false).label}
												</button>
											</td>
											{#each permGroups as col}
												<td class="text-center">
													{#if (col.perms as Record<string, any>)[row.key] == null}
														<button class="badge badge-sm badge-ghost opacity-60 cursor-pointer" title="Inherits the org default — click to override" onclick={() => cycleGroupMatrixCell(col, row.key, false)}>
															↳ {matrixBadge((org as Record<string, any>)[row.key], false).label}
														</button>
													{:else}
														<button class="badge badge-sm cursor-pointer {matrixBadge((col.perms as Record<string, any>)[row.key], false).cls}" title="Group override — click to change (cycles back to Inherit)" onclick={() => cycleGroupMatrixCell(col, row.key, false)}>
															{matrixBadge((col.perms as Record<string, any>)[row.key], false).label}
														</button>
													{/if}
												</td>
											{/each}
										</tr>
									{/each}

									<tr><td colspan={2 + permGroups.length} class="text-[10px] font-semibold uppercase tracking-wide text-base-content/40 bg-base-200/30 py-1">Visibility — who sees members' data</td></tr>
									{#each matrixVisibilityRows as row}
										{@const orgMode = row.key === 'timeEntryVisibility' ? (org.memberTimeEntryVisibility ? 'AdminOnly' : 'Private') : (org as Record<string, any>)[row.key]}
										<tr>
											<td class="font-medium text-sm whitespace-nowrap">{row.label}</td>
											<td class="text-center">
												<button class="badge badge-sm cursor-pointer {matrixBadge(orgMode, true).cls}" title="Organization default — click to change" onclick={() => cycleOrgMatrixCell(row.key, true)} disabled={settingsSaving}>
													{matrixBadge(orgMode, true).label}
												</button>
											</td>
											{#each permGroups as col}
												<td class="text-center">
													{#if (col.perms as Record<string, any>)[row.key] == null}
														<button class="badge badge-sm badge-ghost opacity-60 cursor-pointer" title="Inherits the org default — click to override" onclick={() => cycleGroupMatrixCell(col, row.key, true)}>
															↳ {matrixBadge(orgMode, true).label}
														</button>
													{:else}
														<button class="badge badge-sm cursor-pointer {matrixBadge((col.perms as Record<string, any>)[row.key], true).cls}" title="Group override — click to change (cycles back to Inherit)" onclick={() => cycleGroupMatrixCell(col, row.key, true)}>
															{matrixBadge((col.perms as Record<string, any>)[row.key], true).label}
														</button>
													{/if}
												</td>
											{/each}
										</tr>
									{/each}

									<tr><td colspan={2 + permGroups.length} class="text-[10px] font-semibold uppercase tracking-wide text-base-content/40 bg-base-200/30 py-1">Group admin capabilities — what group admins may approve/see</td></tr>
									{#each matrixCapabilityRows as row}
										<tr>
											<td class="font-medium text-sm whitespace-nowrap">{row.label}</td>
											<td class="text-center text-xs text-base-content/40" title="Organization admins can always do this">always</td>
											{#each permGroups as col}
												<td class="text-center">
													<button
														class="badge badge-sm cursor-pointer {(col.perms as Record<string, any>)[row.key] === true ? 'badge-success' : 'badge-ghost opacity-60'}"
														title="Click to toggle for this group's admins"
														onclick={() => toggleGroupCapability(col, row.key)}
													>
														{(col.perms as Record<string, any>)[row.key] === true ? '✓ enabled' : 'off'}
													</button>
												</td>
											{/each}
										</tr>
									{/each}
								</tbody>
							</table>
						</div>
						{#if permGroups.length === 0}
							<p class="text-base-content/40 text-sm mt-2">No groups yet — create groups to set per-team overrides.</p>
						{/if}
					{/if}
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

			<ApiKeysPanel {orgSlug} />

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
		{:else}
			<!-- ==================== Member read-only Rules view ==================== -->
			<section>
				<h2 class="text-xl text-base-content/70 mb-4">Organization Rules</h2>

				<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
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
		{/if}
	{/if}
</div>
