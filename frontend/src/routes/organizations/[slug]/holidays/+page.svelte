<script lang="ts">
	import Skeleton from '$lib/components/Skeleton.svelte';
	import { getOrgDetail, invalidateOrgDetail } from '$lib/stores/orgDetail';
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { holidayApi } from '$lib/apiClient';
	import { t, tp } from '$lib/i18n';
	import { formatDate, holidayRegionLabel } from '$lib/utils/formatters';
	import { isOrgAdmin } from '$lib/utils/orgRules';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import type { OrganizationDetailResponse, HolidayResponse, HolidayPresetResponse } from '$lib/api';

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

	// Import public holidays from a preset (Germany nationwide or a state, Austria, Switzerland)
	const MAX_IMPORT_YEARS = 10;
	let showImport = $state(false);
	let presets = $state<HolidayPresetResponse[]>([]);
	let importPreset = $state('DE');
	let importFromYear = $state(new Date().getFullYear());
	let importToYear = $state(new Date().getFullYear());
	let importing = $state(false);
	let importError = $state('');
	let importRangeError = $derived(
		!Number.isInteger(importFromYear) || !Number.isInteger(importToYear) || importFromYear < 2000 || importToYear > 2100 || importToYear < importFromYear
			? t('holidays.import.rangeInvalid')
			: importToYear - importFromYear >= MAX_IMPORT_YEARS
				? t('holidays.import.rangeTooLong', { max: MAX_IMPORT_YEARS })
				: ''
	);

	async function toggleImport() {
		showImport = !showImport;
		importError = '';
		if (showImport && presets.length === 0) {
			try {
				const { data } = await holidayApi.apiV1OrganizationsHolidayPresetsGet();
				presets = data;
			} catch (err) {
				importError = extractErrorMessage(err, t('holidays.import.presetsFailed'));
			}
		}
	}

	async function importHolidays(e: Event) {
		e.preventDefault();
		if (importRangeError || !importPreset) return;
		importing = true;
		importError = '';
		try {
			const { data } = await holidayApi.apiV1OrganizationsSlugHolidaysImportPresetPost(orgSlug, importPreset, importFromYear, importToYear);
			invalidateOrgDetail(orgSlug);
			toast.success(tp('holidays.import.done', data.length));
			showImport = false;
			selectedYear = importFromYear;
			await loadHolidays();
		} catch (err) {
			importError = extractErrorMessage(err, t('holidays.import.failed'));
		} finally {
			importing = false;
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
				isHalfDay: newHolidayHalfDay
			});
			toast.success(t('holidays.added'));
			showAddHoliday = false;
			newHolidayName = '';
			newHolidayDate = '';
			newHolidayRecurring = false;
			newHolidayHalfDay = false;
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
				isHalfDay: editHolidayHalfDay
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
			<section>
				<div class="flex items-center justify-between gap-3 mb-4 flex-wrap">
					<div class="flex items-center gap-3">
						<h2 class="text-xl font-bold text-base-content m-0">{t('holidays.title')}</h2>
						<select class="select select-sm w-auto" bind:value={selectedYear} aria-label={t('holidays.year')}>
							{#each availableYears as year (year)}
								<option value={year}>{year}</option>
							{/each}
						</select>
					</div>
					{#if canEdit}
						<div class="flex gap-2 flex-wrap">
							<button class="btn btn-outline btn-sm" onclick={toggleImport} aria-expanded={showImport}>
								{showImport ? t('common.cancel') : t('holidays.import.open')}
							</button>
							<button class="btn btn-primary btn-sm" onclick={toggleAddForm} aria-expanded={showAddHoliday}>
								{showAddHoliday ? t('common.cancel') : t('holidays.add')}
							</button>
						</div>
					{/if}
				</div>

				{#if holidaysError}
					<div class="alert alert-error mb-4 text-sm" role="alert">
						<span>{holidaysError}</span>
						<button class="btn btn-sm" onclick={loadHolidays}>{t('common.retry')}</button>
					</div>
				{/if}

				{#if showImport && canEdit}
					<div class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300">
						<p class="text-sm text-base-content/70 mb-3">{t('holidays.import.intro')}</p>
						{#if importError}
							<div class="alert alert-error mb-3 text-sm" role="alert">{importError}</div>
						{/if}
						<form onsubmit={importHolidays} class="flex gap-3 items-end flex-wrap" novalidate>
							<div class="form-control w-full sm:w-auto">
								<label for="holidayPreset" class="label text-xs">{t('holidays.import.region')}</label>
								<select id="holidayPreset" class="select select-sm w-full sm:w-64" bind:value={importPreset} disabled={importing || presets.length === 0}>
									{#each presets as p (p.key)}
										<option value={p.key}>{holidayRegionLabel(p.key, p.name)}</option>
									{/each}
								</select>
							</div>
							<div class="form-control">
								<label for="importFrom" class="label text-xs">{t('holidays.import.fromYear')}</label>
								<input id="importFrom" type="number" min="2000" max="2100" class="input input-sm w-24" class:input-error={importRangeError} bind:value={importFromYear} disabled={importing} aria-describedby={importRangeError ? 'importRangeError' : undefined} />
							</div>
							<div class="form-control">
								<label for="importTo" class="label text-xs">{t('holidays.import.toYear')}</label>
								<input id="importTo" type="number" min="2000" max="2100" class="input input-sm w-24" class:input-error={importRangeError} bind:value={importToYear} disabled={importing} aria-describedby={importRangeError ? 'importRangeError' : undefined} />
							</div>
							<button type="submit" class="btn btn-primary btn-sm" disabled={importing || !!importRangeError || presets.length === 0}>
								{importing ? t('holidays.import.importing') : t('holidays.import.submit')}
							</button>
						</form>
						{#if importRangeError}<p id="importRangeError" class="text-xs text-error mt-2 m-0">{importRangeError}</p>{/if}
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
									</div>
									{#if canEdit}
										<div class="flex items-center gap-1.5 ml-auto">
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
		</div>
	{/if}
</div>
