<script lang="ts">
	import { organizationsApi } from '$lib/apiClient';
	import type { OrganizationMemberResponse } from '$lib/api';
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';

	/** Grant or revoke the accounting role (owner: anyone; admins: other members — see canGrantAccountant). */
	interface Props {
		orgSlug: string;
		member: OrganizationMemberResponse;
		onchanged?: () => void;
	}
	let { orgSlug, member, onchanged }: Props = $props();

	let saving = $state(false);

	async function toggle(e: Event) {
		const input = e.currentTarget as HTMLInputElement;
		const value = input.checked;
		saving = true;
		try {
			await organizationsApi.apiV1OrganizationsSlugMembersMemberIdAccountantPut(orgSlug, member.id!, { isAccountant: value });
			toast.success(value ? t('accounting.member.granted') : t('accounting.member.revoked'));
			onchanged?.();
		} catch (err) {
			input.checked = !value;
			toast.error(err, t('accounting.member.failed'));
		} finally {
			saving = false;
		}
	}
</script>

<section class="card bg-base-100 border border-base-300 p-5 mb-5" aria-labelledby="member-accountant-{member.id}">
	<h2 id="member-accountant-{member.id}" class="text-base font-bold text-base-content m-0">{t('accounting.member.title')}</h2>
	<p class="text-sm text-base-content/70 mt-1 mb-3">{t('accounting.member.text')}</p>
	<label class="flex items-center gap-3 cursor-pointer w-fit">
		<input type="checkbox" class="toggle toggle-primary" checked={!!member.isAccountant} disabled={saving} onchange={toggle} data-testid="accountant-toggle" />
		<span class="text-sm font-medium">{t('accounting.member.toggle')}</span>
	</label>
</section>
