<script lang="ts">
	import { companyLeaveApi } from '$lib/apiClient';
	import { invalidateOrgDetail } from '$lib/stores/orgDetail';
	import { toast } from '$lib/stores/toast.svelte';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import ExclusionsDialog from '$lib/components/ExclusionsDialog.svelte';
	import { t, tp } from '$lib/i18n';
	import { formatDate } from '$lib/utils/formatters';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import type { CompanyLeaveResponse, GroupResponse } from '$lib/api';

	/**
	 * Betriebsurlaub: enforced vacation of the organization or a group. The server writes the vacation days (working days
	 * only, holidays skipped) and keeps them in step; this lists, creates, edits, deletes and sets exclusions.
	 */
	interface Props {
		slug: string;
		canEdit: boolean;
		groups: GroupResponse[];
		members: { id: number; name: string }[];
	}
	let { slug, canEdit, groups, members }: Props = $props();

	let leaves = $state<CompanyLeaveResponse[]>([]);
	let loadError = $state('');
	let showForm = $state(false);
	let editingId = $state<number | null>(null);
	let name = $state('');
	let from = $state('');
	let to = $state('');
	let group = $state('');
	let saving = $state(false);
	let formError = $state('');
	let submitted = $state(false);
	let excluding = $state<CompanyLeaveResponse | null>(null);
	let savingExclusions = $state(false);

	$effect(() => {
		void slug;
		load();
	});

	async function load() {
		loadError = '';
		try {
			leaves = (await companyLeaveApi.apiV1OrganizationsSlugCompanyLeavesGet(slug)).data;
		} catch (err) {
			loadError = extractErrorMessage(err, t('holidays.leave.loadFailed'));
		}
	}

	function openForm(l?: CompanyLeaveResponse) {
		editingId = l?.id ?? null;
		name = l?.name ?? '';
		from = l?.from ?? '';
		to = l?.to ?? '';
		group = l?.groupId != null ? String(l.groupId) : '';
		formError = '';
		submitted = false;
		showForm = true;
	}

	async function save(e: Event) {
		e.preventDefault();
		submitted = true;
		if (!name.trim() || !from || !to) return;
		saving = true;
		formError = '';
		const groupId = group ? Number(group) : null;
		try {
			invalidateOrgDetail(slug); // vacation figures change
			if (editingId == null) {
				await companyLeaveApi.apiV1OrganizationsSlugCompanyLeavesPost(slug, { name: name.trim(), from, to, groupId });
				toast.success(t('holidays.leave.added'));
			} else {
				await companyLeaveApi.apiV1OrganizationsSlugCompanyLeavesIdPut(slug, editingId,
					{ name: name.trim(), from, to, groupId, forWholeOrganization: groupId == null });
				toast.success(t('holidays.leave.saved'));
			}
			showForm = false;
			await load();
		} catch (err) {
			formError = extractErrorMessage(err, t(editingId == null ? 'holidays.leave.addFailed' : 'holidays.leave.saveFailed'));
		} finally {
			saving = false;
		}
	}

	async function remove(l: CompanyLeaveResponse) {
		if (!l.id) return;
		const ok = await confirmDialog({
			title: t('holidays.leave.deleteTitle'),
			message: t('holidays.leave.deleteMessage', { name: l.name ?? '' }),
			confirmLabel: t('common.delete'),
			danger: true
		});
		if (!ok) return;
		try {
			invalidateOrgDetail(slug);
			await companyLeaveApi.apiV1OrganizationsSlugCompanyLeavesIdDelete(slug, l.id);
			toast.success(t('holidays.leave.deleted'));
			await load();
		} catch (err) {
			toast.error(err, t('holidays.leave.deleteFailed'));
		}
	}

	async function saveExclusions(userIds: number[]) {
		if (!excluding?.id) return;
		savingExclusions = true;
		try {
			invalidateOrgDetail(slug);
			await companyLeaveApi.apiV1OrganizationsSlugCompanyLeavesIdExclusionsPut(slug, excluding.id, { userIds });
			toast.success(t('holidays.exclusions.saved'));
			excluding = null;
			await load();
		} catch (err) {
			toast.error(err, t('holidays.exclusions.failed'));
		} finally {
			savingExclusions = false;
		}
	}

	function groupName(id: number | null | undefined): string | null {
		return id == null ? null : groups.find((g) => g.id === id)?.name ?? null;
	}
</script>

