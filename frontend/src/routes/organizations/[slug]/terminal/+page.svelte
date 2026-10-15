<script lang="ts">
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { getOrgDetail, invalidateOrgDetail } from '$lib/stores/orgDetail';
	import { organizationsApi } from '$lib/apiClient';
	import { toast } from '$lib/stores/toast.svelte';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { t } from '$lib/i18n';
	import type { OrganizationDetailResponse } from '$lib/api';
	import ApiKeysPanel from '$lib/components/ApiKeysPanel.svelte';
	import Skeleton from '$lib/components/Skeleton.svelte';

	const orgSlug = $derived(page.params.slug ?? '');
	let org = $state<OrganizationDetailResponse | null>(null);
	let error = $state('');

	// Terminal rules (org settings; ranges as in the backend's TerminalLimits)
	let doubleScan = $state<number | null>(60);
	let offline = $state<number | null>(72);
	let unknownLimit = $state<number | null>(20);
	let saving = $state(false);
	let submitted = $state(false);
	const inRange = (v: number | null, min: number, max: number) => v != null && Number.isInteger(v) && v >= min && v <= max;
	const doubleScanBad = $derived(submitted && !inRange(doubleScan, 5, 600));
	const offlineBad = $derived(submitted && !inRange(offline, 1, 168));
	const unknownBad = $derived(submitted && !inRange(unknownLimit, 5, 200));

	$effect(() => {
		const slug = orgSlug;
		untrack(async () => {
			org = null;
			error = '';
			try {
				org = await getOrgDetail(slug);
				doubleScan = org.terminalDoubleScanSeconds ?? 60;
				offline = org.terminalOfflineHours ?? 72;
				unknownLimit = org.terminalUnknownCardLimit ?? 20;
			} catch (err) {
				error = extractErrorMessage(err, t('orgSettings.loadOrgFailed'));
			}
		});
	});

	async function saveRules(e: Event) {
		e.preventDefault();
		submitted = true;
		if (doubleScanBad || offlineBad || unknownBad) {
			toast.error(t('orgSettings.terminal.rulesInvalid'));
			return;
		}
		saving = true;
		try {
			await organizationsApi.apiV1OrganizationsSlugSettingsPut(orgSlug, {
				terminalDoubleScanSeconds: doubleScan!,
				terminalOfflineHours: offline!,
				terminalUnknownCardLimit: unknownLimit!
			});
			invalidateOrgDetail(orgSlug);
			submitted = false;
			toast.success(t('orgSettings.terminal.rulesSaved'));
		} catch (err) {
			toast.error(err, t('orgSettings.terminal.rulesFailed'));
		} finally {
			saving = false;
		}
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: t('orgs.tabs.terminal') })}</title>
</svelte:head>

{#snippet numberField(id: string, label: string, hint: string, unit: string, min: number, max: number, bad: boolean, value: number | null, set: (v: number | null) => void)}
	<div>
		<label for={id} class="label text-sm font-medium text-base-content/80">{label}</label>
		<div class="flex items-center gap-2">
			<input
				{id}
				type="number"
				inputmode="numeric"
				class="input input-sm w-24"
				class:input-error={bad}
				aria-invalid={bad}
				aria-describedby="{id}-hint"
				{min}
				{max}
				step="1"
				{value}
				oninput={(e) => set(e.currentTarget.value === '' ? null : Number(e.currentTarget.value))}
				disabled={saving}
			/>
			<span class="text-sm text-base-content/70">{unit}</span>
		</div>
		<p id="{id}-hint" class="text-xs mt-1 mb-0 {bad ? 'text-error' : 'text-base-content/70'}">{hint}</p>
	</div>
{/snippet}

<div class="max-w-5xl mx-auto sm:px-6 pb-6">
	{#if error}
		<div class="alert alert-error" role="alert">{error}</div>
	{:else if !org}
		<Skeleton />
	{:else}
		<section class="card bg-base-100 border border-base-300 p-4 sm:p-5 mb-6" aria-labelledby="terminal-rules" data-testid="terminal-rules">
			<h2 id="terminal-rules" class="text-base font-semibold m-0">{t('orgSettings.terminal.rulesTitle')}</h2>
			<p class="text-sm text-base-content/70 mt-1 mb-4">{t('orgSettings.terminal.rulesIntro')}</p>
			<form onsubmit={saveRules} novalidate class="flex flex-col gap-4">
				<div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
					{@render numberField('terminal-double-scan', t('orgSettings.terminal.doubleScan'), t('orgSettings.terminal.doubleScanHint'), t('orgSettings.terminal.seconds'), 5, 600, doubleScanBad, doubleScan, (v) => (doubleScan = v))}
					{@render numberField('terminal-offline', t('orgSettings.terminal.offline'), t('orgSettings.terminal.offlineHint'), t('orgSettings.terminal.hours'), 1, 168, offlineBad, offline, (v) => (offline = v))}
					{@render numberField('terminal-unknown', t('orgSettings.terminal.unknownLimit'), t('orgSettings.terminal.unknownLimitHint'), t('orgSettings.terminal.perTenMinutes'), 5, 200, unknownBad, unknownLimit, (v) => (unknownLimit = v))}
				</div>
				<div>
					<button type="submit" class="btn btn-primary btn-sm max-sm:w-full" disabled={saving}>{saving ? t('common.saving') : t('common.save')}</button>
				</div>
			</form>
		</section>
		<ApiKeysPanel {orgSlug} members={org.members ?? []} />
	{/if}
</div>
