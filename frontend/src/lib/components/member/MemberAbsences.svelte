<script lang="ts">
	import { untrack } from 'svelte';
	import { absenceDayApi } from '$lib/apiClient';
	import type { AbsenceDayResponse, AbsenceTypeDefinitionResponse } from '$lib/api';
	import AbsenceDialog from '$lib/components/AbsenceDialog.svelte';
	import { absenceTypeLabel, formatDateFull } from '$lib/utils/formatters';
	import { absenceTypeClasses } from '$lib/utils/colors';
	import { dateKey } from '$lib/utils/dateHelpers';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import { confirmDialog } from '$lib/stores/confirm.svelte';
	import { toast } from '$lib/stores/toast.svelte';

	/** One person's absences in an org, grouped by year, with add (AbsenceDialog) and delete. */
	interface Props {
		orgSlug: string;
		userId: number;
		title: string;
		/** Show "add" (AbsenceDialog decides admin endpoint / self endpoint / request). */
		canAdd: boolean;
		canDelete: boolean;
		/** Caller is org admin. */
		isAdmin: boolean;
		/** Caller's own absences need approval (absenceSelfServiceMode === 'RequiresApproval'). */
		mustRequest?: boolean;
		absenceTypes?: AbsenceTypeDefinitionResponse[];
		/** Show who created an entry / via which request. */
		showAudit?: boolean;
		/** After an add or delete (e.g. reload vacation balance). */
		onchanged?: () => void;
		class?: string;
	}
	let {
		orgSlug, userId, title, canAdd, canDelete, isAdmin, mustRequest = false,
		absenceTypes = [], showAudit = false, onchanged, class: className = ''
	}: Props = $props();

	let absences = $state<AbsenceDayResponse[]>([]);
	let loading = $state(true);
	let loadError = $state('');
	let dialogOpen = $state(false);
	let deletingId = $state<number | null>(null);

	$effect(() => {
		if (!orgSlug || !userId) return;
		untrack(() => load());
	});

	async function load() {
		loading = true;
		loadError = '';
		try {
			const { data } = await absenceDayApi.apiV1OrganizationsSlugAbsencesGet(orgSlug, userId, undefined, undefined, 500);
			absences = (data.items ?? []).filter((a) => a.userId === userId);
		} catch (err) {
			loadError = extractErrorMessage(err, 'Could not load absences.');
		} finally {
			loading = false;
		}
	}

	let byYear = $derived.by(() => {
		const groups = new Map<string, AbsenceDayResponse[]>();
		for (const a of [...absences].sort((x, y) => (y.date ?? '').localeCompare(x.date ?? ''))) {
			const year = a.date?.substring(0, 4) ?? '—';
			if (!groups.has(year)) groups.set(year, []);
			groups.get(year)!.push(a);
		}
		return [...groups.entries()];
	});

	function saved(requestMessage?: string) {
		if (requestMessage) toast.info(requestMessage);
		else toast.success('Absence added.');
		load();
		onchanged?.();
	}

	async function remove(a: AbsenceDayResponse) {
		if (!a.id) return;
		const ok = await confirmDialog({
			title: 'Delete absence?',
			message: `${formatDateFull(a.date!)} · ${a.customTypeName ?? absenceTypeLabel(a.type)}`,
			confirmLabel: 'Delete',
			danger: true
		});
		if (!ok) return;
		deletingId = a.id;
		try {
			await absenceDayApi.apiV1OrganizationsSlugAbsencesIdDelete(orgSlug, a.id);
			toast.success('Absence deleted.');
			await load();
			onchanged?.();
		} catch (err) {
			toast.error(err, 'Could not delete the absence.');
		} finally {
			deletingId = null;
		}
	}
</script>

<section class="card bg-base-100 border border-base-300 p-5 {className}">
	<div class="flex items-center justify-between gap-2 mb-3 flex-wrap">
		<h2 class="text-base font-bold text-base-content m-0">{title}</h2>
		{#if canAdd}
			<button type="button" class="btn btn-primary btn-sm" onclick={() => (dialogOpen = true)}>
				{mustRequest && !isAdmin ? '+ Request absence' : '+ Add absence'}
			</button>
		{/if}
	</div>

	{#if loading}
		<p class="text-sm text-base-content/50 flex items-center gap-2"><span class="loading loading-spinner loading-xs"></span> Loading…</p>
	{:else if loadError}
		<div class="alert alert-error text-sm py-2">
			<span>{loadError}</span>
			<button type="button" class="btn btn-ghost btn-xs" onclick={load}>Try again</button>
		</div>
	{:else if absences.length === 0}
		<p class="text-sm text-base-content/50">No absences recorded.</p>
	{:else}
		{#each byYear as [year, items] (year)}
			<div class="mb-4 last:mb-0">
				<h3 class="text-sm font-semibold text-base-content/50 mb-2">{year}</h3>
				<ul class="flex flex-col gap-1.5 list-none p-0 m-0">
					{#each items as a (a.id)}
						<li class="flex items-center gap-x-3 gap-y-1 flex-wrap py-2 px-3 bg-base-200/30 rounded-md border border-base-200 text-sm">
							<span class="font-medium text-base-content/70 min-w-[6.5rem]">{formatDateFull(a.date!)}</span>
							<span class="badge badge-sm {absenceTypeClasses(a.type).badge}">{a.customTypeName ?? absenceTypeLabel(a.type)}</span>
							{#if a.isHalfDay}<span class="badge badge-outline badge-xs">½ day</span>{/if}
							{#if a.note}<span class="text-base-content/50 text-xs italic flex-1 min-w-0 truncate">{a.note}</span>{/if}
							{#if showAudit && (a.createdByName || a.createdViaRequestId)}
								<span class="text-[10px] text-base-content/40 whitespace-nowrap">
									{a.createdByName ? `by ${a.createdByName}` : ''}{a.createdViaRequestId ? ` (request #${a.createdViaRequestId})` : ''}
								</span>
							{/if}
							{#if canDelete}
								<button type="button" class="btn btn-ghost btn-xs ml-auto text-base-content/40 hover:text-error" aria-label="Delete absence" title="Delete absence" onclick={() => remove(a)} disabled={deletingId === a.id}>
									{#if deletingId === a.id}<span class="loading loading-spinner loading-xs"></span>{:else}✕{/if}
								</button>
							{/if}
						</li>
					{/each}
				</ul>
			</div>
		{/each}
	{/if}
</section>

{#if canAdd}
	<AbsenceDialog
		open={dialogOpen}
		{orgSlug}
		date={dateKey(new Date())}
		fixedUserId={userId}
		{isAdmin}
		{mustRequest}
		{absenceTypes}
		onclose={() => (dialogOpen = false)}
		onsaved={saved}
	/>
{/if}
