<script lang="ts">
	import { organizationsApi } from '$lib/apiClient';
	import type { OrganizationMemberResponse } from '$lib/api';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';

	/** A member's personnel number for the payroll export — admins only (the API sends it to nobody else). */
	interface Props {
		orgSlug: string;
		member: OrganizationMemberResponse;
		onchanged?: () => void;
	}
	let { orgSlug, member, onchanged }: Props = $props();

	const MAX = 50;
	let editing = $state(false);
	let value = $state('');
	let saving = $state(false);
	let saveError = $state('');
	let tooLong = $derived(value.trim().length > MAX);

	function startEdit() {
		value = member.personnelNumber ?? '';
		saveError = '';
		editing = true;
	}

	async function save(e: Event) {
		e.preventDefault();
		if (tooLong) return;
		saving = true;
		saveError = '';
		try {
			await organizationsApi.apiV1OrganizationsSlugMembersMemberIdPersonnelNumberPut(orgSlug, member.id!, { personnelNumber: value.trim() || null });
			toast.success(t('memberDetail.personnelNumber.saved'));
			editing = false;
			onchanged?.();
		} catch (err) {
			saveError = extractErrorMessage(err, t('memberDetail.personnelNumber.saveFailed'));
		} finally {
			saving = false;
		}
	}
</script>

<section class="card bg-base-100 border border-base-300 p-5 mb-5">
	<div class="flex items-center justify-between gap-2">
		<h2 class="text-base font-bold text-base-content m-0">{t('memberDetail.personnelNumber.title')}</h2>
		{#if !editing}
			<button type="button" class="btn btn-ghost btn-sm" onclick={startEdit}>{t('common.edit')}</button>
		{/if}
	</div>
	{#if editing}
		<form onsubmit={save} novalidate class="flex flex-col gap-2 mt-3">
			{#if saveError}<div class="alert alert-error text-sm py-2">{saveError}</div>{/if}
			<label for="personnel-number-{member.id}" class="text-sm font-medium text-base-content/80">{t('memberDetail.personnelNumber.label')}</label>
			<div class="flex items-center gap-2 flex-wrap">
				<input id="personnel-number-{member.id}" type="text" maxlength={MAX} class="input input-sm w-48" class:input-error={tooLong} aria-invalid={tooLong} bind:value disabled={saving} />
				<button type="submit" class="btn btn-primary btn-sm" disabled={saving}>{saving ? t('common.saving') : t('common.save')}</button>
				<button type="button" class="btn btn-ghost btn-sm" onclick={() => (editing = false)} disabled={saving}>{t('common.cancel')}</button>
			</div>
			<p class="text-xs text-base-content/70 m-0">{tooLong ? t('memberDetail.personnelNumber.tooLong', { max: MAX }) : t('memberDetail.personnelNumber.hint')}</p>
		</form>
	{:else}
		<p class="text-sm m-0 mt-2 {member.personnelNumber ? 'text-base-content' : 'text-base-content/70'}">
			{member.personnelNumber || t('memberDetail.personnelNumber.none')}
		</p>
	{/if}
</section>
