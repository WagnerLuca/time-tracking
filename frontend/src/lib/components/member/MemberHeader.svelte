<script lang="ts">
	import { goto } from '$app/navigation';
	import { organizationsApi } from '$lib/apiClient';
	import type { OrganizationMemberResponse, OrganizationRole } from '$lib/api';
	import GroupTags from '$lib/components/GroupTags.svelte';
	import { formatDateFull, getInitials, roleLabel } from '$lib/utils/formatters';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';

	/** Avatar, name, role and groups of a member; admins change the role or remove the member. */
	interface Props {
		orgSlug: string;
		member: OrganizationMemberResponse;
		isSelf: boolean;
		/** Caller is org admin (and not looking at themselves) → role select + remove. */
		canManage: boolean;
		callerIsOwner: boolean;
		onchanged?: () => void;
	}
	let { orgSlug, member, isSelf, canManage, callerIsOwner, onchanged }: Props = $props();

	const ROLES = ['Member', 'Admin', 'Owner'] as const;
	let pending = $state(false);
	let name = $derived(`${member.firstName ?? ''} ${member.lastName ?? ''}`.trim());
	let roleValue = $derived(Math.max(0, ROLES.indexOf((member.role ?? 'Member') as (typeof ROLES)[number])));

	function roleBadge(role: string | null | undefined): string {
		return role === 'Owner' ? 'badge-warning' : role === 'Admin' ? 'badge-info' : 'badge-ghost';
	}

	async function changeRole(e: Event & { currentTarget: HTMLSelectElement }) {
		const select = e.currentTarget;
		const newRole = parseInt(select.value, 10);
		pending = true;
		try {
			await organizationsApi.apiV1OrganizationsSlugMembersMemberIdPut(orgSlug, member.id!, { role: newRole as OrganizationRole });
			toast.success(t('memberDetail.header.roleChanged', { role: roleLabel(ROLES[newRole]) }));
			onchanged?.();
		} catch (err) {
			select.value = String(roleValue);
			toast.error(err, t('memberDetail.header.roleFailed'));
		} finally {
			pending = false;
		}
	}

	async function remove() {
		const ok = await confirmDialog({
			title: t('memberDetail.header.removeTitle', { name }),
			message: t('memberDetail.header.removeMessage'),
			confirmLabel: t('memberDetail.header.remove'),
			danger: true
		});
		if (!ok) return;
		pending = true;
		try {
			await organizationsApi.apiV1OrganizationsSlugMembersMemberIdDelete(orgSlug, member.id!);
			toast.success(t('memberDetail.header.removed', { name }));
			goto(`/organizations/${orgSlug}/members`);
		} catch (err) {
			toast.error(err, t('memberDetail.header.removeFailed'));
			pending = false;
		}
	}
</script>

<div class="flex flex-col sm:flex-row sm:items-center gap-4 mb-6 pb-6 border-b border-base-300">
	<div class="flex items-center gap-4 flex-1 min-w-0">
		<div class="w-14 h-14 sm:w-16 sm:h-16 rounded-full bg-gradient-to-br from-primary to-secondary text-primary-content flex items-center justify-center text-xl font-bold shrink-0" aria-hidden="true">
			{getInitials(member.firstName, member.lastName)}
		</div>
		<div class="min-w-0">
			<h1 class="text-2xl font-bold text-base-content m-0 break-words">{name}</h1>
			<p class="text-base-content/60 text-sm mt-0.5 mb-2 break-all">{member.email}</p>
			<div class="flex items-center gap-2 flex-wrap">
				<span class="badge badge-sm uppercase tracking-wide {roleBadge(member.role)}">{roleLabel(member.role)}</span>
				<GroupTags groups={member.groups} size="sm" />
				{#if member.joinedAt}
					<span class="text-base-content/40 text-xs">{t('memberDetail.header.joined', { date: formatDateFull(member.joinedAt) })}</span>
				{/if}
				{#if isSelf}<span class="badge badge-success badge-xs font-semibold">{t('memberDetail.header.you')}</span>{/if}
			</div>
		</div>
	</div>

	{#if canManage}
		<div class="flex items-center gap-2 shrink-0">
			<label for="member-role" class="sr-only">{t('memberDetail.header.role')}</label>
			<select id="member-role" class="select select-bordered select-sm" value={roleValue} onchange={changeRole} disabled={pending}>
				<option value={0}>{roleLabel('Member')}</option>
				<option value={1}>{roleLabel('Admin')}</option>
				{#if callerIsOwner}<option value={2}>{roleLabel('Owner')}</option>{/if}
			</select>
			<button type="button" class="btn btn-outline btn-error btn-sm" onclick={remove} disabled={pending}>{t('memberDetail.header.remove')}</button>
		</div>
	{/if}
</div>
