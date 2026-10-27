<script lang="ts">
	import Modal from '$lib/components/Modal.svelte';
	import { appointmentsApi } from '$lib/apiClient';
	import { t } from '$lib/i18n';
	import { auth } from '$lib/stores/auth.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { formatDate } from '$lib/utils/formatters';
	import type { AppointmentResponse } from '$lib/api';

	/** Create (appointment null) or edit an appointment ("Termin"); information only, no hours. */
	interface Props {
		open: boolean;
		orgSlug: string;
		/** Prefilled date for a new appointment. */
		date: string;
		appointment: AppointmentResponse | null;
		/** Whose appointment a new one is; someone else's only for admins. */
		userId: number | null;
		/** Shown as "Für …" when it is someone else's. */
		userName?: string;
		onclose: () => void;
		onsaved: () => void;
		/** Switches to the absence dialog for this date (new appointments only). */
		onabsence?: (date: string) => void;
	}
	let { open, orgSlug, date, appointment, userId, userName, onclose, onsaved, onabsence }: Props = $props();

	const fieldId = $props.id();
	let day = $state('');
	let allDay = $state(false);
	let from = $state('');
	let to = $state('');
	let note = $state('');
	let saving = $state(false);
	let errorMsg = $state('');

	$effect(() => {
		if (open) {
			day = appointment?.date ?? date;
			allDay = appointment?.allDay ?? false;
			from = appointment?.startTime ?? '';
			to = appointment?.endTime ?? '';
			note = appointment?.note ?? '';
			errorMsg = '';
		}
	});

	const targetUserId = $derived(appointment?.userId ?? userId);
	const forOther = $derived(targetUserId != null && targetUserId !== auth.user?.id);

	async function submit(e: Event) {
		e.preventDefault();
		if (!allDay && (!from || !to || to <= from)) {
			errorMsg = t('appointments.dialog.timesMissing');
			return;
		}
		saving = true;
		errorMsg = '';
		const body = {
			date: day,
			startTime: allDay ? undefined : from,
			endTime: allDay ? undefined : to,
			note: note.trim() || undefined,
			userId: forOther ? (targetUserId ?? undefined) : undefined
		};
		try {
			if (appointment?.id) await appointmentsApi.apiV1OrganizationsSlugAppointmentsIdPut(orgSlug, appointment.id, body);
			else await appointmentsApi.apiV1OrganizationsSlugAppointmentsPost(orgSlug, body);
			toast.success(t('appointments.dialog.saved'));
			onsaved();
			onclose();
		} catch (err) {
			errorMsg = extractErrorMessage(err, t('appointments.dialog.saveFailed'));
		} finally {
			saving = false;
		}
	}

	async function remove() {
		if (!appointment?.id) return;
		const ok = await confirmDialog({
			title: t('appointments.dialog.deleteTitle'),
			message: `${formatDate(appointment.date ?? '')}${appointment.note ? `: ${appointment.note}` : ''}`,
			confirmLabel: t('common.delete'),
			danger: true
		});
		if (!ok) return;
		saving = true;
		try {
			await appointmentsApi.apiV1OrganizationsSlugAppointmentsIdDelete(orgSlug, appointment.id);
			toast.success(t('appointments.dialog.deleted'));
			onsaved();
			onclose();
		} catch (err) {
			toast.error(err, t('appointments.dialog.deleteFailed'));
		} finally {
			saving = false;
		}
	}
</script>

<Modal {open} {onclose} title={appointment ? t('appointments.dialog.titleEdit') : t('appointments.dialog.titleNew')}>
	<form onsubmit={submit} class="flex flex-col gap-3" novalidate data-testid="appointment-form">
		{#if forOther && userName}
			<p class="text-sm text-base-content/70 m-0">{t('appointments.dialog.forName', { name: userName })}</p>
		{/if}
		{#if errorMsg}<div class="alert alert-error text-sm py-2" role="alert">{errorMsg}</div>{/if}
		<div class="flex flex-col gap-1">
			<label for="{fieldId}-date" class="text-sm font-medium text-base-content/70">{t('appointments.dialog.date')}</label>
			<input id="{fieldId}-date" type="date" class="input input-sm w-full" bind:value={day} required disabled={saving} />
		</div>
		<label class="label cursor-pointer flex items-center gap-2 text-sm">
			<input type="checkbox" class="checkbox checkbox-sm" bind:checked={allDay} disabled={saving} />
			{t('appointments.dialog.allDay')}
		</label>
		{#if !allDay}
			<div class="grid grid-cols-2 gap-3">
				<div class="flex flex-col gap-1">
					<label for="{fieldId}-from" class="text-sm font-medium text-base-content/70">{t('appointments.dialog.from')}</label>
					<input id="{fieldId}-from" type="time" class="input input-sm w-full" bind:value={from} disabled={saving} />
				</div>
				<div class="flex flex-col gap-1">
					<label for="{fieldId}-to" class="text-sm font-medium text-base-content/70">{t('appointments.dialog.to')}</label>
					<input id="{fieldId}-to" type="time" class="input input-sm w-full" bind:value={to} disabled={saving} />
				</div>
			</div>
		{/if}
		<div class="flex flex-col gap-1">
			<label for="{fieldId}-note" class="text-sm font-medium text-base-content/70">{t('appointments.dialog.note')}</label>
			<input id="{fieldId}-note" type="text" class="input input-sm w-full" maxlength="200" bind:value={note} placeholder={t('appointments.dialog.notePlaceholder')} disabled={saving} />
		</div>
		<p class="text-xs text-base-content/70 m-0">{t('appointments.dialog.hint')}</p>
		{#if !appointment && onabsence}
			<button type="button" class="btn btn-ghost btn-sm self-start" onclick={() => { onclose(); onabsence(day); }}>{t('appointments.dialog.absenceInstead')}</button>
		{/if}
		<div class="flex justify-end gap-2 mt-2">
			{#if appointment}
				<button type="button" class="btn btn-ghost btn-sm text-error mr-auto" onclick={remove} disabled={saving}>{t('common.delete')}</button>
			{/if}
			<button type="button" class="btn btn-ghost btn-sm" onclick={onclose}>{t('common.cancel')}</button>
			<button type="submit" class="btn btn-primary btn-sm" disabled={saving}>{t('appointments.dialog.save')}</button>
		</div>
	</form>
</Modal>
