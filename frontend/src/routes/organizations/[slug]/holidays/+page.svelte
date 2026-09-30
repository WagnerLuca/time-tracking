<script lang="ts">
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { organizationsApi, holidayApi } from '$lib/apiClient';
	import { formatDate } from '$lib/utils/formatters';
	import { isOrgAdmin } from '$lib/utils/orgRules';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import type { OrganizationDetailResponse, HolidayResponse } from '$lib/api';

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
			const { data } = await organizationsApi.apiV1OrganizationsSlugGet(orgSlug);
			org = data;
			loadHolidays();
		} catch (err) {
			error = getErrorStatus(err) === 404 ? 'Organization not found.' : extractErrorMessage(err, 'Failed to load organization.');
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
			holidaysError = extractErrorMessage(err, 'Failed to load holidays.');
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
			await holidayApi.apiV1OrganizationsSlugHolidaysPost(orgSlug, {
				date: newHolidayDate,
				name: newHolidayName.trim(),
				isRecurring: newHolidayRecurring,
				isHalfDay: newHolidayHalfDay
			});
			toast.success('Holiday added.');
			showAddHoliday = false;
			newHolidayName = '';
			newHolidayDate = '';
			newHolidayRecurring = false;
			newHolidayHalfDay = false;
			addSubmitted = false;
			await loadHolidays();
		} catch (err) {
			addError = extractErrorMessage(err, 'Failed to add holiday.');
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
			await holidayApi.apiV1OrganizationsSlugHolidaysIdPut(orgSlug, id, {
				date: editHolidayDate,
				name: editHolidayName.trim(),
				isRecurring: editHolidayRecurring,
				isHalfDay: editHolidayHalfDay
			});
			toast.success('Holiday saved.');
			editingHolidayId = null;
			await loadHolidays();
		} catch (err) {
			editError = extractErrorMessage(err, 'Failed to update holiday.');
		} finally {
			editHolidaySaving = false;
		}
	}

	async function deleteHoliday(h: HolidayResponse) {
		if (!h.id) return;
		const ok = await confirmDialog({
			title: 'Delete holiday?',
			message: `${h.name} (${formatHolidayDate(h)})`,
			confirmLabel: 'Delete',
			danger: true
		});
		if (!ok) return;
		deletingId = h.id;
		try {
			await holidayApi.apiV1OrganizationsSlugHolidaysIdDelete(orgSlug, h.id);
			toast.success('Holiday deleted.');
			await loadHolidays();
		} catch (err) {
			toast.error(err, 'Failed to delete holiday.');
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
	<title>Holidays - {org?.name ?? 'Organization'} - Time Tracking</title>
</svelte:head>

<div class="max-w-5xl mx-auto sm:px-6 pb-6">
	{#if loading}
		<div class="flex items-center gap-3 justify-center py-12 text-base-content/50"><span class="loading loading-spinner loading-sm"></span><span>Loading…</span></div>
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if org}
		<div class="pt-4">
			<section>
				<div class="flex items-center justify-between gap-3 mb-4 flex-wrap">
					<div class="flex items-center gap-3">
						<h2 class="text-xl font-bold text-base-content m-0">Holidays</h2>
						<select class="select select-bordered select-sm w-auto" bind:value={selectedYear} aria-label="Year">
							{#each availableYears as year (year)}
								<option value={year}>{year}</option>
							{/each}
						</select>
					</div>
					{#if canEdit}
						<button class="btn btn-primary btn-sm" onclick={toggleAddForm} aria-expanded={showAddHoliday}>
							{showAddHoliday ? 'Cancel' : '+ Add holiday'}
						</button>
					{/if}
				</div>

				{#if holidaysError}
					<div class="alert alert-error mb-4 text-sm" role="alert">
						<span>{holidaysError}</span>
						<button class="btn btn-sm" onclick={loadHolidays}>Try again</button>
					</div>
				{/if}

				{#if showAddHoliday && canEdit}
					<div class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300">
						{#if addError}
							<div class="alert alert-error mb-3 text-sm" role="alert">{addError}</div>
						{/if}
						<form onsubmit={addHoliday} class="flex gap-3 items-end flex-wrap" novalidate>
							<div class="form-control w-full sm:w-auto">
								<label for="holidayName" class="label text-xs">Name</label>
								<input id="holidayName" type="text" class="input input-bordered input-sm w-full" class:input-error={addSubmitted && !newHolidayName.trim()} bind:value={newHolidayName} required disabled={addingHoliday} placeholder="e.g. Christmas" />
								{#if addSubmitted && !newHolidayName.trim()}<span class="text-xs text-error mt-1">Required</span>{/if}
							</div>
							<div class="form-control w-full sm:w-auto">
								<label for="holidayDate" class="label text-xs">Date</label>
								<input id="holidayDate" type="date" class="input input-bordered input-sm w-full" class:input-error={addSubmitted && !newHolidayDate} bind:value={newHolidayDate} required disabled={addingHoliday} />
								{#if addSubmitted && !newHolidayDate}<span class="text-xs text-error mt-1">Required</span>{/if}
							</div>
							<label class="label cursor-pointer flex items-center gap-2 text-sm">
								<input type="checkbox" class="checkbox checkbox-sm" bind:checked={newHolidayRecurring} disabled={addingHoliday} />
								Recurring every year
							</label>
							<label class="label cursor-pointer flex items-center gap-2 text-sm">
								<input type="checkbox" class="checkbox checkbox-sm" bind:checked={newHolidayHalfDay} disabled={addingHoliday} />
								Half day
							</label>
							<button type="submit" class="btn btn-primary btn-sm" disabled={addingHoliday}>
								{addingHoliday ? 'Adding…' : 'Add'}
							</button>
						</form>
					</div>
				{/if}

				{#if filteredHolidays.length === 0}
					<p class="text-base-content/50 text-sm">No holidays for {selectedYear}.{#if !canEdit} Holidays are managed by organization admins.{/if}</p>
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
											<label for="editHolidayName" class="label text-xs">Name</label>
											<input id="editHolidayName" type="text" class="input input-bordered input-sm w-full" class:input-error={editSubmitted && !editHolidayName.trim()} bind:value={editHolidayName} required disabled={editHolidaySaving} />
											{#if editSubmitted && !editHolidayName.trim()}<span class="text-xs text-error mt-1">Required</span>{/if}
										</div>
										<div class="form-control w-full sm:w-auto">
											<label for="editHolidayDate" class="label text-xs">Date</label>
											<input id="editHolidayDate" type="date" class="input input-bordered input-sm w-full" class:input-error={editSubmitted && !editHolidayDate} bind:value={editHolidayDate} required disabled={editHolidaySaving} />
											{#if editSubmitted && !editHolidayDate}<span class="text-xs text-error mt-1">Required</span>{/if}
										</div>
										<label class="label cursor-pointer flex items-center gap-2 text-sm">
											<input type="checkbox" class="checkbox checkbox-sm" bind:checked={editHolidayRecurring} disabled={editHolidaySaving} />
											Recurring every year
										</label>
										<label class="label cursor-pointer flex items-center gap-2 text-sm">
											<input type="checkbox" class="checkbox checkbox-sm" bind:checked={editHolidayHalfDay} disabled={editHolidaySaving} />
											Half day
										</label>
										<div class="flex gap-1.5">
											<button type="submit" class="btn btn-primary btn-sm" disabled={editHolidaySaving}>{editHolidaySaving ? 'Saving…' : 'Save'}</button>
											<button type="button" class="btn btn-ghost btn-sm" onclick={() => (editingHolidayId = null)}>Cancel</button>
										</div>
									</form>
								{:else}
									<div class="flex items-center gap-x-3 gap-y-1 flex-wrap min-w-0">
										<span class="font-medium text-base-content">{h.name}</span>
										<span class="text-base-content/60 text-sm tabular-nums">{formatHolidayDate(h)}</span>
										{#if h.isRecurring}
											<span class="badge badge-sm badge-outline">Recurring</span>
										{/if}
										{#if h.isHalfDay}
											<span class="badge badge-sm badge-outline">½ day</span>
										{/if}
									</div>
									{#if canEdit}
										<div class="flex items-center gap-1.5 ml-auto">
											<button class="btn btn-ghost btn-sm" onclick={() => startEditHoliday(h)}>Edit</button>
											<button class="btn btn-ghost btn-xs text-error" aria-label="Delete holiday {h.name}" title="Delete holiday" disabled={deletingId === h.id} onclick={() => deleteHoliday(h)}>&times;</button>
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
