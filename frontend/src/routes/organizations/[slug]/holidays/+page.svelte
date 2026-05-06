<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/stores';
	import { auth } from '$lib/stores/auth.svelte';
	import { organizationsApi, holidayApi } from '$lib/apiClient';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import type {
		OrganizationDetailResponse,
		HolidayResponse
	} from '$lib/api';

	let org = $state<OrganizationDetailResponse | null>(null);
	let loading = $state(true);
	let error = $state('');

	let myRole = $derived(
		org?.members?.find((m) => m.id === auth.user?.id)?.role ?? null
	);
	let canEdit = $derived(myRole === 'Owner' || myRole === 'Admin');

	// Holidays
	let holidays = $state<HolidayResponse[]>([]);
	let showAddHoliday = $state(false);
	let newHolidayName = $state('');
	let newHolidayDate = $state('');
	let addingHoliday = $state(false);
	let holidayError = $state('');
	let editingHolidayId = $state<number | null>(null);
	let editHolidayName = $state('');
	let editHolidayDate = $state('');
	let editHolidaySaving = $state(false);
	let newHolidayRecurring = $state(false);
	let newHolidayHalfDay = $state(false);
	let editHolidayRecurring = $state(false);
	let editHolidayHalfDay = $state(false);

	let orgSlug = $state('');
	let selectedYear = $state(new Date().getFullYear());

	function getAvailableYears(): number[] {
		const years = new Set<number>();
		years.add(new Date().getFullYear());
		for (const h of holidays) {
			if (h.date && !h.isRecurring) {
				const y = parseInt(h.date.substring(0, 4));
				if (!isNaN(y)) years.add(y);
			}
		}
		return [...years].sort((a, b) => b - a);
	}

	function filteredHolidays(): HolidayResponse[] {
		return holidays.filter(h => {
			if (h.isRecurring) return true;
			if (!h.date) return false;
			return h.date.startsWith(String(selectedYear));
		});
	}

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
			loadHolidays();
		} catch (err) {
			if (getErrorStatus(err) === 404) { error = 'Organization not found.'; }
			else { error = 'Failed to load organization.'; }
		} finally {
			loading = false;
		}
	}

	async function loadHolidays() {
		try {
			const { data } = await holidayApi.apiV1OrganizationsSlugHolidaysGet(orgSlug);
			holidays = (data as HolidayResponse[]).sort((a, b) => (a.date ?? '').localeCompare(b.date ?? ''));
		} catch {
			holidays = [];
		}
	}

	async function addHoliday(e: Event) {
		e.preventDefault();
		addingHoliday = true;
		holidayError = '';
		try {
			await holidayApi.apiV1OrganizationsSlugHolidaysPost(orgSlug, {
				date: newHolidayDate,
				name: newHolidayName,
				isRecurring: newHolidayRecurring,
				isHalfDay: newHolidayHalfDay
			});
			await loadHolidays();
			showAddHoliday = false;
			newHolidayName = '';
			newHolidayDate = '';
			newHolidayRecurring = false;
			newHolidayHalfDay = false;
		} catch (err) {
			holidayError = extractErrorMessage(err, 'Failed to add holiday.');
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
	}

	async function saveEditHoliday(id: number) {
		editHolidaySaving = true;
		holidayError = '';
		try {
			await holidayApi.apiV1OrganizationsSlugHolidaysIdPut(orgSlug, id, {
				date: editHolidayDate,
				name: editHolidayName,
				isRecurring: editHolidayRecurring,
				isHalfDay: editHolidayHalfDay
			});
			editingHolidayId = null;
			await loadHolidays();
		} catch (err) {
			holidayError = extractErrorMessage(err, 'Failed to update holiday.');
		} finally {
			editHolidaySaving = false;
		}
	}

	async function deleteHoliday(id: number) {
		if (!confirm('Delete this holiday?')) return;
		try {
			await holidayApi.apiV1OrganizationsSlugHolidaysIdDelete(orgSlug, id);
			await loadHolidays();
		} catch (err) {
			holidayError = extractErrorMessage(err, 'Failed to delete holiday.');
		}
	}

	function formatDateDisplay(dateStr?: string): string {
		if (!dateStr) return '';
		try {
			const d = new Date(dateStr + 'T00:00:00');
			return d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });
		} catch { return dateStr; }
	}
</script>

<svelte:head>
	<title>Holidays - {org?.name ?? 'Organization'} - Time Tracking</title>
</svelte:head>

