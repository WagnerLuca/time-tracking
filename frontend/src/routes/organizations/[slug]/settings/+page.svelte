<script lang="ts">
	import DeclineForm from '$lib/components/DeclineForm.svelte';
	import { getOrgDetail } from '$lib/stores/orgDetail';
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { organizationsApi, pauseRulesApi, requestsApi, absenceDayApi, groupApi } from '$lib/apiClient';
	import { t } from '$lib/i18n';
	import { formatRequestType, formatRequestStatus, formatTimeAgo, formatNumber, statusBadgeClass, parseRequestData } from '$lib/utils/formatters';
	import { DEFAULT_GROUP_COLOR, GROUP_COLOR_PRESETS, safeColor } from '$lib/utils/colors';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import {
		isOrgAdmin, parseRuleMode, ruleModeLabel, ruleModeButtonClass,
		ruleSettings, toggleSettings,
		visibilitySettings, parseVisibilityMode, visibilityModeLabel, visibilityModeButtonClass,
		ruleModeOptions, joinPolicyOptions, visibilityModeOptions
	} from '$lib/utils/orgRules';
	import ModeSelector from '$lib/components/ModeSelector.svelte';
	import ApiKeysPanel from '$lib/components/ApiKeysPanel.svelte';
	import type {
		OrganizationDetailResponse,
		UpdateOrganizationSettingsRequest,
		PauseRuleResponse,
		OrgRequestResponse,
		AbsenceTypeDefinitionResponse,
		AbsenceCreditMode,
		GroupResponse,
		GroupPermissionsResponse,
	} from '$lib/api';

	let org = $state<OrganizationDetailResponse | null>(null);
	let loading = $state(true);
	let error = $state('');

	let myRole = $derived(
		org?.members?.find((m) => m.id === auth.user?.id)?.role ?? null
	);
	let canEdit = $derived(isOrgAdmin(myRole));

	// Settings
	let settingsSaving = $state(false);

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
	let requestHistoryError = $state('');
	let requestHistoryFilter = $state<string>('all');
	let respondingId = $state<number | null>(null);

	const orgSlug = $derived(page.params.slug ?? '');

	// (Re)load whenever the slug in the URL changes, so switching orgs never shows stale data
	$effect(() => {
		if (!orgSlug) return;
		untrack(() => {
			loadOrg();
		});
	});

	async function loadOrg() {
		loading = true;
		error = '';
		try {
			const data = await getOrgDetail(orgSlug);
			org = data;
			if (isOrgAdmin(data.members?.find((m) => m.id === auth.user?.id)?.role)) {
				loadRequestHistory();
				loadAbsenceTypes();
				loadPermGroups();
			}
		} catch (err) {
			error = getErrorStatus(err) === 404 ? t('orgSettings.notFound') : extractErrorMessage(err, t('orgSettings.loadOrgFailed'));
		} finally {
			loading = false;
		}
	}

	/** Refreshes the settings in place (no page spinner); returns false when that failed. */
	async function reloadOrg(): Promise<boolean> {
		try {
			const data = await getOrgDetail(orgSlug, { fresh: true });
			org = data;
			return true;
		} catch (err) {
			toast.error(err, t('orgSettings.reloadFailed'));
			return false;
		}
	}

	async function loadRequestHistory() {
		requestHistoryLoading = true;
		requestHistoryError = '';
		try {
			const { data } = await requestsApi.apiV1OrganizationsSlugRequestsGet(orgSlug);
			requestHistory = data.items ?? [];
		} catch (err) {
			requestHistoryError = extractErrorMessage(err, t('orgSettings.requests.loadFailed'));
		} finally {
			requestHistoryLoading = false;
		}
	}

	/** The request whose decline form (optional reason) is open. */
	let decliningId = $state<number | null>(null);

	async function respondToRequest(req: OrgRequestResponse, accept: boolean, reason?: string) {
		if (!req.id) return;
		respondingId = req.id;
		try {
			await requestsApi.apiV1OrganizationsSlugRequestsIdPut(orgSlug, req.id, { accept, reason });
			decliningId = null;
			toast.success(accept ? t('orgSettings.requests.accepted') : t('orgSettings.requests.declined'));
			await loadRequestHistory();
		} catch (err) {
			toast.error(err, accept ? t('orgSettings.requests.acceptFailed') : t('orgSettings.requests.declineFailed'));
		} finally {
			respondingId = null;
		}
	}

	// ── Custom absence types ──
	let absenceTypes = $state<AbsenceTypeDefinitionResponse[]>([]);
	let absenceTypesError = $state('');
	let showAddType = $state(false);
	let newTypeName = $state('');
	let newTypeColor = $state(DEFAULT_GROUP_COLOR);
	let newTypeCreditMode = $state(1);
	let newTypeCountsAsVacation = $state(false);
	let newTypeSubmitted = $state(false);
	let addTypeError = $state('');
	let typeSaving = $state(false);

	let creditModeOptions = $derived([
		{ value: 0, label: t('orgSettings.absenceTypes.credit.none.label'), hint: t('orgSettings.absenceTypes.credit.none.hint') },
		{ value: 1, label: t('orgSettings.absenceTypes.credit.fill.label'), hint: t('orgSettings.absenceTypes.credit.fill.hint') },
		{ value: 2, label: t('orgSettings.absenceTypes.credit.full.label'), hint: t('orgSettings.absenceTypes.credit.full.hint') }
	]);

	async function loadAbsenceTypes() {
		absenceTypesError = '';
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesGet(orgSlug);
			absenceTypes = data;
		} catch (err) {
			absenceTypesError = extractErrorMessage(err, t('orgSettings.absenceTypes.loadFailed'));
		}
	}

	function toggleAddType() {
		showAddType = !showAddType;
		newTypeSubmitted = false;
		addTypeError = '';
	}

	async function addAbsenceType(e: Event) {
		e.preventDefault();
		newTypeSubmitted = true;
		if (!newTypeName.trim()) return;
		typeSaving = true;
		addTypeError = '';
		try {
			await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesPost(orgSlug, {
				name: newTypeName.trim(),
				color: newTypeColor,
				creditMode: newTypeCreditMode as AbsenceCreditMode,
				countsAsVacation: newTypeCountsAsVacation
			});
			toast.success(t('orgSettings.absenceTypes.added'));
			newTypeName = '';
			newTypeColor = DEFAULT_GROUP_COLOR;
			newTypeCountsAsVacation = false;
			newTypeSubmitted = false;
			showAddType = false;
			await loadAbsenceTypes();
		} catch (err) {
			addTypeError = extractErrorMessage(err, t('orgSettings.absenceTypes.addFailed'));
		} finally {
			typeSaving = false;
		}
	}

	async function updateAbsenceType(at: AbsenceTypeDefinitionResponse, patch: Partial<{ creditMode: number; countsAsVacation: boolean }>) {
		typeSaving = true;
		try {
			await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesTypeIdPut(orgSlug, at.id!, {
				name: at.name!,
				color: at.color ?? undefined,
				creditMode: (patch.creditMode ?? parseCreditMode(at.creditMode)) as AbsenceCreditMode,
				countsAsVacation: patch.countsAsVacation ?? at.countsAsVacation ?? false
			});
			toast.success(t('orgSettings.absenceTypes.saved'));
		} catch (err) {
			toast.error(err, t('orgSettings.absenceTypes.saveFailed'));
		} finally {
			// Reload either way so the controls show the stored values again after an error
			await loadAbsenceTypes();
			typeSaving = false;
		}
	}

	async function deleteAbsenceType(at: AbsenceTypeDefinitionResponse) {
		const ok = await confirmDialog({
			title: t('orgSettings.absenceTypes.deactivateTitle', { name: at.name ?? '' }),
			message: t('orgSettings.absenceTypes.deactivateMessage'),
			confirmLabel: t('orgSettings.absenceTypes.deactivate'),
			danger: true
		});
		if (!ok) return;
		typeSaving = true;
		try {
			await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesTypeIdDelete(orgSlug, at.id!);
			toast.success(t('orgSettings.absenceTypes.deactivated'));
			await loadAbsenceTypes();
		} catch (err) {
			toast.error(err, t('orgSettings.absenceTypes.deactivateFailed'));
		} finally {
			typeSaving = false;
		}
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
	let permGroupsError = $state('');
	let permSaving = $state(false);

	// Row labels reuse the shared rule texts (format.rules) and the group capability texts (groups.perm)
	const MATRIX_RULE_KEYS = ['editPastEntriesMode', 'editPauseMode', 'initialOvertimeMode', 'workScheduleChangeMode', 'csvImportMode', 'absenceSelfServiceMode'] as const;
	const MATRIX_CAPABILITY_KEYS = ['groupAdminCanApproveAbsences', 'groupAdminCanApproveTimeEdits', 'groupAdminCanApproveScheduleChanges', 'groupAdminCanManageMembers', 'groupAdminCanViewAllTimeEntries', 'groupAdminCanExportData'] as const;

	let matrixRuleRows = $derived(MATRIX_RULE_KEYS.map((key) => ({ key, label: t(`format.rules.${key}.label`) })));
	let matrixVisibilityRows = $derived([
		{ key: 'vacationVisibility', label: t('format.rules.vacationVisibility.label') },
		{ key: 'sickDayVisibility', label: t('format.rules.sickDayVisibility.label') },
		{ key: 'timeEntryVisibility', label: t('orgSettings.matrix.timeEntryVisibility') }
	]);
	let matrixCapabilityRows = $derived(MATRIX_CAPABILITY_KEYS.map((key) => ({ key, label: t(`groups.perm.${key}.label`) })));

	async function loadPermGroups() {
		permGroupsLoading = true;
		permGroupsError = '';
		try {
			const [{ data: groups }, { data: allPerms }] = await Promise.all([
				groupApi.apiV1OrganizationsSlugGroupsGet(orgSlug),
				groupApi.apiV1OrganizationsSlugGroupsPermissionsGet(orgSlug)
			]);
			const permsById = new Map(allPerms.map((p) => [p.groupId, p]));
			const columns = groups.map((group) => ({ group, perms: permsById.get(group.id) ?? { groupId: group.id } }));
			permGroups = columns.sort((a, b) => (a.group.name ?? '').localeCompare(b.group.name ?? ''));
		} catch (err) {
			permGroupsError = extractErrorMessage(err, t('orgSettings.matrix.loadFailed'));
		} finally {
			permGroupsLoading = false;
		}
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
		permSaving = true;
		try {
			await groupApi.apiV1OrganizationsSlugGroupsGroupIdPermissionsPut(orgSlug, col.group.id!, { [key]: value } as any);
			const { data } = await groupApi.apiV1OrganizationsSlugGroupsGroupIdPermissionsGet(orgSlug, col.group.id!);
			col.perms = data;
			permGroups = [...permGroups];
			toast.success(t('orgSettings.matrix.saved'));
		} catch (err) {
			toast.error(err, t('orgSettings.matrix.saveFailed'));
		} finally {
			permSaving = false;
		}
	}

	/** Badge classes for a mode value in the matrix. */
	function matrixBadge(mode: string | null | undefined, isVisibility: boolean): { label: string; cls: string } {
		if (mode == null) return { label: '—', cls: 'badge-ghost' };
		if (isVisibility) {
			if (mode === 'Private') return { label: t('format.options.private'), cls: 'badge-neutral' };
			if (mode === 'AdminOnly') return { label: t('format.options.admins'), cls: 'badge-warning' };
			return { label: t('format.options.everyone'), cls: 'badge-success' };
		}
		if (mode === 'Disabled') return { label: t('format.options.disabled'), cls: 'badge-neutral' };
		if (mode === 'RequiresApproval') return { label: t('format.options.approval'), cls: 'badge-warning' };
		return { label: t('format.options.allowed'), cls: 'badge-success' };
	}

	let filteredRequests = $derived(requestHistoryFilter === 'all' ? requestHistory : requestHistory.filter((r) => r.status === requestHistoryFilter));

	/** Saves one setting, then refreshes the page's copy of the org. */
	async function saveSettings(patch: UpdateOrganizationSettingsRequest, failMessage = t('orgSettings.settingFailed')) {
		if (!org) return;
		settingsSaving = true;
		try {
			await organizationsApi.apiV1OrganizationsSlugSettingsPut(orgSlug, patch);
			if (await reloadOrg()) toast.success(t('common.saved'));
		} catch (err) {
			toast.error(err, failMessage);
		} finally {
			settingsSaving = false;
		}
	}

	function setSetting(key: string, value: number) {
		return saveSettings({ [key]: value } as UpdateOrganizationSettingsRequest);
	}

	function toggleSetting(key: string) {
		return saveSettings({ [key]: !(org as Record<string, any>)[key] } as UpdateOrganizationSettingsRequest);
	}

	function saveDefaultVacationDays(e: Event) {
		const input = e.currentTarget as HTMLInputElement;
		const val = parseFloat(input.value);
		if (isNaN(val) || val < 0 || val > 365) {
			toast.error(t('orgSettings.vacationDays.invalid'));
			return;
		}
		saveSettings({ defaultVacationDays: val });
	}

	function saveBusinessDayEnd(e: Event) {
		const val = (e.currentTarget as HTMLInputElement).value;
		if (!val) {
			toast.error(t('orgSettings.businessDayEnd.timeRequired'));
			return;
		}
		saveSettings({ businessDayEnd: val }, t('orgSettings.businessDayEnd.failed'));
	}

	function saveTimeZone(e: Event) {
		saveSettings({ timeZone: (e.currentTarget as HTMLSelectElement).value }, t('orgSettings.businessDayEnd.timeZoneFailed'));
	}

	let pauseRules = $derived(org?.pauseRules ? [...org.pauseRules].sort((a, b) => (a.minHours ?? 0) - (b.minHours ?? 0)) : []);

	function ruleInvalid(minHours: number, pauseMinutes: number): string {
		if (!(minHours > 0) || minHours > 24) return t('orgSettings.pause.invalidHours');
		if (!(pauseMinutes >= 1) || !Number.isInteger(pauseMinutes)) return t('orgSettings.pause.invalidMinutes');
		return '';
	}

	function toggleAddRule() {
		showAddRule = !showAddRule;
		addRuleError = '';
	}

	async function addPauseRule(e: Event) {
		e.preventDefault();
		addRuleError = ruleInvalid(newRuleMinHours, newRulePauseMinutes);
		if (addRuleError) return;
		addingRule = true;
		try {
			await pauseRulesApi.apiV1OrganizationsSlugPauseRulesPost(orgSlug, {
				minHours: newRuleMinHours,
				pauseMinutes: newRulePauseMinutes
			});
			toast.success(t('orgSettings.pause.added'));
			await reloadOrg();
			showAddRule = false;
			newRuleMinHours = 6;
			newRulePauseMinutes = 30;
		} catch (err) {
			addRuleError = extractErrorMessage(err, t('orgSettings.pause.addFailed'));
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

	async function saveEditRule(e: Event, ruleId: number) {
		e.preventDefault();
		editRuleError = ruleInvalid(editRuleMinHours, editRulePauseMinutes);
		if (editRuleError) return;
		editingRuleSaving = true;
		try {
			await pauseRulesApi.apiV1OrganizationsSlugPauseRulesRuleIdPut(orgSlug, ruleId, {
				minHours: editRuleMinHours,
				pauseMinutes: editRulePauseMinutes
			});
			toast.success(t('orgSettings.pause.saved'));
			await reloadOrg();
			editingRuleId = null;
		} catch (err) {
			editRuleError = extractErrorMessage(err, t('orgSettings.pause.saveFailed'));
		} finally {
			editingRuleSaving = false;
		}
	}

	async function deleteRule(rule: PauseRuleResponse) {
		const ok = await confirmDialog({
			title: t('orgSettings.pause.deleteTitle'),
			message: t('orgSettings.pause.deleteMessage', { hours: formatNumber(rule.minHours ?? 0), minutes: rule.pauseMinutes ?? 0 }),
			confirmLabel: t('common.delete'),
			danger: true
		});
		if (!ok) return;
		try {
			await pauseRulesApi.apiV1OrganizationsSlugPauseRulesRuleIdDelete(orgSlug, rule.id!);
			toast.success(t('orgSettings.pause.deleted'));
			await reloadOrg();
		} catch (err) {
			toast.error(err, t('orgSettings.pause.deleteFailed'));
		}
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: org ? `${canEdit ? t('orgSettings.titleAdmin') : t('orgSettings.titleMember')} – ${org.name}` : t('orgSettings.titleAdmin') })}</title>
</svelte:head>

{#snippet settingInfo(label: string, description: string)}
	<div class="min-w-0 flex-1 basis-60">
		<div class="font-semibold text-base-content mb-0.5">{label}</div>
		<div class="text-sm text-base-content/60 max-w-[400px]">{description}</div>
	</div>
{/snippet}

{#snippet pauseRuleText(rule: PauseRuleResponse)}
	<strong>&ge; {formatNumber(rule.minHours ?? 0)} h</strong> {t('orgSettings.pause.tracked')} &rarr; <strong>{t('orgSettings.pause.minutes', { value: rule.pauseMinutes ?? 0 })}</strong> {t('orgSettings.pause.deducted')}
{/snippet}

<div class="max-w-5xl mx-auto sm:px-6 pb-6">
	<div class="flex items-center gap-2 mb-6">
		<a href="/organizations/{orgSlug}" class="text-base-content/60 no-underline text-sm hover:text-primary">&larr; {org?.name ?? t('nav.organization')}</a>
	</div>

	{#if loading}
		<div class="flex items-center gap-3 justify-center py-12 text-base-content/50"><span class="loading loading-spinner loading-sm"></span><span>{t('common.loading')}</span></div>
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if org}
		<h1 class="text-2xl font-bold text-base-content mb-1">{canEdit ? t('orgSettings.titleAdmin') : t('orgSettings.titleMember')}</h1>
		<p class="text-base-content/60 text-sm mb-6 leading-relaxed">
			{canEdit ? t('orgSettings.introAdmin') : t('orgSettings.introMember')}
		</p>

		{#if canEdit}
			<!-- ==================== Admin Settings ==================== -->
			<section>
				<h2 class="text-xl text-base-content/70 mb-4">{t('orgSettings.orgSettings')}</h2>

				<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
					<!-- Toggle settings (booleans) -->
					{#each toggleSettings() as ts (ts.key)}
						{@const value = !!(org as Record<string, any>)[ts.key]}
						<div class="flex items-center justify-between gap-4 p-4 border-b border-base-200 last:border-b-0">
							{@render settingInfo(ts.label, ts.description)}
							<input
								type="checkbox"
								class="toggle toggle-primary shrink-0"
								role="switch"
								checked={value}
								aria-checked={value}
								aria-label={ts.label}
								disabled={settingsSaving}
								onchange={(e) => { e.currentTarget.checked = value; toggleSetting(ts.key); }}
							/>
						</div>
					{/each}

					<!-- Rule mode settings (segmented: click an option) -->
					{#each ruleSettings() as rs (rs.key)}
						{@const mode = (org as Record<string, any>)[rs.key] as string | null}
						<div class="flex items-center justify-between gap-4 p-4 border-b border-base-200 last:border-b-0 flex-wrap">
							{@render settingInfo(rs.label, rs.description)}
							<ModeSelector
								options={rs.key === 'joinPolicy' ? joinPolicyOptions() : ruleModeOptions()}
								value={parseRuleMode(mode)}
								onselect={(v) => setSetting(rs.key, v as number)}
								disabled={settingsSaving}
							/>
						</div>
					{/each}

					<!-- Visibility settings -->
					{#each visibilitySettings() as vs (vs.key)}
						{@const mode = (org as Record<string, any>)[vs.key] as string | null}
						<div class="flex items-center justify-between gap-4 p-4 border-b border-base-200 last:border-b-0 flex-wrap">
							{@render settingInfo(vs.label, vs.description)}
							<ModeSelector
								options={visibilityModeOptions()}
								value={parseVisibilityMode(mode)}
								onselect={(v) => setSetting(vs.key, v as number)}
								disabled={settingsSaving}
							/>
						</div>
					{/each}
				</div>

				<!-- Default Vacation Days -->
				<div class="bg-base-200/50 rounded-lg p-4 mt-4 border border-base-300">
					<div class="flex items-center justify-between gap-4 flex-wrap">
						{@render settingInfo(t('orgSettings.vacationDays.label'), t('orgSettings.vacationDays.description'))}
						<div class="flex items-center gap-2">
							<input
								id="default-vacation-days"
								type="number"
								class="input input-sm w-20 text-center"
								step="0.5"
								min="0"
								max="365"
								value={org.defaultVacationDays ?? 0}
								aria-label={t('orgSettings.vacationDays.label')}
								onchange={saveDefaultVacationDays}
								disabled={settingsSaving}
							/>
							<span class="text-sm text-base-content/60">{t('orgSettings.vacationDays.unit')}</span>
						</div>
					</div>
				</div>

				<div class="bg-base-200/50 rounded-lg p-4 mt-4 border border-base-300">
					<div class="flex items-center justify-between gap-4 flex-wrap">
						{@render settingInfo(t('orgSettings.businessDayEnd.label'), t('orgSettings.businessDayEnd.description'))}
						<div class="flex items-center gap-2 flex-wrap">
							<input
								type="time"
								class="input input-sm w-28"
								value={org.businessDayEnd ?? '00:00'}
								aria-label={t('orgSettings.businessDayEnd.label')}
								onchange={saveBusinessDayEnd}
								disabled={settingsSaving}
							/>
							<select
								class="select select-sm w-52 max-w-full"
								value={org.timeZone ?? 'Europe/Berlin'}
								onchange={saveTimeZone}
								disabled={settingsSaving}
								aria-label={t('orgSettings.businessDayEnd.timeZone')}
							>
								{#each Intl.supportedValuesOf('timeZone') as tz}
									<option value={tz}>{tz}</option>
								{/each}
							</select>
						</div>
					</div>
				</div>

				<!-- Custom Absence Types -->
				<div class="mt-6">
					<div class="flex items-center justify-between gap-3 mb-4 flex-wrap">
						<div class="min-w-0 flex-1 basis-60">
							<h3 class="text-base font-bold text-base-content/70 m-0">{t('orgSettings.absenceTypes.title')}</h3>
							<p class="text-sm text-base-content/60 m-0">{t('orgSettings.absenceTypes.intro')}</p>
						</div>
						<button class="btn btn-primary btn-sm" onclick={toggleAddType} aria-expanded={showAddType}>
							{showAddType ? t('common.cancel') : t('orgSettings.absenceTypes.add')}
						</button>
					</div>

					{#if absenceTypesError}
						<div class="alert alert-error mb-4 text-sm" role="alert">
							<span>{absenceTypesError}</span>
							<button class="btn btn-sm" onclick={loadAbsenceTypes}>{t('common.retry')}</button>
						</div>
					{/if}

					{#if showAddType}
						<form onsubmit={addAbsenceType} class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300 flex gap-3 items-end flex-wrap" novalidate>
							{#if addTypeError}
								<div class="alert alert-error text-sm w-full" role="alert">{addTypeError}</div>
							{/if}
							<div class="flex flex-col gap-1 w-full sm:w-auto">
								<label for="new-type-name" class="text-xs font-medium text-base-content/60">{t('orgSettings.absenceTypes.name')}</label>
								<input id="new-type-name" type="text" class="input input-sm w-full sm:w-44" class:input-error={newTypeSubmitted && !newTypeName.trim()} placeholder={t('orgSettings.absenceTypes.namePlaceholder')} bind:value={newTypeName} required disabled={typeSaving} />
								{#if newTypeSubmitted && !newTypeName.trim()}<span class="text-xs text-error">{t('common.required')}</span>{/if}
							</div>
							<div class="flex flex-col gap-1 w-full sm:w-auto">
								<label for="new-type-credit" class="text-xs font-medium text-base-content/60">{t('orgSettings.absenceTypes.creditMode')}</label>
								<select id="new-type-credit" class="select select-sm w-full" bind:value={newTypeCreditMode} disabled={typeSaving}>
									{#each creditModeOptions as o (o.value)}
										<option value={o.value}>{o.label}</option>
									{/each}
								</select>
							</div>
							<div class="flex flex-col gap-1">
								<span class="text-xs font-medium text-base-content/60">{t('orgSettings.absenceTypes.color')}</span>
								<div class="flex items-center gap-1.5 flex-wrap min-h-8">
									{#each GROUP_COLOR_PRESETS as c (c)}
										<button
											type="button"
											class="w-6 h-6 rounded-full border-2 transition-transform {newTypeColor.toLowerCase() === c.toLowerCase() ? 'border-base-content scale-110' : 'border-transparent'}"
											style="background-color: {c}"
											aria-label={t('orgSettings.absenceTypes.colorSwatch', { color: c })}
											aria-pressed={newTypeColor.toLowerCase() === c.toLowerCase()}
											disabled={typeSaving}
											onclick={() => (newTypeColor = c)}
										></button>
									{/each}
									<input type="color" class="w-6 h-6 rounded cursor-pointer border-0 p-0" aria-label={t('orgSettings.absenceTypes.customColor')} bind:value={newTypeColor} disabled={typeSaving} />
								</div>
							</div>
							<label class="label cursor-pointer flex items-center gap-2 text-sm pb-1.5">
								<input type="checkbox" class="checkbox checkbox-sm" bind:checked={newTypeCountsAsVacation} disabled={typeSaving} />
								{t('orgSettings.absenceTypes.countsAsVacation')}
							</label>
							<button type="submit" class="btn btn-primary btn-sm" disabled={typeSaving}>
								{typeSaving ? t('common.adding') : t('orgSettings.absenceTypes.addSubmit')}
							</button>
							<p class="text-xs text-base-content/50 w-full m-0">{creditModeOptions.find((o) => o.value === newTypeCreditMode)?.hint}</p>
						</form>
					{/if}

					{#if absenceTypes.length === 0}
						{#if !absenceTypesError}
							<p class="text-base-content/50">{t('orgSettings.absenceTypes.empty')}</p>
						{/if}
					{:else}
						<ul class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
							{#each absenceTypes as at (at.id)}
								<li class="flex items-center justify-between p-3 border-b border-base-200 last:border-b-0 flex-wrap gap-2">
									<div class="flex items-center gap-2 min-w-[160px]">
										<span class="w-3 h-3 rounded-full shrink-0" style="background: {safeColor(at.color)}" aria-hidden="true"></span>
										<span class="font-semibold text-sm">{at.name}</span>
										{#if at.countsAsVacation}<span class="badge badge-xs badge-info badge-outline">{t('orgSettings.absenceTypes.vacationBudget')}</span>{/if}
									</div>
									<div class="flex items-center gap-2 flex-wrap">
										<select
											class="select select-xs"
											value={parseCreditMode(at.creditMode)}
											onchange={(e) => updateAbsenceType(at, { creditMode: parseInt(e.currentTarget.value) })}
											disabled={typeSaving}
											aria-label={t('orgSettings.absenceTypes.creditModeFor', { name: at.name ?? '' })}
											title={creditModeOptions.find((o) => o.value === parseCreditMode(at.creditMode))?.hint}
										>
											{#each creditModeOptions as o (o.value)}
												<option value={o.value}>{o.label}</option>
											{/each}
										</select>
										<label class="flex items-center gap-1 text-xs text-base-content/60 cursor-pointer" title={t('orgSettings.absenceTypes.vacationBudgetTitle')}>
											<input type="checkbox" class="checkbox checkbox-xs" checked={at.countsAsVacation ?? false}
												onchange={(e) => updateAbsenceType(at, { countsAsVacation: e.currentTarget.checked })}
												disabled={typeSaving} />
											{t('orgSettings.absenceTypes.vacationBudgetShort')}
										</label>
										<button class="btn btn-ghost btn-xs text-error" title={t('orgSettings.absenceTypes.deactivateLabel', { name: at.name ?? '' })} aria-label={t('orgSettings.absenceTypes.deactivateLabel', { name: at.name ?? '' })} disabled={typeSaving} onclick={() => deleteAbsenceType(at)}>&times;</button>
									</div>
								</li>
							{/each}
						</ul>
					{/if}
				</div>

				<!-- Unified Permissions Overview -->
				<div class="mt-6">
					<div class="flex items-center justify-between gap-2 mb-1 flex-wrap">
						<h3 class="text-base font-bold text-base-content/70 m-0">{t('orgSettings.matrix.title')}</h3>
						<a href="/organizations/{orgSlug}/groups" class="btn btn-ghost btn-xs">{t('orgSettings.matrix.openGroups')}</a>
					</div>
					<p class="text-sm text-base-content/60 mb-3">
						{t('orgSettings.matrix.intro')} <span class="italic">{t('orgSettings.matrix.cycle')}</span>.
					</p>
					{#if permGroupsError}
						<div class="alert alert-error text-sm mb-3" role="alert">
							<span>{permGroupsError}</span>
							<button class="btn btn-sm" onclick={loadPermGroups}>{t('common.retry')}</button>
						</div>
					{/if}
					{#if permGroupsLoading}
						<p class="text-base-content/50 text-sm">{t('orgSettings.matrix.loadingGroups')}</p>
					{:else}
						<div class="overflow-x-auto bg-base-100 border border-base-300 rounded-xl">
							<table class="table table-sm">
								<thead>
									<tr>
										<th class="bg-base-200/50 text-xs">{t('orgSettings.matrix.permission')}</th>
										<th class="bg-base-200/50 text-center text-xs">{t('orgSettings.matrix.organization')}</th>
										{#each permGroups as col (col.group.id)}
											<th class="bg-base-200/50 text-center text-xs">
												<span class="inline-flex items-center gap-1.5">
													<span class="w-2.5 h-2.5 rounded-full shrink-0" style="background: {safeColor(col.group.color)}" aria-hidden="true"></span>
													{col.group.name}
												</span>
											</th>
										{/each}
									</tr>
								</thead>
								<tbody>
									<tr><td colspan={2 + permGroups.length} class="text-[10px] font-semibold uppercase tracking-wide text-base-content/50 bg-base-200/30 py-1">{t('orgSettings.matrix.rulesSection')}</td></tr>
									{#each matrixRuleRows as row (row.key)}
										{@const orgBadge = matrixBadge((org as Record<string, any>)[row.key], false)}
										<tr>
											<td class="font-medium text-sm whitespace-nowrap">{row.label}</td>
											<td class="text-center">
												<button class="badge badge-sm cursor-pointer {orgBadge.cls}" title={t('orgSettings.matrix.orgCellTitle')} onclick={() => cycleOrgMatrixCell(row.key, false)} disabled={settingsSaving}>
													{orgBadge.label}
												</button>
											</td>
											{#each permGroups as col (col.group.id)}
												{@const raw = (col.perms as Record<string, any>)[row.key]}
												<td class="text-center">
													{#if raw == null}
														<button class="badge badge-sm badge-ghost opacity-60 cursor-pointer" title={t('orgSettings.matrix.inheritTitle')} onclick={() => cycleGroupMatrixCell(col, row.key, false)} disabled={permSaving}>
															↳ {orgBadge.label}
														</button>
													{:else}
														<button class="badge badge-sm cursor-pointer {matrixBadge(raw, false).cls}" title={t('orgSettings.matrix.overrideTitle')} onclick={() => cycleGroupMatrixCell(col, row.key, false)} disabled={permSaving}>
															{matrixBadge(raw, false).label}
														</button>
													{/if}
												</td>
											{/each}
										</tr>
									{/each}

									<tr><td colspan={2 + permGroups.length} class="text-[10px] font-semibold uppercase tracking-wide text-base-content/50 bg-base-200/30 py-1">{t('orgSettings.matrix.visibilitySection')}</td></tr>
									{#each matrixVisibilityRows as row (row.key)}
										{@const orgMode = row.key === 'timeEntryVisibility' ? (org.memberTimeEntryVisibility ? 'AdminOnly' : 'Private') : (org as Record<string, any>)[row.key]}
										{@const orgBadge = matrixBadge(orgMode, true)}
										<tr>
											<td class="font-medium text-sm whitespace-nowrap">{row.label}</td>
											<td class="text-center">
												<button class="badge badge-sm cursor-pointer {orgBadge.cls}" title={t('orgSettings.matrix.orgCellTitle')} onclick={() => cycleOrgMatrixCell(row.key, true)} disabled={settingsSaving}>
													{orgBadge.label}
												</button>
											</td>
											{#each permGroups as col (col.group.id)}
												{@const raw = (col.perms as Record<string, any>)[row.key]}
												<td class="text-center">
													{#if raw == null}
														<button class="badge badge-sm badge-ghost opacity-60 cursor-pointer" title={t('orgSettings.matrix.inheritTitle')} onclick={() => cycleGroupMatrixCell(col, row.key, true)} disabled={permSaving}>
															↳ {orgBadge.label}
														</button>
													{:else}
														<button class="badge badge-sm cursor-pointer {matrixBadge(raw, true).cls}" title={t('orgSettings.matrix.overrideTitle')} onclick={() => cycleGroupMatrixCell(col, row.key, true)} disabled={permSaving}>
															{matrixBadge(raw, true).label}
														</button>
													{/if}
												</td>
											{/each}
										</tr>
									{/each}

									<tr><td colspan={2 + permGroups.length} class="text-[10px] font-semibold uppercase tracking-wide text-base-content/50 bg-base-200/30 py-1">{t('orgSettings.matrix.capabilitiesSection')}</td></tr>
									{#each matrixCapabilityRows as row (row.key)}
										<tr>
											<td class="font-medium text-sm whitespace-nowrap">{row.label}</td>
											<td class="text-center text-xs text-base-content/50" title={t('orgSettings.matrix.alwaysTitle')}>{t('orgSettings.matrix.always')}</td>
											{#each permGroups as col (col.group.id)}
												{@const enabled = (col.perms as Record<string, any>)[row.key] === true}
												<td class="text-center">
													<button
														class="badge badge-sm cursor-pointer {enabled ? 'badge-success' : 'badge-ghost opacity-60'}"
														title={t('orgSettings.matrix.capabilityTitle')}
														aria-pressed={enabled}
														onclick={() => toggleGroupCapability(col, row.key)}
														disabled={permSaving}
													>
														{enabled ? t('orgSettings.matrix.enabled') : t('orgSettings.matrix.disabled')}
													</button>
												</td>
											{/each}
										</tr>
									{/each}
								</tbody>
							</table>
						</div>
						{#if permGroups.length === 0 && !permGroupsError}
							<p class="text-base-content/50 text-sm mt-2">{t('orgSettings.matrix.noGroups')}</p>
						{/if}
					{/if}
				</div>

				<!-- Pause Rules -->
				{#if org.autoPauseEnabled}
					<div class="mt-6">
						<div class="flex items-center justify-between gap-2 mb-4 flex-wrap">
							<h3 class="text-base font-bold text-base-content/70 m-0">{t('orgSettings.pause.title')}</h3>
							<button class="btn btn-primary btn-sm" onclick={toggleAddRule} aria-expanded={showAddRule}>
								{showAddRule ? t('common.cancel') : t('orgSettings.pause.add')}
							</button>
						</div>

						{#if showAddRule}
							<div class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300">
								{#if addRuleError}
									<div class="alert alert-error mb-4 text-sm" role="alert">{addRuleError}</div>
								{/if}
								<form onsubmit={addPauseRule} class="flex gap-2 items-center flex-wrap" novalidate>
									<div class="flex items-center gap-1.5">
										<label for="new-rule-hours" class="text-sm text-base-content/70 font-medium whitespace-nowrap">{t('orgSettings.pause.whenTracked')}</label>
										<input id="new-rule-hours" type="number" class="input input-xs w-[70px] text-center" step="0.5" min="0.5" max="24" bind:value={newRuleMinHours} required disabled={addingRule} />
										<span class="text-sm text-base-content/60 whitespace-nowrap">{t('orgSettings.pause.hours')}</span>
									</div>
									<div class="flex items-center gap-1.5">
										<label for="new-rule-pause" class="text-sm text-base-content/70 font-medium whitespace-nowrap">{t('orgSettings.pause.deduct')}</label>
										<input id="new-rule-pause" type="number" class="input input-xs w-[70px] text-center" min="1" step="1" bind:value={newRulePauseMinutes} required disabled={addingRule} />
										<span class="text-sm text-base-content/60 whitespace-nowrap">{t('orgSettings.pause.minPause')}</span>
									</div>
									<button type="submit" class="btn btn-primary btn-sm" disabled={addingRule}>
										{addingRule ? t('common.adding') : t('orgSettings.pause.addSubmit')}
									</button>
								</form>
							</div>
						{/if}

						{#if pauseRules.length === 0}
							<p class="text-base-content/50">{t('orgSettings.pause.empty')}</p>
						{:else}
							<ul class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
								{#each pauseRules as rule (rule.id)}
									<li class="flex items-center justify-between p-3 border-b border-base-200 last:border-b-0 flex-wrap gap-2">
										{#if editingRuleId === rule.id}
											<form onsubmit={(e) => saveEditRule(e, rule.id!)} class="flex items-center gap-3 flex-wrap w-full" novalidate>
												{#if editRuleError}
													<div class="alert alert-error text-sm w-full" role="alert">{editRuleError}</div>
												{/if}
												<div class="flex items-center gap-1.5">
													<label for="edit-rule-hours" class="text-sm text-base-content/70 font-medium whitespace-nowrap">{t('orgSettings.pause.whenTracked')}</label>
													<input id="edit-rule-hours" type="number" class="input input-xs w-[70px] text-center" step="0.5" min="0.5" max="24" bind:value={editRuleMinHours} disabled={editingRuleSaving} />
													<span class="text-sm text-base-content/60 whitespace-nowrap">{t('orgSettings.pause.hoursShort')}</span>
												</div>
												<div class="flex items-center gap-1.5">
													<label for="edit-rule-pause" class="text-sm text-base-content/70 font-medium whitespace-nowrap">{t('orgSettings.pause.deduct')}</label>
													<input id="edit-rule-pause" type="number" class="input input-xs w-[70px] text-center" min="1" step="1" bind:value={editRulePauseMinutes} disabled={editingRuleSaving} />
													<span class="text-sm text-base-content/60 whitespace-nowrap">{t('orgSettings.pause.min')}</span>
												</div>
												<div class="flex items-center gap-1.5">
													<button type="submit" class="btn btn-primary btn-sm" disabled={editingRuleSaving}>{editingRuleSaving ? t('common.saving') : t('common.save')}</button>
													<button type="button" class="btn btn-ghost btn-sm" onclick={() => (editingRuleId = null)}>{t('common.cancel')}</button>
												</div>
											</form>
										{:else}
											<div class="text-[0.9375rem] text-base-content/70">{@render pauseRuleText(rule)}</div>
											<div class="flex items-center gap-1.5">
												<button class="btn btn-ghost btn-sm" onclick={() => startEditRule(rule)}>{t('common.edit')}</button>
												<button class="btn btn-ghost btn-xs text-error" title={t('orgSettings.pause.deleteLabel')} aria-label={t('orgSettings.pause.deleteLabel')} onclick={() => deleteRule(rule)}>&times;</button>
											</div>
										{/if}
									</li>
								{/each}
							</ul>
						{/if}
					</div>
				{/if}
			</section>

			<ApiKeysPanel {orgSlug} members={org.members ?? []} />

			<!-- Request History -->
			<section class="mt-8">
				<h2 class="text-xl font-bold text-base-content mb-4">{t('orgSettings.requests.title')}</h2>

				{#if requestHistoryError}
					<div class="alert alert-error text-sm mb-4" role="alert">
						<span>{requestHistoryError}</span>
						<button class="btn btn-sm" onclick={loadRequestHistory}>{t('common.retry')}</button>
					</div>
				{/if}

				{#if requestHistoryLoading && requestHistory.length === 0}
					<p class="text-base-content/50">{t('orgSettings.requests.loading')}</p>
				{:else if !requestHistoryError}
					<div class="flex gap-1.5 mb-4 flex-wrap">
						{#each ['all', 'Pending', 'Accepted', 'Declined'] as f (f)}
							<button
								class="btn btn-sm rounded-full {requestHistoryFilter === f ? 'btn-neutral' : 'btn-outline'}"
								aria-pressed={requestHistoryFilter === f}
								onclick={() => (requestHistoryFilter = f)}
							>
								{f === 'all' ? t('orgSettings.requests.all') : formatRequestStatus(f)} ({f === 'all' ? requestHistory.length : requestHistory.filter((r) => r.status === f).length})
							</button>
						{/each}
					</div>

					{#if filteredRequests.length === 0}
						<p class="text-base-content/50">{t('orgSettings.requests.empty')}</p>
					{:else}
						<ul class="flex flex-col gap-3">
							{#each filteredRequests as req (req.id)}
								<li class="bg-base-200/30 border border-base-300 rounded-lg p-4">
									<div class="flex items-center gap-2 mb-2 flex-wrap">
										<span class="badge badge-primary badge-sm">{formatRequestType(req.type)}</span>
										<span class="badge badge-sm {statusBadgeClass(req.status)}">{formatRequestStatus(req.status)}</span>
									</div>
									<div class="text-sm">
										<div class="mb-1 break-words">
											<strong>{req.userFirstName} {req.userLastName}</strong>
											<span class="text-base-content/50 text-sm ml-1.5">{req.userEmail}</span>
										</div>
										{#if req.requestData}
											<div class="text-sm my-1 py-1 px-2 bg-primary/10 rounded-md inline-block">{parseRequestData(req.type, req.requestData).join(', ')}</div>
										{/if}
										{#if req.message}
											<div class="text-base-content/60 italic text-sm my-1">"{req.message}"</div>
										{/if}
										<div class="text-base-content/50 text-xs mt-1.5">
											<span>{t('orgSettings.requests.created', { time: formatTimeAgo(req.createdAt) })}</span>
											{#if req.respondedAt}
												<span>&middot; {t('orgSettings.requests.respondedBy', { status: formatRequestStatus(req.status), name: req.respondedByName ?? t('common.unknown'), time: formatTimeAgo(req.respondedAt) })}</span>
											{/if}
											{#if req.relatedEntityId}
												<span>&middot; {t('orgSettings.requests.entry', { id: req.relatedEntityId })}</span>
											{/if}
										</div>
										{#if req.responseReason}
											<div class="text-base-content/70 text-sm mt-1 break-words">{t('common.reason', { reason: req.responseReason })}</div>
										{/if}
									</div>
									{#if req.status === 'Pending'}
										<div class="flex gap-1.5 mt-3 pt-3 border-t border-base-300">
											{#if decliningId === req.id}
												<DeclineForm busy={respondingId === req.id} onconfirm={(reason) => respondToRequest(req, false, reason)} oncancel={() => (decliningId = null)} />
											{:else}
												<button class="btn btn-success btn-sm" disabled={respondingId === req.id} onclick={() => respondToRequest(req, true)}>{t('orgSettings.requests.accept')}</button>
												<button class="btn btn-outline btn-error btn-sm" disabled={respondingId === req.id} onclick={() => (decliningId = req.id ?? null)}>{t('orgSettings.requests.decline')}</button>
											{/if}
										</div>
									{/if}
								</li>
							{/each}
						</ul>
					{/if}
				{/if}
			</section>
		{:else}
			<!-- ==================== Member read-only Rules view ==================== -->
			<section>
				<h2 class="text-xl text-base-content/70 mb-4">{t('orgSettings.orgRules')}</h2>

				<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
					{#each toggleSettings() as ts (ts.key)}
						{@const value = (org as Record<string, any>)[ts.key]}
						<div class="flex items-center justify-between gap-4 p-4 border-b border-base-200 last:border-b-0 flex-wrap">
							{@render settingInfo(ts.label, ts.description)}
							<span class="badge {value ? 'badge-success' : 'badge-ghost'}">{value ? t('orgSettings.on') : t('orgSettings.off')}</span>
						</div>
					{/each}

					{#each ruleSettings() as rs (rs.key)}
						{@const mode = (org as Record<string, any>)[rs.key] as string | null}
						{@const label = rs.labelFn ? rs.labelFn(mode) : ruleModeLabel(mode)}
						<div class="flex items-center justify-between gap-4 p-4 border-b border-base-200 last:border-b-0 flex-wrap">
							{@render settingInfo(rs.label, rs.description)}
							<span class="badge {ruleModeButtonClass(mode).replace('btn-', 'badge-')}">{label}</span>
						</div>
					{/each}

					{#each visibilitySettings() as vs (vs.key)}
						{@const mode = (org as Record<string, any>)[vs.key] as string | null}
						<div class="flex items-center justify-between gap-4 p-4 border-b border-base-200 last:border-b-0 flex-wrap">
							{@render settingInfo(vs.label, vs.description)}
							<span class="badge {visibilityModeButtonClass(mode).replace('btn-', 'badge-')}">{visibilityModeLabel(mode)}</span>
						</div>
					{/each}
				</div>

				{#if org.autoPauseEnabled}
					<div class="mt-6 pt-4 border-t border-base-300">
						<h3 class="text-base font-bold text-base-content mb-3">{t('orgSettings.pause.title')}</h3>
						{#if pauseRules.length === 0}
							<p class="text-base-content/50">{t('orgSettings.pause.emptyMember')}</p>
						{:else}
							<ul class="flex flex-col gap-2">
								{#each pauseRules as rule (rule.id)}
									<li class="p-2 bg-base-200/50 border border-base-300 rounded-lg text-sm text-base-content/70">{@render pauseRuleText(rule)}</li>
								{/each}
							</ul>
						{/if}
					</div>
				{/if}
			</section>
		{/if}
	{/if}
</div>
