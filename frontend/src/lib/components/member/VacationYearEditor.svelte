<script lang="ts">
	import Modal from '$lib/components/Modal.svelte';
	import { organizationsApi } from '$lib/apiClient';
	import type { MemberVacationResponse } from '$lib/api';
	import { t } from '$lib/i18n';
	import { toast } from '$lib/stores/toast.svelte';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { formatDate, formatDateTime, formatNumber } from '$lib/utils/formatters';
	import { orgYear, vacationDetailLines } from '$lib/utils/vacation';

	/** Admin: a member's vacation entitlement and adjustment (+ note) of one year, with that year's balance. */
	interface Props {
		open: boolean;
		onclose: () => void;
		orgSlug: string;
		memberId: number;
		memberName: string;
		/** The org's time zone: the editor starts at the org-local current year. */
		timeZone?: string | null;
		onsaved?: () => void;
	}
	let { open, onclose, orgSlug, memberId, memberName, timeZone = null, onsaved }: Props = $props();

	let year = $state(orgYear(null));
	/** Only the latest request may fill the form (a slow answer for a year typed over must not win). */
	let requestId = 0;
	let data = $state<MemberVacationResponse | null>(null);
	let loading = $state(false);
	let loadError = $state('');
	let entitlement = $state<number | null>(null);
	let adjustment = $state<number | null>(0);
	let note = $state('');
	let employmentStart = $state('');
	let employmentEnd = $state('');
	let expiresOn = $state('');
	let submitted = $state(false);
	let saving = $state(false);
	let saveError = $state('');

	let yearError = $derived(!Number.isInteger(year) || year < 2000 || year > 2100 ? t('vacation.year.yearRange') : '');
	let entitlementError = $derived(submitted && entitlement != null && (!Number.isFinite(entitlement) || entitlement < 0 || entitlement > 365) ? t('vacation.year.entitlementRange') : '');
	let adjustmentError = $derived(submitted && (adjustment == null || !Number.isFinite(adjustment) || adjustment < -365 || adjustment > 365) ? t('vacation.year.adjustmentRange') : '');
	let noteError = $derived(note.trim().length > 500 ? t('vacation.year.noteTooLong') : '');

	async function load() {
		if (yearError) return;
		const id = ++requestId;
		const wanted = year;
		loading = true;
		loadError = '';
		try {
			const { data: d } = await organizationsApi.apiV1OrganizationsSlugMembersMemberIdVacationGet(orgSlug, memberId, wanted);
			if (id !== requestId) return;
			data = d;
			entitlement = d.hasOwnEntitlement ? (d.entitlement ?? 0) : null;
			adjustment = d.adjustment ?? 0;
			note = d.adjustmentNote ?? '';
			employmentStart = d.employmentStart ?? '';
			employmentEnd = d.employmentEnd ?? '';
			expiresOn = d.ownExpiresOn ?? '';
			submitted = false;
			saveError = '';
		} catch (err) {
			if (id !== requestId) return;
			data = null;
			loadError = extractErrorMessage(err, t('vacation.year.loadFailed'));
		} finally {
			if (id === requestId) loading = false;
		}
	}

	// Every opening starts at the org-local current year
	$effect(() => {
		if (open) year = orgYear(timeZone);
	});

	// (Re)load whenever the dialog opens or the year changes
	$effect(() => {
		if (open && year) void load();
	});

	/** Sends one change for the shown year; ignores the answer when another year is shown by then. */
	async function put(body: { entitlement?: number; clearEntitlement?: boolean; adjustment?: number; note?: string; expiresOn?: string; clearExpiresOn?: boolean }, success: string) {
		const target = year;
		saving = true;
		saveError = '';
		try {
			const { data: d } = await organizationsApi.apiV1OrganizationsSlugMembersMemberIdVacationYearPut(orgSlug, memberId, target, body);
			toast.success(success);
			onsaved?.();
			if (target === year && d.year === year) {
				data = d;
				entitlement = d.hasOwnEntitlement ? (d.entitlement ?? 0) : null;
			}
		} catch (err) {
			saveError = extractErrorMessage(err, t('vacation.year.saveFailed'));
		} finally {
			saving = false;
		}
	}

	function clearEntitlement() {
		void put({ clearEntitlement: true }, t('vacation.year.cleared', { year }));
	}

	async function save(e: Event) {
		e.preventDefault();
		submitted = true;
		if (yearError || entitlementError || adjustmentError || noteError || !data) return;
		// Employment dates first: the year's figures below follow them
		if (employmentStart !== (data.employmentStart ?? '') || employmentEnd !== (data.employmentEnd ?? '')) {
			try {
				await organizationsApi.apiV1OrganizationsSlugMembersMemberIdEmploymentPut(orgSlug, memberId, {
					start: employmentStart || undefined, end: employmentEnd || undefined,
					clearStart: !employmentStart, clearEnd: !employmentEnd
				});
			} catch (err) {
				saveError = extractErrorMessage(err, t('vacation.year.saveFailed'));
				return;
			}
		}
		// An empty entitlement leaves it as it is; only the button resets it to inherited
		await put({
			entitlement: entitlement ?? undefined, adjustment: adjustment ?? 0, note: note.trim(),
			...(expiresOn !== (data.ownExpiresOn ?? '') ? (expiresOn ? { expiresOn } : { clearExpiresOn: true }) : {})
		}, t('vacation.year.saved', { year }));
	}
