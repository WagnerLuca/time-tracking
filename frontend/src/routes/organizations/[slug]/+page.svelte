<script lang="ts">
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { organizationsApi, workScheduleApi, absenceDayApi, notificationsApi, groupApi } from '$lib/apiClient';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import { isOrgAdmin, ruleMode } from '$lib/utils/orgRules';
	import SchedulePeriodEditor from '$lib/components/SchedulePeriodEditor.svelte';
	import InitialOvertimeEditor from '$lib/components/InitialOvertimeEditor.svelte';
	import MemberSchedule from '$lib/components/member/MemberSchedule.svelte';
	import MemberVacation from '$lib/components/member/MemberVacation.svelte';
	import MemberAbsences from '$lib/components/member/MemberAbsences.svelte';
	import type {
		OrganizationDetailResponse,
		UpdateOrganizationRequest,
		WorkScheduleResponse,
		AbsenceTypeDefinitionResponse,
		EffectivePermissionsResponse
	} from '$lib/api';

	const orgSlug = $derived(page.params.slug ?? '');

	let org = $state<OrganizationDetailResponse | null>(null);
	let loading = $state(true);
	let error = $state('');

	let workSchedule = $state<WorkScheduleResponse | null>(null);
	let scheduleError = $state('');
	let perms = $state<EffectivePermissionsResponse | null>(null);
	let absenceTypes = $state<AbsenceTypeDefinitionResponse[]>([]);

	let myMember = $derived(org?.members?.find((m) => m.id === auth.user?.id) ?? null);
	let myRole = $derived(myMember?.role ?? null);
	let isAdmin = $derived(isOrgAdmin(myRole));
	let isOwner = $derived(myRole === 'Owner');
	let absenceMode = $derived(ruleMode('absenceSelfServiceMode', perms, org));
	let canAddAbsence = $derived(isAdmin || (absenceMode !== null && absenceMode !== 'Disabled'));

	// Edit organization
	let editing = $state(false);
	let editName = $state('');
	let editDescription = $state('');
	let editSlug = $state('');
	let editWebsite = $state('');
	let editSubmitted = $state(false);
	let editError = $state('');
	let editSaving = $state(false);
	let nameError = $derived(editSubmitted && !editName.trim() ? 'Name is required.' : '');
	let slugError = $derived(
		!editSubmitted ? '' : !editSlug.trim() ? 'Slug is required.' : !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(editSlug.trim()) ? 'Use lowercase letters, digits and hyphens only.' : ''
	);

	let deleting = $state(false);

	// Unread "organization rules changed" notifications
	let settingsNotificationIds = $state<number[]>([]);

	$effect(() => {
		if (!orgSlug) return;
		untrack(() => loadOrg());
	});

	async function loadOrg() {
		loading = true;
		error = '';
		editing = false;
		perms = null;
		settingsNotificationIds = [];
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;
		} catch (err) {
			error = getErrorStatus(err) === 404 ? 'Organization not found.' : extractErrorMessage(err, 'Could not load the organization.');
			loading = false;
			return;
		}
		loading = false;
		loadSchedule();
		loadPerms();
		loadAbsenceTypes();
		loadSettingsNotifications();
	}

	/** Reload org data without the page spinner (keeps the scroll position). */
	async function reloadOrg() {
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;
		} catch (err) {
			toast.error(err, 'Could not reload the organization.');
		}
	}

	async function loadSchedule() {
		scheduleError = '';
		try {
			const { data } = await workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(orgSlug);
			workSchedule = data;
		} catch (err) {
			workSchedule = null;
			if (getErrorStatus(err) !== 404) scheduleError = extractErrorMessage(err, 'Could not load your schedule.');
		}
	}

	async function loadPerms() {
		try {
			const { data } = await groupApi.apiV1OrganizationsSlugEffectivePermissionsGet(orgSlug);
			perms = data;
		} catch (err) {
			// The org defaults still apply (ruleMode falls back to org)
			toast.error(err, 'Could not load your permissions.');
		}
	}

	async function loadAbsenceTypes() {
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesGet(orgSlug);
			absenceTypes = data;
		} catch (err) {
			absenceTypes = [];
			toast.error(err, 'Could not load the absence types.');
		}
	}

	async function loadSettingsNotifications() {
		const orgId = org?.id;
		try {
			const { data } = await notificationsApi.apiV1NotificationsGet(true, 100);
			settingsNotificationIds = (data.items ?? [])
				.filter((n) => n.type === 'SettingsChanged' && n.organizationId === orgId && !n.isRead && n.id != null)
				.map((n) => n.id!);
		} catch {
			// Optional hint banner only; the notification bell in the layout reports its own errors
			settingsNotificationIds = [];
		}
	}

	async function dismissSettingsNotifications() {
		const ids = settingsNotificationIds;
		settingsNotificationIds = [];
		const results = await Promise.allSettled(ids.map((id) => notificationsApi.apiV1NotificationsIdReadPut(id)));
		const failed = results.find((r) => r.status === 'rejected');
		if (failed) toast.error((failed as PromiseRejectedResult).reason, 'Could not mark the notification as read.');
	}

	function startEdit() {
		if (!org) return;
		editName = org.name ?? '';
		editDescription = org.description ?? '';
		editSlug = org.slug ?? '';
		editWebsite = org.website ?? '';
		editSubmitted = false;
		editError = '';
		editing = true;
	}

	async function saveEdit(e: Event) {
		e.preventDefault();
		editSubmitted = true;
		if (nameError || slugError) return;
		editError = '';
		editSaving = true;
		const newSlug = editSlug.trim();
		try {
			const payload: UpdateOrganizationRequest = {
				name: editName.trim(),
				description: editDescription.trim() || undefined,
				slug: newSlug,
				website: editWebsite.trim() || undefined
			};
			await organizationsApi.apiV1OrganizationsSlugPut(orgSlug, payload);
			toast.success('Organization saved.');
			editing = false;
			if (newSlug !== orgSlug) await goto(`/organizations/${newSlug}`, { replaceState: true });
			else await reloadOrg();
		} catch (err) {
			editError = extractErrorMessage(err, 'Could not save the organization.');
		} finally {
			editSaving = false;
		}
	}

	async function deleteOrg() {
		const ok = await confirmDialog({
			title: `Delete ${org?.name ?? 'organization'}?`,
			message: 'All members lose access and this cannot be undone.',
			confirmLabel: 'Delete organization',
			danger: true
		});
		if (!ok) return;
		deleting = true;
		try {
			await organizationsApi.apiV1OrganizationsSlugDelete(orgSlug);
			toast.success('Organization deleted.');
			goto('/organizations');
		} catch (err) {
			toast.error(err, 'Could not delete the organization.');
			deleting = false;
		}
	}
