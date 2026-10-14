<script lang="ts">
	import Skeleton from '$lib/components/Skeleton.svelte';
	import { getOrgDetail, invalidateOrgDetail } from '$lib/stores/orgDetail';
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { groupApi } from '$lib/apiClient';
	import { toast } from '$lib/stores/toast.svelte';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { t } from '$lib/i18n';
	import { getInitials } from '$lib/utils/formatters';
	import { DEFAULT_GROUP_COLOR, GROUP_COLOR_PRESETS, safeColor } from '$lib/utils/colors';
	import ModeSelector from '$lib/components/ModeSelector.svelte';
	import Modal from '$lib/components/Modal.svelte';
	import { isOrgAdmin, parseRuleMode, ruleModeLabel, ruleModeOptions, withInherit } from '$lib/utils/orgRules';
	import { cell, groupCell, previewSentence, VISIBILITY_KINDS, type Column, type VisibilityKind } from '$lib/utils/visibility';
	import { buildGroupTree, chain, inheritedFrom, MAX_GROUP_DEPTH, parentOptions, type GroupNode, type Inherited } from '$lib/utils/groupTree';
	import VisibilityMatrix from '$lib/components/VisibilityMatrix.svelte';
	import type {
		GroupResponse,
		GroupDetailResponse,
		GroupMemberResponse,
		GroupPermissionsResponse,
		OrganizationDetailResponse
	} from '$lib/api';

	const orgSlug = $derived(page.params.slug ?? '');
	let loading = $state(true);
	let error = $state('');

	const isAdmin = $derived(isOrgAdmin(orgContext.selectedOrg?.role));

	// Groups list (flat from the API, drawn as a tree)
	let groups = $state<GroupResponse[]>([]);
	const tree = $derived(buildGroupTree(groups));
	let collapsed = $state<number[]>([]);
	const groupName = (id: number | null | undefined) => groups.find((g) => g.id === id)?.name ?? '';
	const hasChildren = (id: number) => groups.some((g) => g.parentGroupId === id);

	// Selected group detail
	let selectedGroup = $state<GroupDetailResponse | null>(null);
	let selectedGroupLoading = $state(false);

	// Create group form (org admins: any parent; group admins: below a group they manage)
	let showCreateForm = $state(false);
	let createName = $state('');
	let createDescription = $state('');
	let createColor = $state(DEFAULT_GROUP_COLOR);
	let createParent = $state<number | null>(null);
	let createSubmitted = $state(false);
	let creating = $state(false);
	let createError = $state('');
	const createParents = $derived(parentOptions(groups, null).filter((o) => isAdmin || o.group.canManageMembers));

	// Edit group
	let editingGroup = $state(false);
	let editName = $state('');
	let editDescription = $state('');
	let editColor = $state(DEFAULT_GROUP_COLOR);
	let editParent = $state<number | null>(null);
	let editSubmitted = $state(false);
	let editSaving = $state(false);
	let editError = $state('');

	// Deleting a group with sub-groups asks what happens to them
	let deleteChoiceOpen = $state(false);
	let deleteChoice = $state<'MoveUp' | 'Delete'>('MoveUp');
	let deleting = $state(false);

	// Add member
	let showAddMember = $state(false);
	let addMemberUserId = $state<number | null>(null);
	let addingMember = $state(false);
	let addMemberError = $state('');

	// Org members for the add dropdown (only the ones the caller may see; email may be hidden)
	let orgMembers = $state<Array<{ id: number; firstName: string; lastName: string; email: string | null }>>([]);
	// The organization's defaults (what a top-level group inherits)
	let orgDetail = $state<OrganizationDetailResponse | null>(null);

	// Permissions: all groups' overrides, so a sub-group can show what it inherits from where
	let allPermissions = $state<GroupPermissionsResponse[]>([]);
	let permissions = $derived(allPermissions.find((p) => p.groupId === selectedGroup?.id) ?? null);
	const permGroups = $derived(allPermissions.map((p) => ({ ...p, id: p.groupId, name: p.groupName })));
	let permissionsLoadError = $state('');
	let permissionsSaving = $state(false);
	let permissionsError = $state('');
	let detailTab = $state<'members' | 'permissions'>('members');

	// (Re)load whenever the slug in the URL changes, so switching orgs never shows stale data
	$effect(() => {
		if (!orgSlug) return;
		untrack(() => {
			loadGroups();
		});
	});

	async function loadGroups() {
		loading = true;
		error = '';
		try {
			const { data } = await groupApi.apiV1OrganizationsSlugGroupsGet(orgSlug);
			groups = data;
		} catch (err) {
			error = extractErrorMessage(err, t('groups.loadFailed'));
		} finally {
			loading = false;
		}
	}

	function toggleCollapsed(id: number) {
		collapsed = collapsed.includes(id) ? collapsed.filter((x) => x !== id) : [...collapsed, id];
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
		allPermissions = [];
		await refreshGroup(groupId);
	}

	/** Reloads the open group's detail (members) without collapsing it. */
	async function refreshGroup(groupId: number) {
		selectedGroupLoading = true;
		try {
			const { data } = await groupApi.apiV1OrganizationsSlugGroupsGroupIdGet(orgSlug, groupId);
			selectedGroup = data;
			if (isAdmin) loadPermissions();
		} catch (err) {
			toast.error(err, t('groups.loadDetailFailed'));
		} finally {
			selectedGroupLoading = false;
		}
	}

	function openCreateForm(parentId: number | null = null) {
		showCreateForm = parentId != null || !showCreateForm;
		createParent = parentId;
		createError = '';
		createSubmitted = false;
		if (showCreateForm) window.scrollTo({ top: 0, behavior: 'smooth' });
	}

	async function createGroup(e: Event) {
		e.preventDefault();
		createSubmitted = true;
		if (!createName.trim()) return;
		creating = true;
		createError = '';
		try {
			invalidateOrgDetail(orgSlug); // member/vacation figures change
			await groupApi.apiV1OrganizationsSlugGroupsPost(orgSlug, {
				name: createName.trim(),
				description: createDescription.trim() || undefined,
				color: createColor,
				parentGroupId: createParent
			});
			toast.success(t('groups.created'));
			showCreateForm = false;
			createName = '';
			createDescription = '';
			createColor = DEFAULT_GROUP_COLOR;
			createParent = null;
			createSubmitted = false;
			await loadGroups();
		} catch (err) {
			createError = extractErrorMessage(err, t('groups.createFailed'));
		} finally {
			creating = false;
		}
	}

	function startEdit() {
		if (!selectedGroup) return;
		editName = selectedGroup.name ?? '';
		editDescription = selectedGroup.description ?? '';
		editColor = safeColor(selectedGroup.color);
		editParent = groups.find((g) => g.id === selectedGroup?.id)?.parentGroupId ?? null;
		editingGroup = true;
		editSubmitted = false;
		editError = '';
	}

	async function saveEdit(e: Event) {
		e.preventDefault();
		if (!selectedGroup?.id) return;
		editSubmitted = true;
		if (!editName.trim()) return;
		editSaving = true;
		editError = '';
		try {
			invalidateOrgDetail(orgSlug); // member/vacation figures change
			await groupApi.apiV1OrganizationsSlugGroupsGroupIdPut(orgSlug, selectedGroup.id, {
				name: editName.trim(),
				description: editDescription.trim() || undefined,
				color: editColor,
				parentGroupId: editParent
			});
			toast.success(t('groups.saved'));
			editingGroup = false;
			await loadGroups();
			await refreshGroup(selectedGroup.id);
		} catch (err) {
			editError = extractErrorMessage(err, t('groups.saveFailed'));
		} finally {
			editSaving = false;
		}
	}

	async function deleteGroup() {
		if (!selectedGroup?.id) return;
		if (hasChildren(selectedGroup.id)) {
			deleteChoice = 'MoveUp';
			deleteChoiceOpen = true;
			return;
		}
		const ok = await confirmDialog({
			title: t('groups.deleteTitle', { name: selectedGroup.name ?? '' }),
			message: t('groups.deleteMessage'),
			confirmLabel: t('common.delete'),
			danger: true
		});
		if (ok) await performDelete();
	}

	/** Deletes the open group; with sub-groups `deleteChoice` says whether they move up or go too. */
	async function performDelete() {
		if (!selectedGroup?.id) return;
		deleting = true;
		try {
			invalidateOrgDetail(orgSlug); // member/vacation figures change
			const subGroups = hasChildren(selectedGroup.id) ? (deleteChoice === 'Delete' ? 1 : 0) : undefined;
			await groupApi.apiV1OrganizationsSlugGroupsGroupIdDelete(orgSlug, selectedGroup.id, subGroups);
			toast.success(t('groups.deleted'));
			deleteChoiceOpen = false;
			selectedGroup = null;
			await loadGroups();
		} catch (err) {
			toast.error(err, t('groups.deleteFailed'));
		} finally {
			deleting = false;
		}
	}

	async function loadOrgMembers() {
		if (orgMembers.length > 0) return;
		try {
			const data = await getOrgDetail(orgSlug);
			orgMembers = (data.members ?? []).map((m) => ({
				id: m.id!,
				firstName: m.firstName ?? '',
				lastName: m.lastName ?? '',
				email: m.email ?? null
			}));
		} catch (err) {
			orgMembers = [];
			addMemberError = extractErrorMessage(err, t('groups.members.loadOrgMembersFailed'));
		}
	}

	function availableMembers(): typeof orgMembers {
		const existingIds = new Set((selectedGroup?.members ?? []).filter((m) => !m.viaGroupName).map((m) => m.userId));
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
			invalidateOrgDetail(orgSlug); // member/vacation figures change
			await groupApi.apiV1OrganizationsSlugGroupsGroupIdMembersPost(orgSlug, selectedGroup.id, {
				userId: addMemberUserId
			});
			toast.success(t('groups.members.added'));
			showAddMember = false;
			addMemberUserId = null;
			await refreshGroup(selectedGroup.id);
			await loadGroups();
		} catch (err) {
			addMemberError = extractErrorMessage(err, t('groups.members.addFailed'));
		} finally {
			addingMember = false;
		}
	}

	async function removeMember(userId: number) {
		if (!selectedGroup?.id) return;
		const member = selectedGroup.members?.find((m) => m.userId === userId);
		const ok = await confirmDialog({
			title: t('groups.members.removeTitle', { name: `${member?.firstName ?? ''} ${member?.lastName ?? ''}`.trim() }),
			confirmLabel: t('common.remove'),
			danger: true
		});
		if (!ok) return;
		try {
			invalidateOrgDetail(orgSlug); // member/vacation figures change
			await groupApi.apiV1OrganizationsSlugGroupsGroupIdMembersTargetUserIdDelete(orgSlug, selectedGroup.id, userId);
			toast.success(t('groups.members.removed'));
			await refreshGroup(selectedGroup.id);
			await loadGroups();
		} catch (err) {
			toast.error(err, t('groups.members.removeFailed'));
		}
	}

	async function toggleMemberRole(member: GroupMemberResponse) {
		if (!selectedGroup?.id || !member.userId) return;
		const newRole = member.role === 'Admin' ? 'Member' : 'Admin';
		try {
			await groupApi.apiV1OrganizationsSlugGroupsGroupIdMembersTargetUserIdPatch(orgSlug, selectedGroup.id, member.userId, {
				role: newRole
			});
			toast.success(newRole === 'Admin' ? t('groups.members.promoted') : t('groups.members.demoted'));
			await refreshGroup(selectedGroup.id);
		} catch (err) {
			toast.error(err, t('groups.members.roleFailed'));
		}
	}

	// Permission settings definitions (labels follow the UI language)
	const PERM_RULE_KEYS = ['editPastEntriesMode', 'editPauseMode', 'csvImportMode', 'workScheduleChangeMode', 'initialOvertimeMode', 'absenceSelfServiceMode'] as const;
	const PERM_ADMIN_FLAG_KEYS = ['groupAdminCanApproveAbsences', 'groupAdminCanApproveTimeEdits', 'groupAdminCanApproveScheduleChanges', 'groupAdminCanManageMembers', 'groupAdminCanViewAllTimeEntries', 'groupAdminCanExportData'] as const;
	type PermKey = (typeof PERM_RULE_KEYS)[number] | (typeof PERM_ADMIN_FLAG_KEYS)[number];

	function permSetting<K extends PermKey>(key: K) {
		return { key, label: t(`groups.perm.${key}.label`), description: t(`groups.perm.${key}.description`) };
	}

	let permRuleSettings = $derived(PERM_RULE_KEYS.map(permSetting));
	let permAdminFlags = $derived(PERM_ADMIN_FLAG_KEYS.map(permSetting));

	async function loadPermissions() {
		permissionsLoadError = '';
		try {
			const [{ data }, org] = await Promise.all([groupApi.apiV1OrganizationsSlugGroupsPermissionsGet(orgSlug), getOrgDetail(orgSlug)]);
			allPermissions = data;
			orgDetail = org;
		} catch (err) {
			allPermissions = [];
			permissionsLoadError = extractErrorMessage(err, t('groups.perm.loadFailed'));
		}
	}

	// ── "geerbt von …" hints for settings left on inherit ──

	function inheritText<V>(inherited: Inherited<V>, orgValue: V, label: (v: V) => string): string {
		const value = label(inherited.value ?? orgValue);
		return inherited.source ? t('groups.inherit.fromGroup', { group: inherited.source, value }) : t('groups.inherit.fromOrg', { value });
	}

	function ruleHint(key: (typeof PERM_RULE_KEYS)[number]): string {
		const id = selectedGroup?.id;
		if (id == null) return '';
		return inheritText(inheritedFrom(permGroups, id, (g) => g[key]), orgDetail?.[key] ?? null, ruleModeLabel);
	}

	function flagHint(key: (typeof PERM_ADMIN_FLAG_KEYS)[number]): string {
		const id = selectedGroup?.id;
		if (id == null) return '';
		return inheritText(inheritedFrom(permGroups, id, (g) => g[key]), false, (v) => (v ? t('groups.perm.enabled') : t('groups.perm.disabled')));
	}

	function coreHoursHint(): string {
		const id = selectedGroup?.id;
		if (id == null) return '';
		const window = (mode: string | null | undefined, start?: string | null, end?: string | null) =>
			mode === 'Own' ? `${start ?? ''}–${end ?? ''}` : t('groups.perm.coreHours.off');
		const inherited = inheritedFrom(permGroups, id, (g) => (g.coreHoursMode ? window(g.coreHoursMode, g.coreHoursStart, g.coreHoursEnd) : null));
		const orgWindow = orgDetail?.coreHoursEnabled ? window('Own', orgDetail.coreHoursStart, orgDetail.coreHoursEnd) : window('Off');
		return inheritText(inherited, orgWindow, (v) => v);
	}

	/** Per cell of the matrix: what the group inherits (parent's effective value, else the org's) and from where. */
	const inheritedVisibility = $derived.by(() => {
		const id = selectedGroup?.id;
		const sources = new Map<string, string | null>();
		const rows = VISIBILITY_KINDS.map((kind) => {
			const row: { kind: string; inGroups: boolean; outsideGroups: boolean } = { kind, inGroups: false, outsideGroups: false };
			for (const c of ['inGroups', 'outsideGroups'] as Column[]) {
				const inherited = id == null ? { source: null, value: null } : inheritedFrom(permGroups, id, (g) => groupCell(g.visibility, kind, c));
				row[c] = inherited.value ?? cell(orgDetail?.visibility, kind, c);
				sources.set(`${kind}:${c}`, inherited.source);
			}
			return row;
		});
		return { rows, source: (kind: VisibilityKind, c: Column) => sources.get(`${kind}:${c}`) ?? null };
	});

	/** The group's own overrides with inherited ancestor overrides filled in (for the preview sentence). */
	const resolvedVisibility = $derived(
		VISIBILITY_KINDS.map((kind) => {
			const own = (c: Column) => groupCell(permissions?.visibility, kind, c);
			const fromAncestor = (c: Column) => (inheritedVisibility.source(kind, c) ? cell(inheritedVisibility.rows, kind, c) : null);
			return { kind, inGroups: own('inGroups') ?? fromAncestor('inGroups'), outsideGroups: own('outsideGroups') ?? fromAncestor('outsideGroups') };
		})
	);

	/** Core hours override: null = inherit, 0 = off, 1 = own window (times default to 09:00-15:00). */
	function setCoreHours(mode: number | null, start?: string, end?: string) {
		if (mode !== 1) return setPermValue('coreHoursMode', mode);
		return setPermValue('coreHoursMode', 1, {
			coreHoursStart: start ?? permissions?.coreHoursStart ?? '09:00',
			coreHoursEnd: end ?? permissions?.coreHoursEnd ?? '15:00'
		});
	}

	/** One row of the group's visibility overrides (null = inherit), sent as is (no -1 marker). */
	function setVisibility(kind: VisibilityKind, inGroups: boolean | null, outsideGroups: boolean | null) {
		return savePermissions({ visibility: [{ kind, inGroups, outsideGroups }] });
	}

	/** A group admin capability: on/off, or back to inherit (from the parent; at the top: off). */
	function setFlag(key: (typeof PERM_ADMIN_FLAG_KEYS)[number], value: number | null) {
		return savePermissions(value === null ? { inheritCapabilities: [key] } : { [key]: value === 1 });
	}

	function setPermValue(key: string, value: number | boolean | null, extra: Record<string, unknown> = {}) {
		// "Inherit" (null) is transmitted as -1; the backend clears the override
		return savePermissions({ [key]: value === null ? -1 : value, ...extra });
	}

	async function savePermissions(payload: Record<string, unknown>) {
		if (!selectedGroup?.id) return;
		permissionsSaving = true;
		permissionsError = '';
		try {
			invalidateOrgDetail(orgSlug); // member/vacation figures change
			await groupApi.apiV1OrganizationsSlugGroupsGroupIdPermissionsPut(orgSlug, selectedGroup.id, payload as any);
			toast.success(t('groups.perm.saved'));
			await loadPermissions();
		} catch (err) {
			permissionsError = extractErrorMessage(err, t('groups.perm.saveFailed'));
		} finally {
			permissionsSaving = false;
		}
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: orgContext.selectedOrg?.name ? `${t('groups.title')} – ${orgContext.selectedOrg.name}` : t('groups.title') })}</title>
</svelte:head>

