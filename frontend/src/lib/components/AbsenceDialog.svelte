<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { absenceDayApi, requestsApi } from '$lib/apiClient';
	import { t, tp } from '$lib/i18n';
	import { absenceTypeLabel, formatDate, getInitials } from '$lib/utils/formatters';
	import { dateKey } from '$lib/utils/dateHelpers';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import Modal from '$lib/components/Modal.svelte';
	import AbsenceTypeBadge from '$lib/components/AbsenceTypeBadge.svelte';
	import type {
		AbsenceType,
		AbsenceDayResponse,
		AbsenceTypeDefinitionResponse,
		OrganizationMemberResponse,
		RequestType
	} from '$lib/api';

	/**
	 * Small popup to add a vacation / sick day / other absence for a date (range).
	 *
	 * Flows:
	 * - `members` provided (team calendar): a member select is shown; org admins
	 *   create via the admin endpoint.
	 * - `fixedUserId` (a person's calendar): absences target that person —
	 *   admin endpoint for others, self endpoint (or approval request) for yourself.
	 */
	interface Props {
		open: boolean;
		orgSlug: string;
		/** Initial from-date (yyyy-MM-dd). */
		date: string;
		/** When set, a member select is rendered (team calendars). */
		members?: OrganizationMemberResponse[] | null;
		/** Fixed target person (member detail / history calendars). */
		fixedUserId?: number | null;
		/** Caller is org admin (may add for anyone). */
		isAdmin?: boolean;
		/** Caller's own absences need approval — submit a request instead. */
		mustRequest?: boolean;
		/** Org-defined custom absence types for the type select. */
		absenceTypes?: AbsenceTypeDefinitionResponse[];
		/** Existing absences on the clicked day (shown read-only). */
		dayAbsences?: AbsenceDayResponse[];
		onclose: () => void;
		/** Called after a successful save; message set when a request was sent. */
		onsaved: (requestMessage?: string) => void;
	}
	let {
		open, orgSlug, date, members = null, fixedUserId = null,
		isAdmin = false, mustRequest = false, absenceTypes = [],
		dayAbsences = [], onclose, onsaved
	}: Props = $props();

	const RequestAbsence = 6 as RequestType;
	const fieldId = $props.id();

	let fromDate = $state('');
	let toDate = $state('');
	let selectedUserId = $state<number | null>(null);
	let typeValue = $state('1'); // '0'|'1'|'2' or 'c{id}' for custom types
	let note = $state('');
	let halfDay = $state(false);
	let saving = $state(false);
	let submitted = $state(false);
	let errorMsg = $state('');

	// Reset the form whenever the dialog (re)opens for a date
	$effect(() => {
		if (open) {
			fromDate = date;
			toDate = '';
			selectedUserId = members ? null : (fixedUserId ?? auth.user?.id ?? null);
			typeValue = '1';
			note = '';
			halfDay = false;
			submitted = false;
			errorMsg = '';
		}
	});

	let targetUserId = $derived(members ? selectedUserId : (fixedUserId ?? auth.user?.id ?? null));
	let targetIsSelf = $derived(targetUserId === auth.user?.id);
	/** Self-targeted adds under RequiresApproval become requests (admins bypass). */
	let willRequest = $derived(!isAdmin && targetIsSelf && mustRequest);

	let toBeforeFrom = $derived(!!fromDate && !!toDate && toDate < fromDate);
	let memberMissing = $derived(!!members && selectedUserId == null);
	// The server keeps only the member's working days of the range (schedule incl. weekends, holidays)
	let workdays = $derived(fromDate && !toBeforeFrom ? daysInRange(fromDate, toDate) : []);
	let invalid = $derived(!fromDate || toBeforeFrom || memberMissing);

	let selectedCustomType = $derived(typeValue.startsWith('c') ? absenceTypes.find((at) => 'c' + at.id === typeValue) : undefined);

	function parseTypeSelection(value: string): { type: AbsenceType; customTypeId?: number } {
		if (value.startsWith('c')) return { type: 2 as AbsenceType, customTypeId: parseInt(value.slice(1), 10) };
		return { type: parseInt(value, 10) as AbsenceType };
	}

	/** All dates (yyyy-MM-dd) between from and to (inclusive; to defaults to from). */
	function daysInRange(from: string, to: string): string[] {
		const cursor = new Date(from + 'T00:00:00');
		const end = to ? new Date(to + 'T00:00:00') : new Date(cursor);
		const days: string[] = [];
		while (cursor <= end) {
			days.push(dateKey(cursor));
			cursor.setDate(cursor.getDate() + 1);
		}
		return days;
	}

	async function submit(e: Event) {
		e.preventDefault();
		submitted = true;
		if (invalid || targetUserId == null) return;
		saving = true;
		errorMsg = '';
		try {
			const { type, customTypeId } = parseTypeSelection(typeValue);

			if (willRequest) {
				await requestsApi.apiV1OrganizationsSlugRequestsPost(orgSlug, {
					type: RequestAbsence,
					message: note || undefined,
					requestData: JSON.stringify({ dates: workdays, type, customTypeId, isHalfDay: halfDay, note: note || undefined })
				});
				onsaved(tp('absences.dialog.requestSent', workdays.length));
			} else {
				// All weekdays of the range in one call, all-or-nothing (409 names the dates already taken)
				const range = { from: fromDate, to: toDate || fromDate, type, customTypeId, isHalfDay: halfDay, note: note || undefined };
				if (isAdmin) await absenceDayApi.apiV1OrganizationsSlugAbsencesAdminRangePost(orgSlug, { ...range, userId: targetUserId });
				else await absenceDayApi.apiV1OrganizationsSlugAbsencesRangePost(orgSlug, range);
				onsaved();
			}
			onclose();
		} catch (err) {
			errorMsg = extractErrorMessage(err, willRequest ? t('absences.dialog.requestFailed') : t('absences.dialog.addFailed'));
		} finally {
			saving = false;
		}
	}
