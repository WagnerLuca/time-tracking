<script lang="ts">
	import Skeleton from '$lib/components/Skeleton.svelte';
	import { getOrgDetail } from '$lib/stores/orgDetail';
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';
	import { organizationsApi, workScheduleApi, absenceDayApi, groupApi } from '$lib/apiClient';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import { isOrgAdmin } from '$lib/utils/orgRules';
	import { canSee } from '$lib/utils/visibility';
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
	import MemberCards from '$lib/components/member/MemberCards.svelte';
	import type {
		OrganizationDetailResponse,
		OrganizationMemberResponse,
		MemberTimeOverviewResponse,
		WorkScheduleResponse,
		AbsenceTypeDefinitionResponse,
		EffectivePermissionsResponse
	} from '$lib/api';
	import type { BalanceMinutes } from '$lib/utils/balance';

	const orgSlug = $derived(page.params.slug ?? '');
	const memberId = $derived(parseInt(page.params.memberId ?? '0', 10));

	let org = $state<OrganizationDetailResponse | null>(null);
	let calendar = $state<ReturnType<typeof MemberCalendar> | undefined>();
	let member = $state<OrganizationMemberResponse | null>(null);
	let loading = $state(true);
	let error = $state('');

	let overview = $state<MemberTimeOverviewResponse | null>(null);
	let overviewLoading = $state(false);
	let balance = $state<BalanceMinutes | null>(null);
	let absenceTypes = $state<AbsenceTypeDefinitionResponse[]>([]);
	let memberPerms = $state<EffectivePermissionsResponse | null>(null);
	let schedule = $state<WorkScheduleResponse | null>(null);
	let scheduleLoading = $state(false);
	let scheduleError = $state('');

	let myRole = $derived(org?.members?.find((m) => m.id === auth.user?.id)?.role ?? null);
	let isAdmin = $derived(isOrgAdmin(myRole));
	let isSelf = $derived(auth.user?.id === memberId);
	let memberName = $derived(member ? `${member.firstName ?? ''} ${member.lastName ?? ''}`.trim() : '');

	// What the backend lets the caller see about this member (self and admins: everything, admins'
	// view of tracked times per the org setting; others per the visibility matrix and group admin rights)
	let canViewTime = $derived(canSee(member, 'TrackedTime'));
	let canViewSchedule = $derived(canSee(member, 'WorkSchedule'));
	let canViewAbsences = $derived(isAdmin || canSee(member, 'Vacation') || canSee(member, 'SickDays') || canSee(member, 'OtherAbsences'));

	let callerAdminGroupIds = $derived(new Set((org?.members?.find((m) => m.id === auth.user?.id)?.groups ?? []).filter((g) => g.isGroupAdmin).map((g) => g.groupId)));
	// Admin corrections of the member's entries: org admins, or group admins of a shared group for plain members.
	// ponytail: the group's "may approve time edits" flag is not checked here; the backend answers 403 without it.
	let canCorrect = $derived(!isSelf && (isAdmin || (member?.role === 'Member' && (member?.groups ?? []).some((g) => callerAdminGroupIds.has(g.groupId)))));

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
		balance = null;
		memberPerms = null;
		try {
			await loadOrg();
			if (!member) error = t('memberDetail.notFound');
		} catch (err) {
			error = extractErrorMessage(err, t('memberDetail.loadFailed'));
		} finally {
			loading = false;
		}
		if (!member) return;
		if (canViewTime) loadOverview();
		loadAbsenceTypes();
		loadMemberPerms();
		if (canViewSchedule) loadSchedule();
	}

	async function loadOrg(fresh = false) {
		const data = await getOrgDetail(orgSlug, { fresh });
		org = data;
		member = data.members?.find((m) => m.id === memberId) ?? null;
	}

	/** Refresh org + member after a change without the page-level spinner. */
	async function reloadOrg() {
		try {
			await loadOrg(true);
		} catch (err) {
			toast.error(err, t('memberDetail.reloadFailed'));
		}
	}

	async function loadAbsenceTypes() {
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesGet(orgSlug);
			absenceTypes = data;
		} catch (err) {
			absenceTypes = [];
			toast.error(err, t('memberDetail.absenceTypesFailed'));
		}
	}

	async function loadMemberPerms() {
		try {
			const { data } = await groupApi.apiV1OrganizationsSlugMembersMemberIdEffectivePermissionsGet(orgSlug, memberId);
			memberPerms = data;
		} catch (err) {
			reportUnlessForbidden(err, t('memberDetail.permissionsFailed'));
		}
	}

	async function loadOverview() {
		overviewLoading = true;
		try {
			const { data } = await organizationsApi.apiV1OrganizationsSlugTimeOverviewGet(orgSlug, '2000-01-01T00:00:00.000Z', '2099-12-31T23:59:59.999Z');
			overview = data.find((m) => m.userId === memberId) ?? null;
		} catch (err) {
			reportUnlessForbidden(err, t('memberDetail.overviewFailed'));
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
			if (getErrorStatus(err) !== 404) scheduleError = extractErrorMessage(err, t('memberDetail.scheduleFailed'));
		} finally {
			scheduleLoading = false;
		}
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: memberName || t('memberDetail.pageTitle') })}</title>
</svelte:head>