{#snippet colorPicker(value: string, onpick: (c: string) => void, id: string, disabled: boolean)}
	<div class="flex items-center gap-1.5 flex-wrap">
		{#each GROUP_COLOR_PRESETS as c (c)}
			<button
				type="button"
				class="w-6 h-6 rounded-full border-2 transition-transform {value.toLowerCase() === c.toLowerCase() ? 'border-base-content scale-110' : 'border-transparent'}"
				style="background-color: {c}"
				aria-label={t('groups.colorSwatch', { color: c })}
				aria-pressed={value.toLowerCase() === c.toLowerCase()}
				{disabled}
				onclick={() => onpick(c)}
			></button>
		{/each}
		<input {id} type="color" class="w-6 h-6 rounded cursor-pointer border-0 p-0" aria-label={t('groups.customColor')} {value} {disabled} oninput={(e) => onpick(e.currentTarget.value)} />
	</div>
{/snippet}

{#snippet parentSelect(id: string, value: number | null, options: { group: GroupResponse; depth: number }[], onpick: (v: number | null) => void, disabled: boolean, allowTop: boolean)}
	<div class="form-control w-full sm:w-auto">
		<label for={id} class="label text-xs">{t('groups.parent')}</label>
		<select {id} class="select select-sm w-full sm:w-56" {disabled} value={value ?? ''} onchange={(e) => onpick(e.currentTarget.value ? Number(e.currentTarget.value) : null)}>
			{#if allowTop}<option value="">{t('groups.noParent')}</option>{/if}
			{#each options as o (o.group.id)}
				<option value={o.group.id}>{'  '.repeat(o.depth - 1)}{o.group.name}</option>
			{/each}
		</select>
	</div>
{/snippet}

{#snippet hint(text: string)}
	<div class="text-xs text-info mt-0.5" data-testid="inherited-hint">{text}</div>
{/snippet}

{#snippet groupTree(nodes: GroupNode<GroupResponse>[])}
	{#each nodes as node (node.group.id)}
		{@const group = node.group}
		{@const isSelected = selectedGroup?.id === group.id}
		{@const isCollapsed = collapsed.includes(group.id!)}
		<li class="grid grid-cols-[minmax(0,1fr)] gap-2 min-w-0" data-testid="group-node" data-depth={node.depth}>
			<div class="flex items-stretch gap-0.5 sm:gap-1">
				{#if node.children.length > 0}
					<button
						type="button"
						class="btn btn-ghost btn-xs self-center px-0.5 sm:px-1"
						aria-expanded={!isCollapsed}
						aria-controls="subgroups-{group.id}"
						aria-label={isCollapsed ? t('groups.expand', { name: group.name ?? '' }) : t('groups.collapse', { name: group.name ?? '' })}
						onclick={() => toggleCollapsed(group.id!)}
					>
						<svg class="w-4 h-4 transition-transform" class:-rotate-90={isCollapsed} viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clip-rule="evenodd"/></svg>
					</button>
				{:else}
					<span class="w-5 sm:w-6 shrink-0" aria-hidden="true"></span>
				{/if}
				<div class="flex-1 min-w-0 bg-base-100 border border-base-300 rounded-xl overflow-hidden transition-shadow" class:shadow-md={isSelected}>
					<!-- Group card header -->
					<button
						data-testid="group-header"
						class="w-full flex items-center gap-3 p-3 sm:p-4 text-left hover:bg-base-200/50 transition-colors cursor-pointer"
						onclick={() => selectGroup(group.id!)}
						aria-expanded={isSelected}
					>
						<div class="w-3 h-8 rounded-full shrink-0" style="background-color: {safeColor(group.color)}" aria-hidden="true"></div>
						<div class="flex-1 min-w-0">
							<div class="font-semibold text-base-content truncate">{group.name}</div>
							{#if group.description}
								<div class="text-sm text-base-content/50 truncate">{group.description}</div>
							{/if}
						</div>
						<div class="flex items-center gap-1 text-sm text-base-content/50">
							<svg class="w-4 h-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"><path d="M13 6a3 3 0 11-6 0 3 3 0 016 0zM18 8a2 2 0 11-4 0 2 2 0 014 0zM14 15a4 4 0 00-8 0v3h8v-3zM6 8a2 2 0 11-4 0 2 2 0 014 0zM16 18v-3a5.972 5.972 0 00-.75-2.906A3.005 3.005 0 0119 15v3h-3zM4.75 12.094A5.973 5.973 0 004 15v3H1v-3a3 3 0 013.75-2.906z"/></svg>
							<span>{group.memberCount ?? 0}</span>
						</div>
						{#if group.isGroupAdmin}
							<span class="badge badge-sm badge-primary badge-outline" title={t('groups.groupAdmin')}>{t('groups.adminBadge')}</span>
						{/if}
					</button>

					<!-- Expanded detail panel -->
					{#if isSelected}
						<div class="border-t border-base-300 p-3 sm:p-4">
							{@render detail(group)}
						</div>
					{/if}
				</div>
			</div>
			{#if node.children.length > 0 && !isCollapsed}
				<ul id="subgroups-{group.id}" class="grid grid-cols-[minmax(0,1fr)] gap-2 pl-1.5 sm:pl-7 border-l border-base-300 ml-2.5 sm:ml-3" role="list" aria-label={t('groups.subGroupOf', { name: group.name ?? '' })}>
					{@render groupTree(node.children)}
				</ul>
			{/if}
		</li>
	{/each}
{/snippet}

{#snippet detail(group: GroupResponse)}
	{#if selectedGroupLoading}
		<div class="flex items-center gap-2 justify-center py-4 text-base-content/40">
			<span class="loading loading-spinner loading-xs"></span><span class="text-sm">{t('common.loading')}</span>
		</div>
	{:else if selectedGroup}
		{#if group.parentGroupId}
			<p class="text-xs text-base-content/60 mb-3">{t('groups.subGroupOf', { name: groupName(group.parentGroupId) })}</p>
		{/if}
		<!-- Edit form or group info + actions -->
		{#if editingGroup}
			<form onsubmit={saveEdit} class="mb-4 bg-base-200/50 p-3 rounded-lg border border-base-300" novalidate>
				{#if editError}
					<div class="alert alert-error mb-3 text-sm">{editError}</div>
				{/if}
				<div class="flex gap-3 items-end flex-wrap">
					<div class="form-control w-full sm:w-auto">
						<label for="editName" class="label text-xs">{t('groups.name')}</label>
						<input id="editName" type="text" class="input input-sm w-full" class:input-error={editSubmitted && !editName.trim()} bind:value={editName} required disabled={editSaving} />
						{#if editSubmitted && !editName.trim()}<span class="text-xs text-error mt-1">{t('common.required')}</span>{/if}
					</div>
					<div class="form-control w-full sm:w-auto">
						<label for="editDesc" class="label text-xs">{t('groups.description')}</label>
						<input id="editDesc" type="text" class="input input-sm w-full sm:w-56" bind:value={editDescription} disabled={editSaving} />
					</div>
					{@render parentSelect('editParent', editParent, parentOptions(groups, group.id!), (v) => (editParent = v), editSaving, true)}
					<div class="form-control">
						<span class="label text-xs">{t('groups.color')}</span>
						{@render colorPicker(editColor, (c) => (editColor = c), 'editColor', editSaving)}
					</div>
					<div class="flex gap-1.5">
						<button type="submit" class="btn btn-primary btn-sm" disabled={editSaving}>
							{editSaving ? t('common.saving') : t('common.save')}
						</button>
						<button type="button" class="btn btn-ghost btn-sm" onclick={() => (editingGroup = false)}>{t('common.cancel')}</button>
					</div>
				</div>
			</form>
		{:else if isAdmin || group.canManageMembers}
			<div class="flex items-center gap-2 mb-4 flex-wrap">
				{#if isAdmin}
					<button class="btn btn-ghost btn-sm" onclick={startEdit}>{t('common.edit')}</button>
					<button class="btn btn-ghost btn-sm text-error" onclick={deleteGroup}>{t('common.delete')}</button>
				{/if}
				{#if chain(groups, group.id!).length < MAX_GROUP_DEPTH}
					<button class="btn btn-ghost btn-sm" onclick={() => openCreateForm(group.id!)}>{t('groups.createSub')}</button>
				{/if}
			</div>
		{/if}

		<!-- Tab switcher (Members / Permissions) -->
		{#if isAdmin}
			<div class="tabs mb-4" role="tablist">
				<button class="tab {detailTab === 'members' ? 'tab-active' : ''}" role="tab" aria-selected={detailTab === 'members'} onclick={() => (detailTab = 'members')}>{t('groups.tabs.members')}</button>
				<button class="tab {detailTab === 'permissions' ? 'tab-active' : ''}" role="tab" aria-selected={detailTab === 'permissions'} onclick={() => (detailTab = 'permissions')}>{t('groups.tabs.permissions')}</button>
			</div>
		{/if}

		{#if detailTab === 'members'}
			<!-- Members section -->
			<div class="flex items-center justify-between mb-3">
				<h3 class="text-sm font-semibold text-base-content/70">{t('groups.members.heading', { count: selectedGroup.members?.length ?? 0 })}</h3>
				{#if isAdmin || group.canManageMembers}
					<button class="btn btn-ghost btn-xs" onclick={openAddMember}>
						{t('groups.members.add')}
					</button>
				{/if}
			</div>

			<!-- Add member form -->
			{#if showAddMember}
				<div class="bg-base-200/50 p-3 rounded-lg border border-base-300 mb-3">
					{#if addMemberError}
						<div class="alert alert-error mb-2 text-sm">{addMemberError}</div>
					{/if}
					<form onsubmit={addMember} class="flex gap-2 items-end flex-wrap">
						<div class="form-control flex-1 min-w-[12rem]">
							<label for="addMemberId" class="label text-xs">{t('groups.members.select')}</label>
							<select id="addMemberId" class="select select-sm w-full" bind:value={addMemberUserId} disabled={addingMember}>
								<option value={null}>{t('groups.members.selectPlaceholder')}</option>
								{#each availableMembers() as m}
									<option value={m.id}>{m.firstName} {m.lastName}{m.email ? ` (${m.email})` : ''}</option>
								{/each}
							</select>
						</div>
						<button type="submit" class="btn btn-primary btn-sm" disabled={addingMember || !addMemberUserId}>
							{addingMember ? t('common.adding') : t('common.add')}
						</button>
						<button type="button" class="btn btn-ghost btn-sm" onclick={() => (showAddMember = false)}>{t('common.cancel')}</button>
					</form>
				</div>
			{/if}

			<!-- Member list: direct members, then the sub-groups' members ("über Team Küche") -->
			{#if (selectedGroup.members?.length ?? 0) === 0}
				<p class="text-base-content/40 text-sm">{t('groups.members.empty')}</p>
			{:else}
				<div class="space-y-1">
					{#each selectedGroup.members ?? [] as member (member.userId)}
						<div class="flex items-center gap-3 p-2 rounded-lg hover:bg-base-200/50 transition-colors flex-wrap sm:flex-nowrap">
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
								{#if member.email}<div class="text-xs text-base-content/60 truncate">{member.email}</div>{/if}
							</div>
							{#if member.viaGroupName}
								<span class="badge badge-xs badge-ghost border-dashed" data-testid="member-via">{t('groups.via', { group: member.viaGroupName })}</span>
							{:else}
								{#if member.role === 'Admin'}
									<span class="badge badge-xs badge-primary" title={t('groups.groupAdmin')}>{t('groups.adminBadge')}</span>
								{/if}
								{#if isAdmin}
									<div class="flex items-center gap-1">
										<button
											class="btn btn-ghost btn-xs"
											title={member.role === 'Admin' ? t('groups.members.demoteTitle') : t('groups.members.promoteTitle')}
											onclick={() => toggleMemberRole(member)}
										>
											{member.role === 'Admin' ? t('groups.members.demote') : t('groups.members.promote')}
										</button>
										<button
											class="btn btn-ghost btn-xs text-error"
											title={t('groups.members.removeFromGroup')}
											aria-label={t('groups.members.removeLabel', { name: `${member.firstName ?? ''} ${member.lastName ?? ''}`.trim() })}
											onclick={() => removeMember(member.userId!)}
										>
											&times;
										</button>
									</div>
								{/if}
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
			<p class="text-xs text-base-content/50 mb-4">{t('groups.perm.intro')}</p>

			<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
				<!-- Rule mode overrides -->
				<div class="px-3 py-2 bg-base-200/50 border-b border-base-300">
					<span class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">{t('groups.perm.rules')}</span>
				</div>
				{#each permRuleSettings as rs (rs.key)}
					{@const value = permissions[rs.key]}
					<div class="flex items-center justify-between gap-3 p-3 border-b border-base-200 last:border-b-0 flex-wrap">
						<div>
							<div class="text-sm font-medium text-base-content">{rs.label}</div>
							<div class="text-xs text-base-content/50">{rs.description}</div>
							{#if value == null}{@render hint(ruleHint(rs.key))}{/if}
						</div>
						<ModeSelector
							options={withInherit(ruleModeOptions())}
							value={value == null ? null : parseRuleMode(value)}
							onselect={(v) => setPermValue(rs.key, v as number | null)}
							disabled={permissionsSaving}
							size="xs"
						/>
					</div>
				{/each}

				<!-- Member visibility matrix overrides -->
				<div class="px-3 py-2 bg-base-200/50 border-b border-base-300">
					<span class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">{t('groups.perm.visibility')}</span>
				</div>
				<div class="p-3 border-b border-base-200" data-testid="group-visibility">
					<p class="text-xs text-base-content/50 mb-2">{t('visibility.groupIntro')}</p>
					<VisibilityMatrix mode="group" org={inheritedVisibility.rows} group={permissions.visibility} disabled={permissionsSaving}
						ongroupchange={setVisibility} inheritedSource={inheritedVisibility.source} />
					<p class="text-sm mt-2 mb-0" aria-live="polite">{previewSentence(orgDetail?.visibility, { name: group.name, visibility: resolvedVisibility })}</p>
				</div>

				<!-- Core working hours override -->
				<div class="px-3 py-2 bg-base-200/50 border-b border-base-300">
					<span class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">{t('groups.perm.coreHours.title')}</span>
				</div>
				<div class="flex items-center justify-between gap-3 p-3 border-b border-base-200 flex-wrap" data-testid="group-core-hours">
					<div>
						<div class="text-sm font-medium text-base-content">{t('groups.perm.coreHours.label')}</div>
						<div class="text-xs text-base-content/50">{t('groups.perm.coreHours.description')}</div>
						{#if !permissions.coreHoursMode}{@render hint(coreHoursHint())}{/if}
					</div>
					<ModeSelector
						options={withInherit([
							{ value: 0, label: t('groups.perm.coreHours.off'), activeClass: 'btn-neutral' },
							{ value: 1, label: t('groups.perm.coreHours.own'), activeClass: 'btn-primary' }
						])}
						value={permissions.coreHoursMode === 'Own' ? 1 : permissions.coreHoursMode === 'Off' ? 0 : null}
						onselect={(v) => setCoreHours(v as number | null)}
						disabled={permissionsSaving}
						size="xs"
					/>
					{#if permissions.coreHoursMode === 'Own'}
						<div class="flex items-center gap-2 text-sm w-full justify-end">
							<input type="time" class="input input-xs w-24" aria-label={t('groups.perm.coreHours.start')} value={permissions.coreHoursStart ?? '09:00'}
								onchange={(e) => setCoreHours(1, e.currentTarget.value, undefined)} disabled={permissionsSaving} />
							<span class="text-base-content/60">–</span>
							<input type="time" class="input input-xs w-24" aria-label={t('groups.perm.coreHours.end')} value={permissions.coreHoursEnd ?? '15:00'}
								onchange={(e) => setCoreHours(1, undefined, e.currentTarget.value)} disabled={permissionsSaving} />
						</div>
					{/if}
				</div>

				<!-- Group Admin capability flags -->
				<div class="px-3 py-2 bg-base-200/50 border-b border-base-300">
					<span class="text-xs font-semibold text-base-content/60 uppercase tracking-wide">{t('groups.perm.capabilities')}</span>
				</div>
				{#each permAdminFlags as flag (flag.key)}
					{@const value = permissions[flag.key]}
					<div class="flex items-center justify-between gap-3 p-3 border-b border-base-200 last:border-b-0 flex-wrap">
						<div>
							<div class="text-sm font-medium text-base-content">{flag.label}</div>
							<div class="text-xs text-base-content/50">{flag.description}</div>
							{#if value == null}{@render hint(flagHint(flag.key))}{/if}
						</div>
						<ModeSelector
							options={withInherit([
								{ value: 0, label: t('groups.perm.disabled'), activeClass: 'btn-neutral' },
								{ value: 1, label: t('groups.perm.enabled'), activeClass: 'btn-success' }
							])}
							value={value == null ? null : value ? 1 : 0}
							onselect={(v) => setFlag(flag.key, v as number | null)}
							disabled={permissionsSaving}
							size="xs"
						/>
					</div>
				{/each}
			</div>
		{:else if detailTab === 'permissions' && isAdmin && permissionsLoadError}
			<div class="alert alert-error text-sm" role="alert">
				<span>{permissionsLoadError}</span>
				<button class="btn btn-sm" onclick={() => loadPermissions()}>{t('common.retry')}</button>
			</div>
		{:else if detailTab === 'permissions' && isAdmin && !permissions}
			<div class="flex items-center gap-2 justify-center py-4 text-base-content/40">
				<span class="loading loading-spinner loading-xs"></span><span class="text-sm">{t('groups.perm.loading')}</span>
			</div>
		{/if}
	{/if}
{/snippet}

<div class="max-w-5xl mx-auto sm:px-6 pb-6">
	{#if loading}
		<Skeleton />
	{:else if error && groups.length === 0}
		<div class="alert alert-error">{error}</div>
	{:else}
		<div class="pt-4">
			<!-- Header -->
			<div class="flex items-center justify-between gap-2 mb-4 flex-wrap">
				<h2 class="text-xl font-bold text-base-content m-0">{t('groups.title')}</h2>
				{#if isAdmin}
					<button class="btn btn-primary btn-sm" onclick={() => openCreateForm()} aria-expanded={showCreateForm}>
						{showCreateForm ? t('common.cancel') : t('groups.create')}
					</button>
				{/if}
			</div>

			{#if error}
				<div class="alert alert-error mb-4 text-sm">{error}</div>
			{/if}

			<!-- Create form -->
			{#if showCreateForm && (isAdmin || createParent != null)}
				<div class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300">
					{#if createError}
						<div class="alert alert-error mb-3 text-sm">{createError}</div>
					{/if}
					<form onsubmit={createGroup} class="flex gap-3 items-end flex-wrap" novalidate>
						<div class="form-control w-full sm:w-auto">
							<label for="groupName" class="label text-xs">{t('groups.name')}</label>
							<input id="groupName" type="text" class="input input-sm w-full" class:input-error={createSubmitted && !createName.trim()} bind:value={createName} required disabled={creating} placeholder={t('groups.namePlaceholder')} />
							{#if createSubmitted && !createName.trim()}<span class="text-xs text-error mt-1">{t('common.required')}</span>{/if}
						</div>
						<div class="form-control w-full sm:w-auto">
							<label for="groupDesc" class="label text-xs">{t('groups.description')}</label>
							<input id="groupDesc" type="text" class="input input-sm w-full sm:w-56" bind:value={createDescription} disabled={creating} placeholder={t('groups.descriptionPlaceholder')} />
						</div>
						{@render parentSelect('groupParent', createParent, createParents, (v) => (createParent = v), creating, isAdmin)}
						<div class="form-control">
							<span class="label text-xs">{t('groups.color')}</span>
							{@render colorPicker(createColor, (c) => (createColor = c), 'groupColor', creating)}
						</div>
						<div class="flex gap-1.5">
							<button type="submit" class="btn btn-primary btn-sm" disabled={creating}>
								{creating ? t('common.creating') : t('common.create')}
							</button>
							{#if !isAdmin}
								<button type="button" class="btn btn-ghost btn-sm" onclick={() => (showCreateForm = false)}>{t('common.cancel')}</button>
							{/if}
						</div>
					</form>
				</div>
			{/if}

			{#if groups.length === 0}
				<p class="text-base-content/40 text-sm">{t('groups.empty')}{#if isAdmin} {t('groups.emptyAdminHint')}{/if}</p>
			{:else}
				<ul class="grid grid-cols-[minmax(0,1fr)] gap-2" role="list" aria-label={t('groups.treeLabel')}>
					{@render groupTree(tree)}
				</ul>
			{/if}
		</div>
	{/if}
</div>

<Modal open={deleteChoiceOpen} title={t('groups.deleteTitle', { name: selectedGroup?.name ?? '' })} onclose={() => { if (!deleting) deleteChoiceOpen = false; }}>
	{@const parentName = groupName(groups.find((g) => g.id === selectedGroup?.id)?.parentGroupId)}
	<p class="text-sm mb-3">{t('groups.deleteSub.message', { name: selectedGroup?.name ?? '' })}</p>
	<fieldset class="grid gap-3">
		<legend class="sr-only">{t('groups.deleteSub.message', { name: selectedGroup?.name ?? '' })}</legend>
		<label class="flex gap-3 items-start cursor-pointer">
			<input type="radio" class="radio radio-sm mt-0.5" name="subGroups" value="MoveUp" bind:group={deleteChoice} />
			<span>
				<span class="text-sm font-medium block">{t('groups.deleteSub.moveUp')}</span>
				<span class="text-xs text-base-content/60">{parentName ? t('groups.deleteSub.moveUpHint', { parent: parentName }) : t('groups.deleteSub.moveUpTopHint')}</span>
			</span>
		</label>
		<label class="flex gap-3 items-start cursor-pointer">
			<input type="radio" class="radio radio-sm mt-0.5" name="subGroups" value="Delete" bind:group={deleteChoice} />
			<span>
				<span class="text-sm font-medium block">{t('groups.deleteSub.delete')}</span>
				<span class="text-xs text-base-content/60">{t('groups.deleteSub.deleteHint')}</span>
			</span>
		</label>
	</fieldset>
	<p class="text-xs text-base-content/60 mt-3 mb-0">{t('groups.deleteMessage')}</p>
	{#snippet actions()}
		<button class="btn btn-ghost btn-sm" onclick={() => (deleteChoiceOpen = false)} disabled={deleting}>{t('common.cancel')}</button>
		<button class="btn btn-error btn-sm" onclick={performDelete} disabled={deleting}>{t('common.delete')}</button>
	{/snippet}
</Modal>