</script>

<svelte:head>
	<title>{org?.name ?? 'Organization'} – Time Tracking</title>
</svelte:head>

<div class="max-w-5xl mx-auto px-4 sm:px-6 pb-6">
	{#if loading}
		<div class="flex items-center gap-3 justify-center py-12 text-base-content/50"><span class="loading loading-spinner loading-sm"></span><span>Loading…</span></div>
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if org}
		{#if editing}
			<div class="card bg-base-100 border border-base-300 p-5 sm:p-8 max-w-xl">
				<h2 class="text-xl font-bold text-base-content mb-5">Edit organization</h2>
				{#if editError}
					<div class="alert alert-error mb-4 text-sm">{editError}</div>
				{/if}
				<form onsubmit={saveEdit} novalidate class="flex flex-col gap-4">
					<div>
						<label for="editName" class="label text-sm font-medium text-base-content/70">Name</label>
						<input id="editName" type="text" class="input input-bordered w-full" class:input-error={nameError} aria-invalid={!!nameError} bind:value={editName} disabled={editSaving} />
						{#if nameError}<p class="text-error text-xs mt-1">{nameError}</p>{/if}
					</div>
					<div>
						<label for="editSlug" class="label text-sm font-medium text-base-content/70">Slug</label>
						<input id="editSlug" type="text" class="input input-bordered w-full" class:input-error={slugError} aria-invalid={!!slugError} bind:value={editSlug} disabled={editSaving} />
						{#if slugError}<p class="text-error text-xs mt-1">{slugError}</p>{/if}
					</div>
					<div>
						<label for="editDesc" class="label text-sm font-medium text-base-content/70">Description</label>
						<textarea id="editDesc" class="textarea textarea-bordered w-full" bind:value={editDescription} rows="3" disabled={editSaving}></textarea>
					</div>
					<div>
						<label for="editWeb" class="label text-sm font-medium text-base-content/70">Website</label>
						<input id="editWeb" type="url" class="input input-bordered w-full" bind:value={editWebsite} disabled={editSaving} />
					</div>
					<div class="flex gap-3 justify-end mt-2">
						<button type="button" class="btn btn-ghost btn-sm" onclick={() => (editing = false)} disabled={editSaving}>Cancel</button>
						<button type="submit" class="btn btn-primary btn-sm" disabled={editSaving}>
							{editSaving ? 'Saving…' : 'Save changes'}
						</button>
					</div>
				</form>
			</div>
		{:else}
			<div class="flex items-start justify-between gap-3 mb-2 flex-wrap">
				<div class="min-w-0">
					<h1 class="text-2xl font-bold text-base-content break-words">{org.name}</h1>
					<span class="text-base-content/50 text-sm">/{org.slug}</span>
				</div>
				{#if isAdmin}
					<div class="flex gap-2">
						<button type="button" class="btn btn-outline btn-sm" onclick={startEdit}>Edit</button>
						{#if isOwner}
							<button type="button" class="btn btn-outline btn-error btn-sm" onclick={deleteOrg} disabled={deleting}>
								{deleting ? 'Deleting…' : 'Delete'}
							</button>
						{/if}
					</div>
				{/if}
			</div>

			{#if !isAdmin && org.memberTimeEntryVisibility}
				<div class="alert alert-warning mb-4 text-sm">
					<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
					<span>The admins of this organization can see your tracked working hours.</span>
				</div>
			{/if}

			{#if settingsNotificationIds.length > 0}
				<div class="alert alert-info mb-4 text-sm">
					<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
					<span>The organization rules have changed. See <a href="/organizations/{orgSlug}/settings" class="link link-primary font-semibold" onclick={dismissSettingsNotifications}>Settings</a> for details.</span>
					<button type="button" class="btn btn-ghost btn-xs btn-circle ml-auto" aria-label="Dismiss" title="Dismiss" onclick={dismissSettingsNotifications}>✕</button>
				</div>
			{/if}

			{#if org.description}
				<p class="text-base-content/70 my-3">{org.description}</p>
			{/if}
			{#if org.website}
				<a href={org.website} target="_blank" rel="noopener noreferrer" class="link link-primary text-sm inline-block mb-4 break-all">{org.website}</a>
			{/if}

			<p class="text-base-content/60 text-sm mt-2 mb-5">Your personal work schedule, overtime balance, vacation and absences in this organization.</p>

			{#if myMember}
				<div class="flex flex-col gap-4">
					<MemberSchedule schedule={workSchedule} error={scheduleError} />
					{#if workSchedule}
						<InitialOvertimeEditor
							{orgSlug}
							hours={workSchedule.initialOvertimeHours ?? 0}
							editable={workSchedule.initialOvertimeMode === 'Allowed'}
							mode={workSchedule.initialOvertimeMode}
							onsaved={loadSchedule}
						/>
					{/if}
					<MemberVacation {orgSlug} member={myMember} canEdit={isAdmin} onchanged={reloadOrg} />
					<MemberAbsences
						{orgSlug}
						userId={myMember.id!}
						title="My absences"
						canAdd={canAddAbsence}
						canDelete={isAdmin || absenceMode === 'Allowed'}
						{isAdmin}
						mustRequest={absenceMode === 'RequiresApproval'}
						{absenceTypes}
						onchanged={reloadOrg}
					/>
					{#if workSchedule}
						<SchedulePeriodEditor
							{orgSlug}
							editable={workSchedule.workScheduleChangeMode === 'Allowed'}
							mode={workSchedule.workScheduleChangeMode}
							onchanged={loadSchedule}
						/>
					{/if}
				</div>
			{/if}
		{/if}
	{/if}
</div>
