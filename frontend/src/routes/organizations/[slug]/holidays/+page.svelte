<script lang="ts">
	import Skeleton from '$lib/components/Skeleton.svelte';
	import { getOrgDetail, invalidateOrgDetail } from '$lib/stores/orgDetail';
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { holidayApi, groupApi } from '$lib/apiClient';
	import ExclusionsDialog from '$lib/components/ExclusionsDialog.svelte';
	import CompanyLeavesPanel from '$lib/components/CompanyLeavesPanel.svelte';
	import { t, tp } from '$lib/i18n';
	import HolidayRegionPanel from '$lib/components/HolidayRegionPanel.svelte';
	import { formatDate } from '$lib/utils/formatters';
	import { isOrgAdmin } from '$lib/utils/orgRules';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import type { OrganizationDetailResponse, HolidayResponse, GroupResponse } from '$lib/api';

	let org = $state<OrganizationDetailResponse | null>(null);
	let loading = $state(true);
	let error = $state('');

	let myRole = $derived(org?.members?.find((m) => m.id === auth.user?.id)?.role ?? null);
	let canEdit = $derived(isOrgAdmin(myRole));

	// Holidays
	let holidays = $state<HolidayResponse[]>([]);
	let holidaysError = $state('');

	// Add form
	let showAddHoliday = $state(false);
	let newHolidayName = $state('');
	let newHolidayDate = $state('');
	let newHolidayRecurring = $state(false);
	let newHolidayHalfDay = $state(false);
	let addingHoliday = $state(false);
	let addSubmitted = $state(false);
	let addError = $state('');

	// Inline edit
	let editingHolidayId = $state<number | null>(null);
	let editHolidayName = $state('');
	let editHolidayDate = $state('');
	let editHolidayRecurring = $state(false);
	let editHolidayHalfDay = $state(false);
	let editHolidaySaving = $state(false);
	let editSubmitted = $state(false);
	let editError = $state('');

	let deletingId = $state<number | null>(null);

	// Company holidays of a group (and its sub-groups); members an admin leaves out
	let groups = $state<GroupResponse[]>([]);
	let newHolidayGroup = $state('');
	let editHolidayGroup = $state('');
	let excluding = $state<HolidayResponse | null>(null);
	let savingExclusions = $state(false);
	let members = $derived((org?.members ?? []).map((m) => ({ id: m.id ?? 0, name: `${m.firstName ?? ''} ${m.lastName ?? ''}`.trim() }))
		.sort((a, b) => a.name.localeCompare(b.name)));

	function groupName(id: number | null | undefined): string | null {
		return id == null ? null : groups.find((g) => g.id === id)?.name ?? `#${id}`;
	}

	async function loadGroups() {
		try {
			groups = (await groupApi.apiV1OrganizationsSlugGroupsGet(orgSlug)).data;
		} catch {
			groups = [];
		}
	}

	async function saveExclusions(userIds: number[]) {
		if (!excluding?.id) return;
		savingExclusions = true;
		try {
			invalidateOrgDetail(orgSlug);
			await holidayApi.apiV1OrganizationsSlugHolidaysIdExclusionsPut(orgSlug, excluding.id, { userIds });
			toast.success(t('holidays.exclusions.saved'));
			excluding = null;
			await loadHolidays();
		} catch (err) {
			toast.error(err, t('holidays.exclusions.failed'));
		} finally {
			savingExclusions = false;
		}
	}

	const orgSlug = $derived(page.params.slug ?? '');
	let selectedYear = $state(new Date().getFullYear());

	let availableYears = $derived.by(() => {
		const years = new Set<number>([new Date().getFullYear()]);
		for (const h of holidays) {
			if (h.date && !h.isRecurring) {
				const y = parseInt(h.date.substring(0, 4));
				if (!isNaN(y)) years.add(y);
			}
		}
		return [...years].sort((a, b) => b - a);
	});

	let filteredHolidays = $derived(holidays.filter((h) => h.isRecurring || (h.date ?? '').startsWith(String(selectedYear))));

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
			loadHolidays();
			if (isOrgAdmin(data.members?.find((m) => m.id === auth.user?.id)?.role ?? null)) loadGroups();
		} catch (err) {
			error = getErrorStatus(err) === 404 ? t('holidays.notFound') : extractErrorMessage(err, t('holidays.loadOrgFailed'));
		} finally {
			loading = false;
		}
	}

	async function loadHolidays() {
		holidaysError = '';
		try {
			const { data } = await holidayApi.apiV1OrganizationsSlugHolidaysGet(orgSlug);
			holidays = [...data].sort((a, b) => (a.date ?? '').localeCompare(b.date ?? ''));
		} catch (err) {
			holidaysError = extractErrorMessage(err, t('holidays.loadFailed'));
		}
	}

	function toggleAddForm() {
		showAddHoliday = !showAddHoliday;
		addSubmitted = false;
		addError = '';
	}

	async function addHoliday(e: Event) {
		e.preventDefault();
		addSubmitted = true;
		if (!newHolidayName.trim() || !newHolidayDate) return;
		addingHoliday = true;
		addError = '';
		try {
			invalidateOrgDetail(orgSlug); // member/vacation figures change
			await holidayApi.apiV1OrganizationsSlugHolidaysPost(orgSlug, {
				date: newHolidayDate,
				name: newHolidayName.trim(),
				isRecurring: newHolidayRecurring,
				isHalfDay: newHolidayHalfDay,
				groupId: newHolidayGroup ? Number(newHolidayGroup) : null
			});
			toast.success(t('holidays.added'));
			showAddHoliday = false;
			newHolidayName = '';
			newHolidayDate = '';
			newHolidayRecurring = false;
			newHolidayHalfDay = false;
			newHolidayGroup = '';
			addSubmitted = false;
			await loadHolidays();
		} catch (err) {
			addError = extractErrorMessage(err, t('holidays.addFailed'));
		} finally {
			addingHoliday = false;
		}
	}

	function startEditHoliday(h: HolidayResponse) {
		editingHolidayId = h.id ?? null;
		editHolidayName = h.name ?? '';
		editHolidayDate = h.date ?? '';
		editHolidayRecurring = h.isRecurring ?? false;
		editHolidayHalfDay = h.isHalfDay ?? false;
		editHolidayGroup = h.groupId != null ? String(h.groupId) : '';
		editSubmitted = false;
		editError = '';
	}

	async function saveEditHoliday(e: Event, id: number) {
		e.preventDefault();
		editSubmitted = true;
		if (!editHolidayName.trim() || !editHolidayDate) return;
		editHolidaySaving = true;
		editError = '';
		try {
			invalidateOrgDetail(orgSlug); // member/vacation figures change
			await holidayApi.apiV1OrganizationsSlugHolidaysIdPut(orgSlug, id, {
				date: editHolidayDate,
				name: editHolidayName.trim(),
				isRecurring: editHolidayRecurring,
				isHalfDay: editHolidayHalfDay,
				groupId: editHolidayGroup ? Number(editHolidayGroup) : null,
				forWholeOrganization: !editHolidayGroup
			});
			toast.success(t('holidays.saved'));
			editingHolidayId = null;
			await loadHolidays();
		} catch (err) {
			editError = extractErrorMessage(err, t('holidays.saveFailed'));
		} finally {
			editHolidaySaving = false;
		}
	}

	async function deleteHoliday(h: HolidayResponse) {
		if (!h.id) return;
		const ok = await confirmDialog({
			title: t('holidays.deleteTitle'),
			message: `${h.name} (${formatHolidayDate(h)})`,
			confirmLabel: t('common.delete'),
			danger: true
		});
		if (!ok) return;
		deletingId = h.id;
		try {
			invalidateOrgDetail(orgSlug); // member/vacation figures change
			await holidayApi.apiV1OrganizationsSlugHolidaysIdDelete(orgSlug, h.id);
			toast.success(t('holidays.deleted'));
			await loadHolidays();
		} catch (err) {
			toast.error(err, t('holidays.deleteFailed'));
		} finally {
			deletingId = null;
		}
	}

	/** Recurring holidays show day and month only. */
	function formatHolidayDate(h: HolidayResponse): string {
		if (!h.date) return '';
		return h.isRecurring ? formatDate(h.date, { day: '2-digit', month: '2-digit' }) : formatDate(h.date);
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: org ? `${t('holidays.title')} – ${org.name}` : t('holidays.title') })}</title>
</svelte:head>

