<script lang="ts">
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { organizationsApi, workScheduleApi, absenceDayApi, groupApi } from '$lib/apiClient';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import { isOrgAdmin } from '$lib/utils/orgRules';
	import MemberCalendar from '$lib/components/MemberCalendar.svelte';
	import SchedulePeriodEditor from '$lib/components/SchedulePeriodEditor.svelte';
	import InitialOvertimeEditor from '$lib/components/InitialOvertimeEditor.svelte';
	import MemberHeader from '$lib/components/member/MemberHeader.svelte';
	import MemberSummary from '$lib/components/member/MemberSummary.svelte';
	import MemberReports from '$lib/components/member/MemberReports.svelte';
	import MemberAbsences from '$lib/components/member/MemberAbsences.svelte';
	import MemberPermissions from '$lib/components/member/MemberPermissions.svelte';
	import MemberSchedule from '$lib/components/member/MemberSchedule.svelte';
	import MemberVacation from '$lib/components/member/MemberVacation.svelte';
	import MemberBadge from '$lib/components/member/MemberBadge.svelte';
	import type {
		OrganizationDetailResponse,
		OrganizationMemberResponse,
		MemberTimeOverviewResponse,
		WorkScheduleResponse,
		AbsenceTypeDefinitionResponse,
		EffectivePermissionsResponse
	} from '$lib/api';

	const orgSlug = $derived(page.params.slug ?? '');
	const memberId = $derived(parseInt(page.params.memberId ?? '0', 10));

	let org = $state<OrganizationDetailResponse | null>(null);
	let member = $state<OrganizationMemberResponse | null>(null);
	let loading = $state(true);
	let error = $state('');

	let overview = $state<MemberTimeOverviewResponse | null>(null);
	let overviewLoading = $state(false);
	let firstEntryDate = $state<Date | null>(null);
	let absenceTypes = $state<AbsenceTypeDefinitionResponse[]>([]);
	let memberPerms = $state<EffectivePermissionsResponse | null>(null);
	let schedule = $state<WorkScheduleResponse | null>(null);
	let scheduleLoading = $state(false);
	let scheduleError = $state('');

	let myRole = $derived(org?.members?.find((m) => m.id === auth.user?.id)?.role ?? null);
	let isAdmin = $derived(isOrgAdmin(myRole));
	let isSelf = $derived(auth.user?.id === memberId);
	let memberName = $derived(member ? `${member.firstName ?? ''} ${member.lastName ?? ''}`.trim() : '');

	// Self, admin with visibility enabled, or the overview endpoint returned data
	let canViewTime = $derived(isSelf || (isAdmin && (org?.memberTimeEntryVisibility ?? false)) || overview !== null);

	// Group admins sharing a group with the member may export too (backend is the source of truth)
	let callerAdminGroupIds = $derived(new Set((org?.members?.find((m) => m.id === auth.user?.id)?.groups ?? []).filter((g) => g.isGroupAdmin).map((g) => g.groupId)));
	let canGenerateReport = $derived(isSelf || isAdmin || (member?.groups ?? []).some((g) => callerAdminGroupIds.has(g.groupId)));

	$effect(() => {
		if (!orgSlug || !memberId) return;
		untrack(() => loadAll());
	});

	/** 403 just means "not visible to you" → no toast; anything else is reported. */
	function reportUnlessForbidden(err: unknown, fallback: string) {
		if (getErrorStatus(err) !== 403) toast.error(err, fallback);
	}

	async function loadAll() {
		loading = true;
		error = '';
		overview = null;
		firstEntryDate = null;
		memberPerms = null;
		try {
			await loadOrg();
			if (!member) error = 'Member not found.';
		} catch (err) {
			error = extractErrorMessage(err, 'Could not load the organization.');
		} finally {
			loading = false;
		}
		if (!member) return;
		loadOverview();
		loadAbsenceTypes();
		loadMemberPerms();
		loadSchedule();
	}

	async function loadOrg() {
		const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
		org = data;
		member = data.members?.find((m) => m.id === memberId) ?? null;
	}

	/** Refresh org + member after a change without the page-level spinner. */
	async function reloadOrg() {
		try {
			await loadOrg();
		} catch (err) {
			toast.error(err, 'Could not reload the member.');
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

	async function loadMemberPerms() {
		try {
			const { data } = await groupApi.apiV1OrganizationsSlugMembersMemberIdEffectivePermissionsGet(orgSlug, memberId);
			memberPerms = data;
		} catch (err) {
			reportUnlessForbidden(err, 'Could not load the permissions.');
		}
	}

	async function loadOverview() {
		overviewLoading = true;
		// TODO(backend): replace both all-time fetches with the GET cumulative-overtime balance endpoint once available.
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugTimeOverviewGet(orgSlug, '2000-01-01T00:00:00.000Z', '2099-12-31T23:59:59.999Z');
			overview = data.find((m) => m.userId === memberId) ?? null;
		} catch (err) {
			reportUnlessForbidden(err, 'Could not load the time overview.');
		}
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugMemberEntriesMemberIdGet(orgSlug, memberId, '2000-01-01T00:00:00.000Z', '2099-12-31T23:59:59.999Z');
			const starts = (data.items ?? []).filter((e) => e.startTime).map((e) => new Date(e.startTime!).getTime());
			firstEntryDate = starts.length > 0 ? new Date(Math.min(...starts)) : null;
		} catch (err) {
			reportUnlessForbidden(err, 'Could not load the time entries.');
		}
		overviewLoading = false;
	}

	async function loadSchedule() {
		scheduleLoading = true;
		scheduleError = '';
		try {
			const { data } = await workScheduleApi.apiV1OrganizationsSlugMembersMemberIdWorkScheduleGet(orgSlug, memberId);
			schedule = data;
		} catch (err) {
			schedule = null;
			if (getErrorStatus(err) !== 404) scheduleError = extractErrorMessage(err, 'Could not load the schedule.');
		} finally {
			scheduleLoading = false;
		}
	}