<div class="max-w-5xl mx-auto px-6 pb-6">
	{#if loading}
		<div class="flex items-center gap-3 justify-center py-12 text-base-content/40"><span class="loading loading-spinner loading-sm"></span><span>Loading...</span></div>
	{:else if error}
		<div class="alert alert-error">{error}</div>
	{:else if org}
		<div class="pt-4">
			<section>
				<div class="flex items-center justify-between mb-4">
					<div class="flex items-center gap-3">
						<h2 class="text-xl font-bold text-base-content">Holidays</h2>
						<select class="select select-bordered select-sm" bind:value={selectedYear}>
							{#each getAvailableYears() as year}
								<option value={year}>{year}</option>
							{/each}
						</select>
					</div>
					{#if canEdit}
						<button class="btn btn-primary btn-sm" onclick={() => (showAddHoliday = !showAddHoliday)}>
							{showAddHoliday ? 'Cancel' : '+ Add Holiday'}
						</button>
					{/if}
				</div>

				{#if holidayError}
					<div class="alert alert-error mb-4 text-sm">{holidayError}</div>
				{/if}

				{#if showAddHoliday && canEdit}
					<div class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300">
						<form onsubmit={addHoliday} class="flex gap-3 items-end flex-wrap">
							<div class="form-control">
								<label for="holidayName" class="label text-xs">Name</label>
								<input id="holidayName" type="text" class="input input-bordered input-sm" bind:value={newHolidayName} required disabled={addingHoliday} placeholder="e.g. Christmas" />
							</div>
							<div class="form-control">
								<label for="holidayDate" class="label text-xs">Date</label>
								<input id="holidayDate" type="date" class="input input-bordered input-sm" bind:value={newHolidayDate} required disabled={addingHoliday} />
							</div>
							<label class="label cursor-pointer flex items-center gap-2 text-sm">
								<input type="checkbox" class="checkbox checkbox-sm" bind:checked={newHolidayRecurring} />
								Recurring
							</label>
							<label class="label cursor-pointer flex items-center gap-2 text-sm">
								<input type="checkbox" class="checkbox checkbox-sm" bind:checked={newHolidayHalfDay} />
								Half day
							</label>
							<button type="submit" class="btn btn-primary btn-sm" disabled={addingHoliday}>
								{addingHoliday ? 'Adding...' : 'Add'}
							</button>
						</form>
					</div>
				{/if}

				{@const filtered = filteredHolidays()}
				{#if filtered.length === 0}
					<p class="text-base-content/40 text-sm">No holidays for {selectedYear}.{#if !canEdit} Holidays are managed by organization admins.{/if}</p>
				{:else}
					<div class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
						{#each filtered as h}
							<div class="flex items-center justify-between p-3 border-b border-base-200 last:border-b-0">
								{#if editingHolidayId === h.id && canEdit}
									<div class="flex gap-3 items-end flex-wrap w-full">
										<div class="form-control">
											<input type="text" class="input input-bordered input-sm" bind:value={editHolidayName} disabled={editHolidaySaving} />
										</div>
										<div class="form-control">
											<input type="date" class="input input-bordered input-sm" bind:value={editHolidayDate} disabled={editHolidaySaving} />
										</div>
										<label class="label cursor-pointer flex items-center gap-2 text-sm">
											<input type="checkbox" class="checkbox checkbox-sm" bind:checked={editHolidayRecurring} />
											Recurring
										</label>
										<label class="label cursor-pointer flex items-center gap-2 text-sm">
											<input type="checkbox" class="checkbox checkbox-sm" bind:checked={editHolidayHalfDay} />
											Half day
										</label>
										<div class="flex gap-1.5">
											<button class="btn btn-primary btn-sm" onclick={() => saveEditHoliday(h.id!)} disabled={editHolidaySaving}>Save</button>
											<button class="btn btn-ghost btn-sm" onclick={() => (editingHolidayId = null)}>Cancel</button>
										</div>
									</div>
								{:else}
									<div class="flex items-center gap-3">
										<span class="font-medium text-base-content">{h.name}</span>
										<span class="text-base-content/50 text-sm font-mono">{formatDateDisplay(h.date)}</span>
										{#if h.isRecurring}
											<span class="badge badge-sm badge-outline">Recurring</span>
										{/if}
										{#if h.isHalfDay}
											<span class="badge badge-sm badge-outline">½ day</span>
										{/if}
									</div>
									{#if canEdit}
										<div class="flex items-center gap-1.5">
											<button class="btn btn-ghost btn-sm" onclick={() => startEditHoliday(h)}>Edit</button>
											<button class="btn btn-ghost btn-xs text-error" title="Delete holiday" onclick={() => deleteHoliday(h.id!)}>&times;</button>
										</div>
									{/if}
								{/if}
							</div>
						{/each}
					</div>
				{/if}
			</section>
		</div>
	{/if}
</div>