<div class="max-w-5xl mx-auto sm:px-6 pb-6">
	{#if loading}
		<Skeleton />
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if org}
		<div class="pt-4">
			<HolidayRegionPanel slug={orgSlug} region={org.holidayRegion} year={selectedYear} {canEdit}
				onchange={async () => { org = await getOrgDetail(orgSlug); await loadHolidays(); }} />
			<section>
				<div class="flex items-center justify-between gap-3 mb-4 flex-wrap">
					<div class="flex items-center gap-3">
						<h2 class="text-lg font-semibold text-base-content m-0">{t('holidays.company.title')}</h2>
						<select class="select select-sm w-auto" bind:value={selectedYear} aria-label={t('holidays.year')}>
							{#each availableYears as year (year)}
								<option value={year}>{year}</option>
							{/each}
						</select>
					</div>
					{#if canEdit}
						<div class="flex gap-2 flex-wrap">
							<button class="btn btn-primary btn-sm" onclick={toggleAddForm} aria-expanded={showAddHoliday}>
								{showAddHoliday ? t('common.cancel') : t('holidays.add')}
							</button>
						</div>
					{/if}
				</div>

				<p class="text-sm text-base-content/70 mt-0 mb-3">{t('holidays.company.intro')}</p>
				{#if holidaysError}
					<div class="alert alert-error mb-4 text-sm" role="alert">
						<span>{holidaysError}</span>
						<button class="btn btn-sm" onclick={loadHolidays}>{t('common.retry')}</button>
					</div>
				{/if}


				{#if showAddHoliday && canEdit}
					<div class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300">
						{#if addError}
							<div class="alert alert-error mb-3 text-sm" role="alert">{addError}</div>
						{/if}
						<form onsubmit={addHoliday} class="flex gap-3 items-end flex-wrap" novalidate>
							<div class="form-control w-full sm:w-auto">
								<label for="holidayName" class="label text-xs">{t('holidays.name')}</label>
								<input id="holidayName" type="text" class="input input-sm w-full" class:input-error={addSubmitted && !newHolidayName.trim()} bind:value={newHolidayName} required disabled={addingHoliday} placeholder={t('holidays.namePlaceholder')} />
								{#if addSubmitted && !newHolidayName.trim()}<span class="text-xs text-error mt-1">{t('common.required')}</span>{/if}
							</div>
							<div class="form-control w-full sm:w-auto">
								<label for="holidayDate" class="label text-xs">{t('holidays.date')}</label>
								<input id="holidayDate" type="date" class="input input-sm w-full" class:input-error={addSubmitted && !newHolidayDate} bind:value={newHolidayDate} required disabled={addingHoliday} />
								{#if addSubmitted && !newHolidayDate}<span class="text-xs text-error mt-1">{t('common.required')}</span>{/if}
							</div>
							<div class="form-control w-full sm:w-auto">
								<label for="holidayGroup" class="label text-xs">{t('holidays.company.scope')}</label>
								<select id="holidayGroup" class="select select-sm w-full sm:w-56" bind:value={newHolidayGroup} disabled={addingHoliday}>
									<option value="">{t('holidays.company.wholeOrg')}</option>
									{#each groups as g (g.id)}<option value={String(g.id)}>{g.name}</option>{/each}
								</select>
							</div>
							<label class="label cursor-pointer flex items-center gap-2 text-sm">
								<input type="checkbox" class="checkbox checkbox-sm" bind:checked={newHolidayRecurring} disabled={addingHoliday} />
								{t('holidays.recurringEveryYear')}
							</label>
							<label class="label cursor-pointer flex items-center gap-2 text-sm">
								<input type="checkbox" class="checkbox checkbox-sm" bind:checked={newHolidayHalfDay} disabled={addingHoliday} />
								{t('holidays.halfDay')}
							</label>
							<button type="submit" class="btn btn-primary btn-sm" disabled={addingHoliday}>
								{addingHoliday ? t('common.adding') : t('common.add')}
							</button>
						</form>
					</div>
				{/if}

				{#if filteredHolidays.length === 0}
					<p class="text-base-content/70 text-sm">{t('holidays.empty', { year: selectedYear })}{#if !canEdit} {t('holidays.managedByAdmins')}{/if}</p>
				{:else}
					<ul class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
						{#each filteredHolidays as h (h.id)}
							<li class="flex items-center justify-between gap-2 p-3 border-b border-base-200 last:border-b-0 flex-wrap">
								{#if editingHolidayId === h.id && canEdit}
									<form onsubmit={(e) => saveEditHoliday(e, h.id!)} class="flex gap-3 items-end flex-wrap w-full" novalidate>
										{#if editError}
											<div class="alert alert-error text-sm w-full" role="alert">{editError}</div>
										{/if}
										<div class="form-control w-full sm:w-auto">
											<label for="editHolidayName" class="label text-xs">{t('holidays.name')}</label>
											<input id="editHolidayName" type="text" class="input input-sm w-full" class:input-error={editSubmitted && !editHolidayName.trim()} bind:value={editHolidayName} required disabled={editHolidaySaving} />
											{#if editSubmitted && !editHolidayName.trim()}<span class="text-xs text-error mt-1">{t('common.required')}</span>{/if}
										</div>
										<div class="form-control w-full sm:w-auto">
											<label for="editHolidayDate" class="label text-xs">{t('holidays.date')}</label>
											<input id="editHolidayDate" type="date" class="input input-sm w-full" class:input-error={editSubmitted && !editHolidayDate} bind:value={editHolidayDate} required disabled={editHolidaySaving} />
											{#if editSubmitted && !editHolidayDate}<span class="text-xs text-error mt-1">{t('common.required')}</span>{/if}
										</div>
										<div class="form-control w-full sm:w-auto">
											<label for="editHolidayGroup" class="label text-xs">{t('holidays.company.scope')}</label>
											<select id="editHolidayGroup" class="select select-sm w-full sm:w-56" bind:value={editHolidayGroup} disabled={editHolidaySaving}>
												<option value="">{t('holidays.company.wholeOrg')}</option>
												{#each groups as g (g.id)}<option value={String(g.id)}>{g.name}</option>{/each}
											</select>
										</div>
										<label class="label cursor-pointer flex items-center gap-2 text-sm">
											<input type="checkbox" class="checkbox checkbox-sm" bind:checked={editHolidayRecurring} disabled={editHolidaySaving} />
											{t('holidays.recurringEveryYear')}
										</label>
										<label class="label cursor-pointer flex items-center gap-2 text-sm">
											<input type="checkbox" class="checkbox checkbox-sm" bind:checked={editHolidayHalfDay} disabled={editHolidaySaving} />
											{t('holidays.halfDay')}
										</label>
										<div class="flex gap-1.5">
											<button type="submit" class="btn btn-primary btn-sm" disabled={editHolidaySaving}>{editHolidaySaving ? t('common.saving') : t('common.save')}</button>
											<button type="button" class="btn btn-ghost btn-sm" onclick={() => (editingHolidayId = null)}>{t('common.cancel')}</button>
										</div>
									</form>
								{:else}
									<div class="flex items-center gap-x-3 gap-y-1 flex-wrap min-w-0">
										<span class="font-medium text-base-content">{h.name}</span>
										<span class="text-base-content/70 text-sm tabular-nums">{formatHolidayDate(h)}</span>
										{#if h.isRecurring}
											<span class="badge badge-sm badge-outline">{t('holidays.recurring')}</span>
										{/if}
										{#if h.isHalfDay}
											<span class="badge badge-sm badge-outline">{t('holidays.halfDayBadge')}</span>
										{/if}
										{#if h.groupId != null}
											<span class="badge badge-sm badge-outline">{groupName(h.groupId)}</span>
										{/if}
										{#if (h.excludedUserIds?.length ?? 0) > 0}
											<span class="badge badge-sm badge-outline">{tp('holidays.company.excluded', h.excludedUserIds!.length)}</span>
										{/if}
									</div>
									{#if canEdit}
										<div class="flex items-center gap-1.5 ml-auto">
											<button class="btn btn-ghost btn-sm" onclick={() => (excluding = h)}>{t('holidays.company.exclude')}</button>
											<button class="btn btn-ghost btn-sm" onclick={() => startEditHoliday(h)}>{t('common.edit')}</button>
											<button class="btn btn-ghost btn-xs text-error" aria-label={t('holidays.deleteLabel', { name: h.name ?? '' })} title={t('holidays.deleteLabel', { name: h.name ?? '' })} disabled={deletingId === h.id} onclick={() => deleteHoliday(h)}>&times;</button>
										</div>
									{/if}
								{/if}
							</li>
						{/each}
					</ul>
				{/if}
			</section>
			<CompanyLeavesPanel slug={orgSlug} {canEdit} {groups} {members} />
		</div>
		<ExclusionsDialog open={excluding !== null} name={excluding?.name ?? ''} {members} selected={excluding?.excludedUserIds ?? []}
			saving={savingExclusions} onclose={() => (excluding = null)} onsave={saveExclusions} />
	{/if}
</div>
