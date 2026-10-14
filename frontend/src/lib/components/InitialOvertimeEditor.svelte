<script lang="ts">
	import { organizationsApi, workScheduleApi } from '$lib/apiClient';
	import { formatNumber } from '$lib/utils/formatters';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';

	/**
	 * Initial overtime balance (carry-over from before tracking started).
	 * `memberId` set → admin member endpoint; unset → the caller's own endpoint.
	 * The page decides `editable`; `mode` (initialOvertimeMode) only explains a
	 * read-only value on self pages.
	 */
	interface Props {
		orgSlug: string;
		memberId?: number | null;
		/** Current value in hours. */
		hours: number;
		editable: boolean;
		mode?: string | null;
		onsaved?: () => void;
		class?: string;
	}
	let { orgSlug, memberId = null, hours, editable, mode = null, onsaved, class: className = '' }: Props = $props();

	const MAX_ABS = 1000;

	let editing = $state(false);
	let value = $state<number | null>(0);
	let submitted = $state(false);
	let saving = $state(false);
	let saveError = $state('');

	let valueError = $derived(
		!submitted ? '' : value == null || !Number.isFinite(value) ? t('schedule.overtime.invalid') : Math.abs(value) > MAX_ABS ? t('schedule.overtime.range', { max: MAX_ABS }) : ''
	);

	function signedHours(h: number): string {
		return t('common.hoursShort', { value: formatNumber(h, { signDisplay: 'exceptZero', maximumFractionDigits: 2 }) });
	}

	function startEdit() {
		value = hours;
		submitted = false;
		saveError = '';
		editing = true;
	}

	async function save(e: Event) {
		e.preventDefault();
		submitted = true;
		if (valueError || value == null) return;
		saving = true;
		saveError = '';
		try {
			if (memberId) await organizationsApi.apiV1OrganizationsSlugMembersMemberIdInitialOvertimePut(orgSlug, memberId, { initialOvertimeHours: value });
			else await workScheduleApi.apiV1OrganizationsSlugInitialOvertimePut(orgSlug, { initialOvertimeHours: value });
			toast.success(t('schedule.overtime.saved'));
			editing = false;
			onsaved?.();
		} catch (err) {
			saveError = extractErrorMessage(err, t('schedule.overtime.saveFailed'));
		} finally {
			saving = false;
		}
	}
</script>

<section class="card bg-base-100 border border-base-300 p-5 {className}">
	<div class="flex items-center justify-between gap-2 mb-3">
		<h2 class="text-base font-bold text-base-content m-0">{t('schedule.overtime.title')}</h2>
		{#if editable && !editing}
			<button type="button" class="btn btn-ghost btn-sm" onclick={startEdit}>{t('common.edit')}</button>
		{/if}
	</div>

	{#if editing}
		<form onsubmit={save} novalidate class="flex flex-col gap-2">
			{#if saveError}<div class="alert alert-error text-sm py-2">{saveError}</div>{/if}
			<label for="io-hours-{memberId ?? 'self'}" class="text-sm font-medium text-base-content/80">{t('schedule.overtime.hoursLabel')}</label>
			<div class="flex items-center gap-2 flex-wrap">
				<input id="io-hours-{memberId ?? 'self'}" type="number" step="0.5" min={-MAX_ABS} max={MAX_ABS} class="input input-sm w-32" class:input-error={valueError} aria-invalid={!!valueError} bind:value disabled={saving} />
				<button type="submit" class="btn btn-primary btn-sm" disabled={saving}>{saving ? t('common.saving') : t('common.save')}</button>
				<button type="button" class="btn btn-ghost btn-sm" onclick={() => (editing = false)} disabled={saving}>{t('common.cancel')}</button>
			</div>
			{#if valueError}<p class="text-error text-xs m-0">{valueError}</p>{/if}
		</form>
	{:else}
		<p class="text-2xl font-bold m-0 {hours > 0 ? 'text-success' : hours < 0 ? 'text-error' : 'text-base-content'}">{signedHours(hours)}</p>
		<p class="text-xs text-base-content/70 mt-1 mb-0">{t('schedule.overtime.description')}</p>
		{#if !editable && mode === 'RequiresApproval'}
			<p class="text-sm text-base-content/70 mt-2 mb-0">{t('schedule.overtime.approvalNote')}</p>
		{:else if !editable && mode === 'Disabled'}
			<p class="text-sm text-base-content/70 mt-2 mb-0">{t('schedule.overtime.disabledNote')}</p>
		{/if}
	{/if}
</section>
