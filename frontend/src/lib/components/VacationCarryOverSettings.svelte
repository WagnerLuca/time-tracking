<script lang="ts">
	import type { OrganizationDetailResponse, UpdateOrganizationSettingsRequest } from '$lib/api';
	import { t } from '$lib/i18n';
	import { toast } from '$lib/stores/toast.svelte';
	import { formatDate } from '$lib/utils/formatters';
	import { CARRY_OVER_MODES, carryOverModeValue, clampExpiryDay, expiryValue, isValidExpiry, orgYear, parseExpiry, type CarryOverModeName } from '$lib/utils/vacation';

	/** Org settings (Abwesenheiten): what happens to leftover vacation days — rule, expiry, cap, start year. */
	interface Props {
		org: OrganizationDetailResponse;
		disabled: boolean;
		save: (patch: UpdateOrganizationSettingsRequest) => unknown;
	}
	let { org, disabled, save }: Props = $props();

	/** The org-local year (the backend checks `since` against it). */
	let currentYear = $derived(orgYear(org.timeZone));
	let mode = $derived((CARRY_OVER_MODES as string[]).includes(org.vacationCarryOverMode ?? '') ? (org.vacationCarryOverMode as CarryOverModeName) : 'UntilDate');
	let expiry = $derived(parseExpiry(org.vacationCarryOverExpiry));
	const months = $derived(Array.from({ length: 12 }, (_, i) => ({ value: i + 1, label: formatDate(new Date(2024, i, 1), { month: 'long' }) })));

	function saveMode(e: Event) {
		const value = (e.currentTarget as HTMLSelectElement).value;
		save({ vacationCarryOverMode: carryOverModeValue(value) });
	}

	/** Saves day and month together; a new month clamps the day (31 → 30 in April), so no step in between is invalid. */
	function saveExpiry() {
		const month = Number((document.getElementById('carry-over-month') as HTMLSelectElement | null)?.value);
		const dayInput = document.getElementById('carry-over-day') as HTMLInputElement | null;
		let day = Number(dayInput?.value);
		if (Number.isInteger(day) && day > 0 && Number.isInteger(month) && month >= 1 && month <= 12) {
			day = clampExpiryDay(month, day);
			if (dayInput) dayInput.value = String(day);
		}
		if (!isValidExpiry(month, day)) {
			toast.error(t('orgSettings.carryOver.invalidExpiry'));
			return;
		}
		save({ vacationCarryOverExpiry: expiryValue(month, day) });
	}

	function saveMax(e: Event) {
		const raw = (e.currentTarget as HTMLInputElement).value.trim();
		if (raw === '') {
			save({ clearVacationCarryOverMaxDays: true });
			return;
		}
		const value = Number(raw);
		if (!Number.isFinite(value) || value < 0 || value > 365) {
			toast.error(t('orgSettings.carryOver.invalidMax'));
			return;
		}
		save({ vacationCarryOverMaxDays: value });
	}

	function saveSince(e: Event) {
		const value = Number((e.currentTarget as HTMLInputElement).value);
		if (!Number.isInteger(value) || value < 2000 || value > currentYear) {
			toast.error(t('orgSettings.carryOver.invalidSince', { year: currentYear }));
			return;
		}
		save({ vacationCarryOverSince: value });
	}
</script>

<div class="bg-base-200/50 rounded-lg p-4 mt-4 border border-base-300" data-testid="carry-over-settings">
	<div class="min-w-0">
		<div class="font-semibold text-base-content mb-0.5">{t('orgSettings.carryOver.label')}</div>
		<div class="text-sm text-base-content/70 max-w-[560px]">{t('orgSettings.carryOver.description')}</div>
	</div>
	<div class="flex flex-col gap-3 mt-3 text-sm">
		<label class="flex items-center gap-2 flex-wrap">
			<span class="text-base-content/70 w-40">{t('orgSettings.carryOver.mode')}</span>
			<select id="carry-over-mode" class="select select-sm w-full sm:w-64" value={mode} onchange={saveMode} {disabled}>
				{#each CARRY_OVER_MODES as m (m)}
					<option value={m}>{t(`orgSettings.carryOver.modes.${m}`)}</option>
				{/each}
			</select>
		</label>
		<p class="text-xs text-base-content/70 m-0">{t(`orgSettings.carryOver.modeHints.${mode}`)}</p>

		{#if mode === 'UntilDate'}
			<div class="flex items-center gap-2 flex-wrap">
				<span class="text-base-content/70 w-40">{t('orgSettings.carryOver.expiry')}</span>
				<input id="carry-over-day" type="number" class="input input-sm w-20 text-center" min="1" max="31" step="1" value={expiry.day} aria-label={t('orgSettings.carryOver.day')} onchange={saveExpiry} {disabled} />
				<select id="carry-over-month" class="select select-sm w-40" value={expiry.month} aria-label={t('orgSettings.carryOver.month')} onchange={saveExpiry} {disabled}>
					{#each months as m (m.value)}
						<option value={m.value}>{m.label}</option>
					{/each}
				</select>
			</div>
		{/if}

		{#if mode !== 'None'}
			<label class="flex items-center gap-2 flex-wrap">
				<span class="text-base-content/70 w-40">{t('orgSettings.carryOver.maxDays')}</span>
				<input id="carry-over-max" type="number" class="input input-sm w-24 text-center" min="0" max="365" step="0.5" value={org.vacationCarryOverMaxDays ?? ''} placeholder={t('orgSettings.carryOver.maxDaysPlaceholder')} onchange={saveMax} {disabled} />
				<span class="text-base-content/70">{t('orgSettings.carryOver.unit')}</span>
			</label>
			<label class="flex items-center gap-2 flex-wrap">
				<span class="text-base-content/70 w-40">{t('orgSettings.carryOver.since')}</span>
				<input id="carry-over-since" type="number" class="input input-sm w-24 text-center" min="2000" max={currentYear} step="1" value={org.vacationCarryOverSince ?? currentYear} onchange={saveSince} {disabled} />
			</label>
			<p class="text-xs text-base-content/70 m-0">{t('orgSettings.carryOver.sinceHint')}</p>
		{/if}
	</div>
</div>