</script>

<svelte:head>
	<title>{memberName || 'Member'} – {org?.name ?? 'Organization'}</title>
</svelte:head>

<div class="max-w-3xl mx-auto py-6 px-4 sm:px-6">
	<a href="/organizations/{orgSlug}/members" class="inline-flex items-center gap-1.5 text-base-content/60 no-underline text-sm mb-6 transition-colors hover:text-primary">
		<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M19 12H5"/><path d="M12 19l-7-7 7-7"/></svg>
		Back to members
	</a>

	{#if loading}
		<div class="text-center py-12">
			<span class="loading loading-spinner loading-md"></span>
			<p class="text-base-content/50 text-sm">Loading…</p>
		</div>
	{:else if error}
		<div class="alert alert-error mb-4 text-sm">{error}</div>
	{:else if member}
		<MemberHeader {orgSlug} {member} {isSelf} canManage={isAdmin && !isSelf} callerIsOwner={myRole === 'Owner'} onchanged={reloadOrg} />

		{#if canViewTime}
			<section class="card bg-base-100 border border-base-300 p-5 mb-5">
				<h2 class="text-base font-bold text-base-content m-0 mb-4">Calendar</h2>
				{#key `${orgSlug}-${memberId}`}
					<MemberCalendar {orgSlug} orgId={org?.id} userId={memberId} {isSelf} orgDetail={org} />
				{/key}
			</section>

			<MemberSummary {member} {overview} initialOvertimeHours={schedule?.initialOvertimeHours ?? 0} {firstEntryDate} loading={overviewLoading} />
		{/if}

		{#if canGenerateReport}
			<MemberReports {orgSlug} {memberId} subject={isSelf ? 'you' : memberName} />
		{/if}

		{#if isSelf || isAdmin}
			<MemberAbsences
				class="mb-5"
				{orgSlug}
				userId={memberId}
				title="Absences"
				canAdd={isAdmin}
				canDelete={isAdmin}
				{isAdmin}
				{absenceTypes}
				showAudit
				onchanged={reloadOrg}
			/>
		{/if}

		{#if (isAdmin || isSelf) && memberPerms}
			<MemberPermissions {orgSlug} perms={memberPerms} {isSelf} canManage={isAdmin} />
		{/if}

		<MemberSchedule class="mb-5" {schedule} loading={scheduleLoading} error={scheduleError} />
		<InitialOvertimeEditor class="mb-5" {orgSlug} {memberId} hours={schedule?.initialOvertimeHours ?? 0} editable={isAdmin} onsaved={loadSchedule} />
		<MemberVacation class="mb-5" {orgSlug} {member} canEdit={isAdmin} onchanged={reloadOrg} />
		{#if isAdmin}
			<MemberBadge {orgSlug} {member} onchanged={reloadOrg} />
		{/if}
		<SchedulePeriodEditor class="mb-5" {orgSlug} {memberId} editable={isAdmin} onchanged={loadSchedule} />
	{/if}
</div>
