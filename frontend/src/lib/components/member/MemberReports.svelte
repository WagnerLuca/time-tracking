<script lang="ts">
	import { reportsApi } from '$lib/apiClient';
	import { getMonthName } from '$lib/utils/formatters';
	import { toast } from '$lib/stores/toast.svelte';

	/** Monthly (day by day) and yearly (month by month) PDF working-hours reports for one member. */
	interface Props {
		orgSlug: string;
		memberId: number;
		/** Whose report it is, for the intro text. */
		subject: string;
	}
	let { orgSlug, memberId, subject }: Props = $props();

	const now = new Date();
	let month = $state(now.getMonth() + 1);
	let monthYear = $state<number | null>(now.getFullYear());
	let year = $state<number | null>(now.getFullYear());
	let monthlyBusy = $state(false);
	let yearlyBusy = $state(false);

	function validYear(y: number | null): y is number {
		return y != null && Number.isInteger(y) && y >= 2000 && y <= 2100;
	}

	function downloadBlob(data: BlobPart, filename: string) {
		const url = URL.createObjectURL(new Blob([data], { type: 'application/pdf' }));
		const a = document.createElement('a');
		a.href = url;
		a.download = filename;
		a.click();
		URL.revokeObjectURL(url);
	}

	async function downloadMonthly() {
		if (!validYear(monthYear)) return;
		monthlyBusy = true;
		try {
			const res = await reportsApi.apiV1OrganizationsSlugMembersMemberIdReportsMonthlyGet(orgSlug, memberId, monthYear, month, { responseType: 'blob' });
			downloadBlob(res.data as unknown as BlobPart, `time-report-${orgSlug}-${monthYear}-${String(month).padStart(2, '0')}.pdf`);
			toast.success('Monthly report downloaded.');
		} catch (err) {
			toast.error(err, 'Could not create the monthly report.');
		} finally {
			monthlyBusy = false;
		}
	}

	async function downloadYearly() {
		if (!validYear(year)) return;
		yearlyBusy = true;
		try {
			const res = await reportsApi.apiV1OrganizationsSlugMembersMemberIdReportsYearlyGet(orgSlug, memberId, year, { responseType: 'blob' });
			downloadBlob(res.data as unknown as BlobPart, `time-report-${orgSlug}-${year}.pdf`);
			toast.success('Yearly report downloaded.');
		} catch (err) {
			toast.error(err, 'Could not create the yearly report.');
		} finally {
			yearlyBusy = false;
		}
	}
</script>

<section class="card bg-base-100 border border-base-300 p-5 mb-5">
	<h2 class="text-base font-bold text-base-content m-0 mb-1">Reports</h2>
	<p class="text-xs text-base-content/50 mb-4">Printable PDF working-hours reports for {subject}: day by day for a month, or month by month for a year.</p>

	<div class="grid gap-4 sm:grid-cols-2">
		<div class="bg-base-200/40 border border-base-300 rounded-lg p-3.5">
			<h3 class="font-semibold text-sm m-0 mb-2.5">Monthly report</h3>
			<div class="flex items-start gap-2 flex-wrap">
				<select class="select select-bordered select-sm" bind:value={month} aria-label="Month" disabled={monthlyBusy}>
					{#each Array.from({ length: 12 }, (_, i) => i + 1) as m}
						<option value={m}>{getMonthName(new Date(2000, m - 1, 1))}</option>
					{/each}
				</select>
				<div>
					<input type="number" class="input input-bordered input-sm w-24" class:input-error={!validYear(monthYear)} aria-invalid={!validYear(monthYear)} bind:value={monthYear} min="2000" max="2100" aria-label="Year" disabled={monthlyBusy} />
					{#if !validYear(monthYear)}<p class="text-error text-xs mt-1 mb-0">2000–2100</p>{/if}
				</div>
				<button type="button" class="btn btn-primary btn-sm" onclick={downloadMonthly} disabled={monthlyBusy || !validYear(monthYear)}>
					{#if monthlyBusy}<span class="loading loading-spinner loading-xs"></span> Creating…{:else}Download PDF{/if}
				</button>
			</div>
		</div>

		<div class="bg-base-200/40 border border-base-300 rounded-lg p-3.5">
			<h3 class="font-semibold text-sm m-0 mb-2.5">Yearly report</h3>
			<div class="flex items-start gap-2 flex-wrap">
				<div>
					<input type="number" class="input input-bordered input-sm w-24" class:input-error={!validYear(year)} aria-invalid={!validYear(year)} bind:value={year} min="2000" max="2100" aria-label="Year" disabled={yearlyBusy} />
					{#if !validYear(year)}<p class="text-error text-xs mt-1 mb-0">2000–2100</p>{/if}
				</div>
				<button type="button" class="btn btn-primary btn-sm" onclick={downloadYearly} disabled={yearlyBusy || !validYear(year)}>
					{#if yearlyBusy}<span class="loading loading-spinner loading-xs"></span> Creating…{:else}Download PDF{/if}
				</button>
			</div>
		</div>
	</div>
</section>