</script>

<Modal {open} {onclose} title={date ? formatDate(date, { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' }) : undefined}>
	<!-- Existing absences on this day -->
	{#if dayAbsences.length > 0}
		<div class="mb-4">
			<h4 class="text-sm font-semibold text-base-content/60 mb-2">{t('absences.dialog.existing')}</h4>
			<ul class="flex flex-col gap-1">
				{#each dayAbsences as ab (ab.id)}
					<li class="flex items-center gap-2 p-2 bg-base-200/50 rounded-lg text-sm flex-wrap">
						<span class="w-6 h-6 rounded-full bg-primary text-primary-content flex items-center justify-center text-[10px] font-semibold shrink-0" aria-hidden="true">
							{getInitials(ab.userFirstName, ab.userLastName)}
						</span>
						<span class="font-medium">{ab.userFirstName} {ab.userLastName}</span>
						<AbsenceTypeBadge type={ab.type} customTypeName={ab.customTypeName} customTypeColor={ab.customTypeColor} size="xs" />
						{#if ab.isHalfDay}<span class="badge badge-xs badge-outline" title={t('absences.dialog.halfDay')}>½</span>{/if}
						{#if ab.note}<span class="text-base-content/50 text-xs truncate">{ab.note}</span>{/if}
					</li>
				{/each}
			</ul>
		</div>
		<div class="divider my-2 text-xs text-base-content/50">{willRequest ? t('absences.dialog.requestDivider') : t('absences.dialog.addDivider')}</div>
	{/if}

	{#if willRequest}
		<div class="alert alert-info text-sm py-1.5 px-2.5 mb-3">
			{t('absences.dialog.requestHint')}
		</div>
	{/if}
	{#if errorMsg}
		<div class="alert alert-error text-sm py-1.5 px-2.5 mb-3" role="alert">{errorMsg}</div>
	{/if}

	<form onsubmit={submit} class="flex flex-col gap-3" novalidate>
		{#if members}
			<div class="flex flex-col gap-1">
				<label for="{fieldId}-member" class="text-sm font-medium text-base-content/70">{t('absences.dialog.member')}</label>
				<select id="{fieldId}-member" bind:value={selectedUserId} class="select select-sm w-full" class:select-error={submitted && memberMissing} aria-invalid={submitted && memberMissing} disabled={saving}>
					<option value={null}>{t('absences.dialog.selectMember')}</option>
					{#each members as m (m.id)}
						<option value={m.id}>{m.firstName} {m.lastName}{m.id === auth.user?.id ? t('absences.youSuffix') : ''}</option>
					{/each}
				</select>
				{#if submitted && memberMissing}<span class="text-xs text-error">{t('absences.dialog.memberRequired')}</span>{/if}
			</div>
		{/if}
		<div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
			<div class="flex flex-col gap-1">
				<label for="{fieldId}-from" class="text-sm font-medium text-base-content/70">{t('absences.dialog.from')}</label>
				<input id="{fieldId}-from" type="date" class="input input-sm w-full" class:input-error={submitted && !fromDate} bind:value={fromDate} required disabled={saving} />
				{#if submitted && !fromDate}<span class="text-xs text-error">{t('common.required')}</span>{/if}
			</div>
			<div class="flex flex-col gap-1">
				<label for="{fieldId}-to" class="text-sm font-medium text-base-content/70">{t('absences.dialog.to')}</label>
				<input id="{fieldId}-to" type="date" class="input input-sm w-full" class:input-error={toBeforeFrom} aria-invalid={toBeforeFrom} bind:value={toDate} min={fromDate} disabled={saving} />
				{#if toBeforeFrom}<span class="text-xs text-error">{t('absences.dialog.toBeforeFrom')}</span>{/if}
			</div>
		</div>
		<div class="flex flex-col gap-1">
			<label for="{fieldId}-type" class="text-sm font-medium text-base-content/70">{t('absences.dialog.type')}</label>
			<select id="{fieldId}-type" bind:value={typeValue} class="select select-sm w-full" disabled={saving}>
				<option value="0">{absenceTypeLabel('SickDay')}</option>
				<option value="1">{absenceTypeLabel('Vacation')}</option>
				<option value="2">{absenceTypeLabel('Other')}</option>
				{#each absenceTypes as at (at.id)}
					<option value={'c' + at.id}>{at.name}</option>
				{/each}
			</select>
			{#if selectedCustomType}
				<span class="text-xs text-base-content/50">
					{selectedCustomType.creditMode === 'FillToTarget' ? t('absences.dialog.credit.FillToTarget')
					: selectedCustomType.creditMode === 'FullCredit' ? t('absences.dialog.credit.FullCredit')
					: t('absences.dialog.credit.None')}
				</span>
			{/if}
		</div>
		<div class="flex flex-col gap-1">
			<label for="{fieldId}-note" class="text-sm font-medium text-base-content/70">{t('absences.dialog.note')}</label>
			<input id="{fieldId}-note" type="text" class="input input-sm w-full" bind:value={note} placeholder={t('absences.dialog.notePlaceholder')} disabled={saving} />
		</div>
		<label class="label cursor-pointer flex items-center gap-2 text-sm">
			<input type="checkbox" class="checkbox checkbox-sm" bind:checked={halfDay} disabled={saving} />
			{t('absences.dialog.halfDay')}
		</label>
		<p class="text-xs text-base-content/50 m-0">{t('absences.dialog.workdaysHint')}</p>
		<div class="flex justify-end gap-2 mt-2">
			<button type="button" class="btn btn-ghost btn-sm" onclick={onclose}>{t('common.cancel')}</button>
			<button type="submit" class="btn btn-primary btn-sm" disabled={saving}>
				{saving ? (willRequest ? t('common.sending') : t('common.adding')) : willRequest ? t('absences.dialog.sendRequest') : t('absences.dialog.add')}
			</button>
		</div>
	</form>
</Modal>
