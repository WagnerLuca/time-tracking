<script lang="ts">
	import { apiKeysApi } from '$lib/apiClient';
	import type { OrganizationMemberResponse } from '$lib/api';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';

	/** Terminal badge (RFID tag) of a member — admins only. */
	interface Props {
		orgSlug: string;
		member: OrganizationMemberResponse;
		onchanged?: () => void;
	}
	let { orgSlug, member, onchanged }: Props = $props();

	let editing = $state(false);
	let value = $state('');
	let saving = $state(false);
	let saveError = $state('');

	function startEdit() {
		value = member.badgeId ?? '';
		saveError = '';
		editing = true;
	}

	async function save(e: Event) {
		e.preventDefault();
		saving = true;
		saveError = '';
		try {
			await apiKeysApi.apiV1OrganizationsSlugMembersMemberIdBadgePut(orgSlug, member.id!, { badgeId: value.trim() || null });
			toast.success(value.trim() ? t('memberDetail.badge.saved') : t('memberDetail.badge.removed'));
			editing = false;
			onchanged?.();
		} catch (err) {
			saveError = extractErrorMessage(err, t('memberDetail.badge.saveFailed'));
		} finally {
			saving = false;
		}
	}
</script>

<section class="card bg-base-100 border border-base-300 p-5 mb-5">
	<div class="flex items-center justify-between gap-2 mb-2">
		<h2 class="text-base font-bold text-base-content m-0">{t('memberDetail.badge.title')}</h2>
		{#if !editing}
			<button type="button" class="btn btn-ghost btn-sm" onclick={startEdit}>{t('common.edit')}</button>
		{/if}
	</div>
	{#if editing}
		<form onsubmit={save} class="flex flex-col gap-2">
			{#if saveError}<div class="alert alert-error text-sm py-2">{saveError}</div>{/if}
			<label for="badge-{member.id}" class="text-sm font-medium text-base-content/80">{t('memberDetail.badge.label')}</label>
			<div class="flex items-center gap-2 flex-wrap">
				<input id="badge-{member.id}" bind:value maxlength="100" placeholder={t('memberDetail.badge.placeholder')} class="input input-bordered input-sm font-mono w-full sm:w-64" disabled={saving} />
				<button type="submit" class="btn btn-primary btn-sm" disabled={saving}>{saving ? t('common.saving') : t('common.save')}</button>
				<button type="button" class="btn btn-ghost btn-sm" onclick={() => (editing = false)} disabled={saving}>{t('common.cancel')}</button>
			</div>
		</form>
	{:else}
		<p class="text-base-content font-mono m-0 break-all">{member.badgeId ?? '—'}</p>
	{/if}
	<p class="text-base-content/50 text-xs mt-2 mb-0">{t('memberDetail.badge.hint')}</p>
</section>