</script>

<Modal {open} {onclose} title={t('vacation.year.title', { name: memberName })} maxWidth="32rem">
	<form onsubmit={save} novalidate class="flex flex-col gap-3" data-testid="vacation-year-editor">
		{#if saveError}<div class="alert alert-error text-sm py-2" role="alert">{saveError}</div>{/if}
		<label class="flex items-center gap-2">
			<span class="text-sm font-medium text-base-content/80 w-28">{t('vacation.year.year')}</span>
			<input type="number" class="input input-sm w-28" min="2000" max="2100" step="1" bind:value={year} class:input-error={yearError} aria-invalid={!!yearError} disabled={saving} />
		</label>
		{#if yearError}<p class="text-error text-xs m-0">{yearError}</p>{/if}

		{#if loadError}
			<div class="alert alert-error text-sm py-2" role="alert">{loadError}</div>
		{:else if data}
			<div class="rounded-lg bg-base-200/50 border border-base-300 p-3 text-sm" aria-live="polite">
				<div class="font-semibold mb-1">{t('vacation.year.balance', { year: data.year ?? year })}</div>
				<dl class="grid grid-cols-2 gap-x-4 gap-y-0.5 m-0">
					<dt class="text-base-content/70">{t('vacation.year.entitlement')}</dt><dd class="m-0 text-right tabular-nums">{formatNumber(data.entitlement ?? 0)}</dd>
					<dt class="text-base-content/70">{t('vacation.year.carriedIn')}</dt><dd class="m-0 text-right tabular-nums">{formatNumber(data.carriedIn ?? 0)}</dd>
					<dt class="text-base-content/70">{t('vacation.year.expired')}</dt><dd class="m-0 text-right tabular-nums">{formatNumber(data.expired ?? 0)}</dd>
					<dt class="text-base-content/70">{t('vacation.year.available')}</dt><dd class="m-0 text-right tabular-nums">{formatNumber(data.available ?? 0)}</dd>
					<dt class="text-base-content/70">{t('vacation.year.used')}</dt><dd class="m-0 text-right tabular-nums">{formatNumber((data.taken ?? 0) + (data.planned ?? 0))}</dd>
					<dt class="font-semibold">{t('vacation.year.remaining')}</dt><dd class="m-0 text-right tabular-nums font-semibold">{formatNumber(data.remaining ?? 0)}</dd>
				</dl>
				{#if (data.annualEntitlement ?? data.entitlement) !== data.entitlement}
					<p class="text-xs text-base-content/70 m-0 mt-1">{t('vacation.year.proRata', { days: formatNumber(data.entitlement ?? 0), annual: formatNumber(data.annualEntitlement ?? 0) })}</p>
				{/if}
				{#if (data.carriedIn ?? 0) > 0}
					<p class="text-xs text-base-content/70 m-0 mt-1">{data.expiryNoticeAt ? t('vacation.year.noticeSent', { date: formatDate(data.expiryNoticeAt) }) : t('vacation.year.noNotice')}</p>
				{/if}
				{#each vacationDetailLines(data, data.year ?? year) as line (line)}
					<p class="text-xs text-base-content/70 m-0 mt-1">{line}</p>
				{/each}
				{#if data.updatedAt}
					<p class="text-xs text-base-content/70 m-0 mt-1">{t('vacation.year.updated', { date: formatDateTime(data.updatedAt), name: data.updatedByName ?? '—' })}</p>
				{/if}
			</div>

			<div class="flex flex-col gap-1">
				<label for="vac-year-entitlement" class="text-sm font-medium text-base-content/80">{t('vacation.year.entitlement')}</label>
				<div class="flex items-center gap-2 flex-wrap">
					<input id="vac-year-entitlement" type="number" step="0.5" min="0" max="365" class="input input-sm w-28" class:input-error={entitlementError} aria-invalid={!!entitlementError}
						bind:value={entitlement} placeholder={formatNumber(data.entitlement ?? 0)} disabled={saving || loading} />
					{#if data.hasOwnEntitlement}
						<button type="button" class="btn btn-ghost btn-xs" onclick={clearEntitlement} disabled={saving || loading}>{t('vacation.year.clear')}</button>
					{/if}
				</div>
				<p class="text-xs text-base-content/70 m-0">{data.hasOwnEntitlement ? t('vacation.year.own') : t('vacation.year.inherited', { days: formatNumber(data.entitlement ?? 0) })}</p>
				<p class="text-xs text-base-content/70 m-0">{t('vacation.year.entitlementHint')}</p>
				{#if entitlementError}<p class="text-error text-xs m-0">{entitlementError}</p>{/if}
			</div>

			<div class="flex flex-col gap-1">
				<label for="vac-year-adjustment" class="text-sm font-medium text-base-content/80">{t('vacation.year.adjustment')}</label>
				<input id="vac-year-adjustment" type="number" step="0.5" min="-365" max="365" class="input input-sm w-28" class:input-error={adjustmentError} aria-invalid={!!adjustmentError} bind:value={adjustment} disabled={saving || loading} />
				<p class="text-xs text-base-content/70 m-0">{t('vacation.year.adjustmentHint')}</p>
				{#if adjustmentError}<p class="text-error text-xs m-0">{adjustmentError}</p>{/if}
			</div>

			<fieldset class="flex flex-col gap-1">
				<legend class="text-sm font-medium text-base-content/80">{t('vacation.year.employmentStart')} / {t('vacation.year.employmentEnd')}</legend>
				<div class="flex gap-2 flex-wrap">
					<input type="date" class="input input-sm" aria-label={t('vacation.year.employmentStart')} bind:value={employmentStart} disabled={saving || loading} />
					<input type="date" class="input input-sm" aria-label={t('vacation.year.employmentEnd')} bind:value={employmentEnd} min={employmentStart || undefined} disabled={saving || loading} />
				</div>
				<p class="text-xs text-base-content/70 m-0">{t('vacation.year.employmentHint')}</p>
			</fieldset>

			<div class="flex flex-col gap-1">
				<label for="vac-year-expires" class="text-sm font-medium text-base-content/80">{t('vacation.year.expiresOn')}</label>
				<input id="vac-year-expires" type="date" class="input input-sm w-44" min="{data.year ?? year}-01-01" max="{data.year ?? year}-12-31" bind:value={expiresOn} disabled={saving || loading} />
				<p class="text-xs text-base-content/70 m-0">{t('vacation.year.expiresOnHint')}</p>
			</div>

			<div class="flex flex-col gap-1">
				<label for="vac-year-note" class="text-sm font-medium text-base-content/80">{t('vacation.year.note')}</label>
				<input id="vac-year-note" type="text" maxlength="500" class="input input-sm w-full" class:input-error={noteError} aria-invalid={!!noteError} placeholder={t('vacation.year.notePlaceholder')} bind:value={note} disabled={saving || loading} />
				{#if noteError}<p class="text-error text-xs m-0">{noteError}</p>{/if}
			</div>

			<div class="flex justify-end gap-2 mt-1">
				<button type="button" class="btn btn-ghost btn-sm" onclick={onclose} disabled={saving}>{t('common.close')}</button>
				<button type="submit" class="btn btn-primary btn-sm" disabled={saving || loading}>{saving ? t('common.saving') : t('common.save')}</button>
			</div>
		{:else if loading}
			<p class="text-sm text-base-content/70 m-0">{t('common.loading')}</p>
		{/if}
	</form>
</Modal>
