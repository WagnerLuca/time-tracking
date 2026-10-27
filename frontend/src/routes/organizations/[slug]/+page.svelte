<script lang="ts">
	import Skeleton from '$lib/components/Skeleton.svelte';
	import { getOrgDetail } from '$lib/stores/orgDetail';
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';
	import { workScheduleApi, absenceDayApi, notificationsApi, groupApi } from '$lib/apiClient';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import { isOrgAdmin, ruleMode } from '$lib/utils/orgRules';
	import WorkSchedulePlan from '$lib/components/WorkSchedulePlan.svelte';
	import InitialOvertimeEditor from '$lib/components/InitialOvertimeEditor.svelte';
	import MemberVacation from '$lib/components/member/MemberVacation.svelte';
	import MemberAbsences from '$lib/components/member/MemberAbsences.svelte';
	import type {
		OrganizationDetailResponse,
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
	let absenceMode = $derived(ruleMode('absenceSelfServiceMode', perms, org));
	let canAddAbsence = $derived(isAdmin || (absenceMode !== null && absenceMode !== 'Disabled'));

	// Unread "organization rules changed" notifications
	let settingsNotificationIds = $state<number[]>([]);

	$effect(() => {
		if (!orgSlug) return;
		untrack(() => loadOrg());
	});

	async function loadOrg() {
		loading = true;
		error = '';
		perms = null;
		settingsNotificationIds = [];
		try {
			const data = await getOrgDetail(orgSlug);
			org = data;
		} catch (err) {
			error = getErrorStatus(err) === 404 ? t('orgHome.notFound') : extractErrorMessage(err, t('orgHome.loadFailed'));
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
			const data = await getOrgDetail(orgSlug, { fresh: true });
			org = data;
		} catch (err) {
			toast.error(err, t('orgHome.reloadFailed'));
		}
	}

	async function loadSchedule() {
		scheduleError = '';
		try {
			const { data } = await workScheduleApi.apiV1OrganizationsSlugWorkScheduleGet(orgSlug);
			workSchedule = data;
		} catch (err) {
			workSchedule = null;
			if (getErrorStatus(err) !== 404) scheduleError = extractErrorMessage(err, t('orgHome.scheduleFailed'));
		}
	}

	async function loadPerms() {
		try {
			const { data } = await groupApi.apiV1OrganizationsSlugEffectivePermissionsGet(orgSlug);
			perms = data;
		} catch (err) {
			// The org defaults still apply (ruleMode falls back to org)
			toast.error(err, t('orgHome.permissionsFailed'));
		}
	}

	async function loadAbsenceTypes() {
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsenceTypesGet(orgSlug);
			absenceTypes = data;
		} catch (err) {
			absenceTypes = [];
			toast.error(err, t('orgHome.absenceTypesFailed'));
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
		if (failed) toast.error((failed as PromiseRejectedResult).reason, t('orgHome.markReadFailed'));
	}

</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: org?.name ?? t('orgHome.fallbackName') })}</title>
</svelte:head>

<div class="max-w-5xl mx-auto px-4 sm:px-6 pb-6">
	{#if loading}
		<Skeleton />
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if org}
			<div class="flex items-start justify-between gap-3 mb-2 flex-wrap">
				<div class="min-w-0">
					<h1 class="text-2xl font-bold text-base-content break-words">{org.name}</h1>
					<span class="text-base-content/70 text-sm">/{org.slug}</span>
				</div>
			</div>

			{#if !isAdmin && org.memberTimeEntryVisibility}
				<div class="alert alert-warning mb-4 text-sm">
					<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
					<span>{t('orgHome.visibilityWarning')}</span>
				</div>
			{/if}

			{#if settingsNotificationIds.length > 0}
				<div class="alert alert-info mb-4 text-sm">
					<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
					<span>{t('orgHome.rulesChangedBefore')} <a href="/organizations/{orgSlug}/settings" class="link font-semibold" onclick={dismissSettingsNotifications}>{t('orgHome.rulesChangedLink')}</a>{t('orgHome.rulesChangedAfter')}</span>
					<button type="button" class="btn btn-ghost btn-xs btn-circle ml-auto" aria-label={t('common.dismiss')} title={t('common.dismiss')} onclick={dismissSettingsNotifications}>✕</button>
				</div>
			{/if}

			{#if org.description}
				<p class="text-base-content/70 my-3">{org.description}</p>
			{/if}
			<!-- Only http(s) becomes a link: a javascript:/data: URL must never be clickable -->
			{#if org.website && /^https?:\/\//i.test(org.website)}
				<a href={org.website} target="_blank" rel="noopener noreferrer" class="link link-primary text-sm inline-block mb-4 break-all">{org.website}</a>
			{/if}

			<p class="text-base-content/70 text-sm mt-2 mb-5">{t('orgHome.intro')}</p>

			{#if myMember}
				<div class="flex flex-col gap-4">
					{#if scheduleError}<div class="alert alert-error text-sm">{scheduleError}</div>{/if}
					<WorkSchedulePlan {orgSlug} mode={workSchedule?.workScheduleChangeMode} onchanged={loadSchedule} />
					{#if workSchedule}
						<InitialOvertimeEditor
							{orgSlug}
							hours={workSchedule.initialOvertimeHours ?? 0}
							editable={workSchedule.initialOvertimeMode === 'Allowed'}
							mode={workSchedule.initialOvertimeMode}
							onsaved={loadSchedule}
						/>
					{/if}
					<MemberVacation {orgSlug} timeZone={org?.timeZone} member={myMember} canEdit={isAdmin} onchanged={reloadOrg} />
					<MemberAbsences
						{orgSlug}
						userId={myMember.id!}
						title={t('orgHome.myAbsences')}
						canAdd={canAddAbsence}
						canDelete={isAdmin || absenceMode === 'Allowed'}
						{isAdmin}
						mustRequest={absenceMode === 'RequiresApproval'}
						{absenceTypes}
						onchanged={reloadOrg}
					/>
				</div>
			{/if}
	{/if}
</div>