<section class="mt-8" aria-labelledby="leave-title">
	<div class="flex items-center justify-between gap-3 mb-1 flex-wrap">
		<h2 id="leave-title" class="text-lg font-semibold m-0">{t('holidays.leave.title')}</h2>
		{#if canEdit}
			<button class="btn btn-primary btn-sm" onclick={() => (showForm && editingId == null ? (showForm = false) : openForm())} aria-expanded={showForm}>
				{showForm && editingId == null ? t('common.cancel') : t('holidays.leave.add')}
			</button>
		{/if}
	</div>
	<p class="text-sm text-base-content/70 mt-0 mb-3">{t('holidays.leave.intro')}</p>

	{#if loadError}
		<div class="alert alert-error mb-3 text-sm" role="alert"><span>{loadError}</span><button class="btn btn-sm" onclick={load}>{t('common.retry')}</button></div>
	{/if}

	{#if showForm && canEdit}
		<div class="bg-base-200/50 p-4 rounded-lg mb-4 border border-base-300">
			{#if formError}<div class="alert alert-error mb-3 text-sm" role="alert">{formError}</div>{/if}
			<form onsubmit={save} class="flex gap-3 items-end flex-wrap" novalidate>
				<div class="form-control w-full sm:w-auto">
					<label for="leaveName" class="label text-xs">{t('holidays.name')}</label>
					<input id="leaveName" type="text" class="input input-sm w-full" class:input-error={submitted && !name.trim()} bind:value={name} placeholder={t('holidays.leave.namePlaceholder')} disabled={saving} />
				</div>
				<div class="form-control">
					<label for="leaveFrom" class="label text-xs">{t('holidays.leave.from')}</label>
					<input id="leaveFrom" type="date" class="input input-sm" class:input-error={submitted && !from} bind:value={from} disabled={saving} />
				</div>
				<div class="form-control">
					<label for="leaveTo" class="label text-xs">{t('holidays.leave.to')}</label>
					<input id="leaveTo" type="date" class="input input-sm" class:input-error={submitted && !to} bind:value={to} min={from || undefined} disabled={saving} />
				</div>
				<div class="form-control w-full sm:w-auto">
					<label for="leaveGroup" class="label text-xs">{t('holidays.company.scope')}</label>
					<select id="leaveGroup" class="select select-sm w-full sm:w-56" bind:value={group} disabled={saving}>
						<option value="">{t('holidays.company.wholeOrg')}</option>
						{#each groups as g (g.id)}<option value={String(g.id)}>{g.name}</option>{/each}
					</select>
				</div>
				<div class="flex gap-1.5">
					<button type="submit" class="btn btn-primary btn-sm" disabled={saving}>{saving ? t('common.saving') : t('common.save')}</button>
					{#if editingId != null}<button type="button" class="btn btn-ghost btn-sm" onclick={() => (showForm = false)}>{t('common.cancel')}</button>{/if}
				</div>
			</form>
		</div>
	{/if}

	{#if leaves.length === 0 && !loadError}
		<p class="text-base-content/70 text-sm">{t('holidays.leave.empty')}</p>
	{:else}
		<ul class="bg-base-100 border border-base-300 rounded-xl overflow-hidden">
			{#each leaves as l (l.id)}
				<li class="flex items-center justify-between gap-2 p-3 border-b border-base-200 last:border-b-0 flex-wrap">
					<div class="flex items-center gap-x-3 gap-y-1 flex-wrap min-w-0">
						<span class="font-medium">{l.name}</span>
						<span class="text-base-content/70 text-sm tabular-nums">{formatDate(l.from ?? '')} – {formatDate(l.to ?? '')}</span>
						{#if groupName(l.groupId)}<span class="badge badge-sm badge-outline">{groupName(l.groupId)}</span>{/if}
						{#if (l.excludedUserIds?.length ?? 0) > 0}
							<span class="badge badge-sm badge-outline">{tp('holidays.company.excluded', l.excludedUserIds!.length)}</span>
						{/if}
					</div>
					{#if canEdit}
						<div class="flex items-center gap-1.5 ml-auto">
							<button class="btn btn-ghost btn-sm" onclick={() => (excluding = l)}>{t('holidays.company.exclude')}</button>
							<button class="btn btn-ghost btn-sm" onclick={() => openForm(l)}>{t('common.edit')}</button>
							<button class="btn btn-ghost btn-xs text-error" aria-label={t('holidays.leave.deleteLabel', { name: l.name ?? '' })}
								title={t('holidays.leave.deleteLabel', { name: l.name ?? '' })} onclick={() => remove(l)}>&times;</button>
						</div>
					{/if}
				</li>
			{/each}
		</ul>
	{/if}
</section>

<ExclusionsDialog open={excluding !== null} name={excluding?.name ?? ''} {members} selected={excluding?.excludedUserIds ?? []}
	saving={savingExclusions} onclose={() => (excluding = null)} onsave={saveExclusions} />