<div class="max-w-3xl mx-auto py-6 px-4 sm:px-6">
	<a href="/organizations/{orgSlug}/members" class="inline-flex items-center gap-1.5 text-base-content/60 no-underline text-sm mb-6 transition-colors hover:text-primary">
		<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M19 12H5"/><path d="M12 19l-7-7 7-7"/></svg>
		{t('memberDetail.back')}
	</a>

	{#if loading}
		<Skeleton />
	{:else if error}
		<div class="alert alert-error mb-4 text-sm">{error}</div>
	{:else if member}
		<MemberHeader {orgSlug} {member} {isSelf} canManage={isAdmin && !isSelf} callerIsOwner={myRole === 'Owner'} onchanged={reloadOrg} />

		{#if canViewTime}
			<section class="card bg-base-100 border border-base-300 p-5 mb-5">
				<h2 class="text-base font-bold text-base-content m-0 mb-4">{t('memberDetail.calendar')}</h2>
				{#key `${orgSlug}-${memberId}`}
					<MemberCalendar bind:this={calendar} {orgSlug} orgId={org?.id} userId={memberId} {isSelf} {canCorrect} orgDetail={org} onbalance={(b) => (balance = b)} />
				{/key}
			</section>

			<MemberSummary {member} {overview} initialOvertimeHours={schedule?.initialOvertimeHours ?? 0} {balance} loading={overviewLoading} />
		{/if}

		{#if canViewTime}
			<MemberReports {orgSlug} {memberId} subject={isSelf ? t('memberDetail.reports.subjectSelf') : memberName} />
		{/if}

		{#if canViewAbsences}
			<MemberAbsences
				class="mb-5"
				{orgSlug}
				userId={memberId}
				title={t('memberDetail.absences.title')}
				canAdd={isAdmin}
				canDelete={isAdmin}
				{isAdmin}
				{absenceTypes}
				showAudit={isSelf || isAdmin}
				onchanged={reloadOrg}
			/>
		{/if}

		{#if (isAdmin || isSelf) && memberPerms}
			<MemberPermissions {orgSlug} perms={memberPerms} {isSelf} canManage={isAdmin} />
		{/if}

		{#if canViewSchedule}
			<MemberSchedule class="mb-5" {schedule} loading={scheduleLoading} error={scheduleError} />
		{/if}
		{#if isSelf || isAdmin}
			<!-- The starting overtime balance is payroll data: the person and admins only -->
			<InitialOvertimeEditor class="mb-5" {orgSlug} {memberId} hours={schedule?.initialOvertimeHours ?? 0} editable={isAdmin} onsaved={async () => { await loadSchedule(); await calendar?.reloadBalance(); }} />
		{/if}
		{#if canSee(member, 'VacationBalance')}
			<MemberVacation class="mb-5" {orgSlug} {member} canEdit={isAdmin} onchanged={reloadOrg} />
		{/if}
		{#if isAdmin}
			<MemberCards {orgSlug} {memberId} onchanged={reloadOrg} />
		{/if}
		{#if canViewSchedule}
			<SchedulePeriodEditor class="mb-5" {orgSlug} {memberId} editable={isAdmin} onchanged={loadSchedule} />
		{/if}
	{/if}
</div>
