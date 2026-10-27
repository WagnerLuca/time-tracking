<script lang="ts">
	import { holidayApi, organizationsApi } from '$lib/apiClient';
	import { invalidateOrgDetail } from '$lib/stores/orgDetail';
	import { toast } from '$lib/stores/toast.svelte';
	import { t, tp } from '$lib/i18n';
	import { formatDate, holidayRegionLabel } from '$lib/utils/formatters';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { dateKey } from '$lib/utils/dateHelpers';
	import type { HolidayPresetResponse, PublicHolidayResponse } from '$lib/api';

	/**
	 * The organization's public holiday region (calculated by the server, never stored as rows): admins pick it, members
	 * see it. Lists the region's holidays of the shown year and the viewer's own region when it differs (their schedule or
	 * groups name another state).
	 */
	interface Props {
		slug: string;
		region: string | null | undefined;
		year: number;
		canEdit: boolean;
		onchange: () => void;
	}
	let { slug, region, year, canEdit, onchange }: Props = $props();

	let presets = $state<HolidayPresetResponse[]>([]);
	let days = $state<PublicHolidayResponse[]>([]);
	let previewError = $state('');
	let saving = $state(false);
	let removing = $state(false);
	let own = $state<{ region: string | null; conflict: boolean } | null>(null);

	$effect(() => {
		holidayApi.apiV1OrganizationsHolidayPresetsGet().then((r) => (presets = r.data)).catch(() => (presets = []));
	});

	$effect(() => {
		const key = region;
		const y = year;
		previewError = '';
		if (!key) {
			days = [];
			return;
		}
		holidayApi.apiV1OrganizationsHolidayPresetsRegionDatesGet(key, y)
			.then((r) => (days = r.data))
			.catch((err) => {
				days = [];
				previewError = extractErrorMessage(err, t('holidays.region.previewFailed'));
			});
	});

	$effect(() => {
		const s = slug;
		const key = dateKey(new Date());
		holidayApi.apiV1OrganizationsSlugHolidaysDaysGet(s, key, key)
			.then((r) => (own = { region: r.data.region ?? null, conflict: r.data.regionConflict ?? false }))
			.catch(() => (own = null));
	});

	async function save(e: Event) {
		const key = (e.currentTarget as HTMLSelectElement).value;
		saving = true;
		try {
			await organizationsApi.apiV1OrganizationsSlugSettingsPut(slug, { holidayRegion: key });
			invalidateOrgDetail(slug);
			toast.success(t('holidays.region.saved'));
			onchange();
		} catch (err) {
			toast.error(err, t('holidays.region.saveFailed'));
		} finally {
			saving = false;
		}
	}

	async function removeCovered() {
		removing = true;
		try {
			const { data } = await holidayApi.apiV1OrganizationsSlugHolidaysRemoveCoveredPost(slug);
			invalidateOrgDetail(slug);
			toast.success(tp('holidays.region.removeCoveredDone', data));
			onchange();
		} catch (err) {
			toast.error(err, t('holidays.region.removeCoveredFailed'));
		} finally {
			removing = false;
		}
	}
</script>

<section class="card bg-base-100 border border-base-300 p-4 mb-6" aria-labelledby="region-title">
	<h2 id="region-title" class="text-lg font-semibold m-0 mb-1">{t('holidays.region.title')}</h2>
	<p class="text-sm text-base-content/70 mt-0 mb-3">{t('holidays.region.intro')}</p>

	{#if canEdit}
		<div class="flex gap-3 items-end flex-wrap mb-3">
			<div class="form-control w-full sm:w-auto">
				<label for="orgHolidayRegion" class="label text-xs">{t('holidays.region.label')}</label>
				<select id="orgHolidayRegion" class="select select-sm w-full sm:w-72" value={region ?? ''} onchange={save} disabled={saving}>
					<option value="">{t('holidays.region.none')}</option>
					{#each presets as p (p.key)}
						<option value={p.key}>{holidayRegionLabel(p.key, p.name)}</option>
					{/each}
				</select>
			</div>
			{#if region}
				<button type="button" class="btn btn-outline btn-sm" onclick={removeCovered} disabled={removing} title={t('holidays.region.removeCoveredHint')}>
					{t('holidays.region.removeCovered')}
				</button>
			{/if}
		</div>
	{:else if region}
		<p class="text-sm m-0 mb-2">{t('holidays.region.readOnly', { region: holidayRegionLabel(region) })}</p>
	{/if}

	{#if own?.conflict}
		<p class="text-sm text-warning m-0 mb-2">{t('holidays.region.unclear')}</p>
	{:else if own?.region && own.region !== region}
		<p class="text-sm m-0 mb-2">{t('holidays.region.yours', { region: holidayRegionLabel(own.region) })}</p>
	{/if}

	{#if previewError}
		<p class="text-sm text-error m-0">{previewError}</p>
	{:else if region && days.length > 0}
		<details>
			<summary class="text-sm cursor-pointer">{tp('holidays.region.preview', days.length, { year })}</summary>
			<ul class="mt-2 grid gap-x-6 gap-y-1 sm:grid-cols-2 text-sm list-none p-0 m-0">
				{#each days as d (d.date)}
					<li class="flex gap-3"><span class="tabular-nums text-base-content/70 w-24 shrink-0">{formatDate(d.date ?? '')}</span>{d.name}</li>
				{/each}
			</ul>
		</details>
	{/if}
</section>
