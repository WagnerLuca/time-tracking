<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { absenceDayApi, requestsApi } from '$lib/apiClient';
	import { absenceTypeLabel } from '$lib/utils/formatters';
	import { extractErrorMessage } from '$lib/utils/errorHandler';
	import type {
		AbsenceType,
		AbsenceDayResponse,
		AbsenceTypeDefinitionResponse,
		OrganizationMemberResponse
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

	let fromDate = $state('');
	let toDate = $state('');
	let selectedUserId = $state<number | null>(null);
	let typeValue = $state('1'); // '0'|'1'|'2' or 'c{id}' for custom types
	let note = $state('');
	let halfDay = $state(false);
	let saving = $state(false);
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
			errorMsg = '';
		}
	});

	let targetUserId = $derived(members ? selectedUserId : (fixedUserId ?? auth.user?.id ?? null));
	let targetIsSelf = $derived(targetUserId === auth.user?.id);
	/** Self-targeted adds under RequiresApproval become requests (admins bypass). */
	let willRequest = $derived(!isAdmin && targetIsSelf && mustRequest);

	function parseTypeSelection(value: string): { type: AbsenceType; customTypeId?: number } {
		if (value.startsWith('c')) return { type: 2 as AbsenceType, customTypeId: parseInt(value.slice(1), 10) };
		return { type: parseInt(value, 10) as AbsenceType };
	}

	function workdaysInRange(): string[] {
		const from = new Date(fromDate + 'T00:00:00');
		const to = toDate ? new Date(toDate + 'T00:00:00') : from;
		const days: string[] = [];
		const cursor = new Date(from);
		while (cursor <= to) {
			const dow = cursor.getDay();
			if (dow >= 1 && dow <= 5) {
				days.push(`${cursor.getFullYear()}-${String(cursor.getMonth() + 1).padStart(2, '0')}-${String(cursor.getDate()).padStart(2, '0')}`);
			}
			cursor.setDate(cursor.getDate() + 1);
		}
		return days;
	}

	async function submit(e: Event) {
		e.preventDefault();
		if (!fromDate || targetUserId == null) return;
		saving = true;
		errorMsg = '';
		try {
			const workdays = workdaysInRange();
			if (workdays.length === 0) {
				errorMsg = 'No workdays in selected range.';
				saving = false;
				return;
			}
			const { type, customTypeId } = parseTypeSelection(typeValue);

			if (willRequest) {
				await requestsApi.apiV1OrganizationsSlugRequestsPost(orgSlug, {
					type: 6 as any, // RequestAbsence
					message: note || undefined,
					requestData: JSON.stringify({ dates: workdays, type, customTypeId, isHalfDay: halfDay, note: note || undefined })
				});
				onsaved(`Absence request for ${workdays.length} day${workdays.length > 1 ? 's' : ''} sent for approval.`);
			} else {
				for (const dateStr of workdays) {
					if (isAdmin) {
						await absenceDayApi.apiV1OrganizationsSlugAbsencesAdminPost(orgSlug, {
							userId: targetUserId, date: dateStr, type, customTypeId, isHalfDay: halfDay, note: note || undefined
						});
					} else {
						await absenceDayApi.apiV1OrganizationsSlugAbsencesPost(orgSlug, {
							date: dateStr, type, customTypeId, isHalfDay: halfDay, note: note || undefined
						});
					}
				}
				onsaved();
			}
			onclose();
		} catch (err) {
			errorMsg = extractErrorMessage(err, willRequest ? 'Failed to send request.' : 'Failed to add absence.');
		} finally {
			saving = false;
		}
	}

	function absenceColor(type: string | null | undefined): string {
		switch (type) {
			case 'Vacation': return 'bg-info/20 text-info border-info/30';
			case 'SickDay': return 'bg-error/20 text-error border-error/30';
			default: return 'bg-warning/20 text-warning border-warning/30';
		}
	}
</script>

{#if open}
	<!-- svelte-ignore a11y_click_events_have_key_events -->
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div class="fixed inset-0 bg-black/40 z-50 flex items-center justify-center" onclick={onclose}>
		<!-- svelte-ignore a11y_click_events_have_key_events -->
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div class="bg-base-100 rounded-xl shadow-xl p-6 w-full max-w-md mx-4 border border-base-300 max-h-[90vh] overflow-y-auto" onclick={(e) => e.stopPropagation()}>
			<div class="flex items-center justify-between mb-4">
				<h3 class="text-lg font-bold text-base-content">
					{new Date(date + 'T00:00:00').toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' })}
				</h3>
				<button class="btn btn-ghost btn-sm btn-circle" onclick={onclose}>✕</button>
			</div>

			<!-- Existing absences on this day -->
			{#if dayAbsences.length > 0}
				<div class="mb-4">
					<h4 class="text-sm font-semibold text-base-content/60 mb-2">Absences on this day</h4>
					<div class="flex flex-col gap-1">
						{#each dayAbsences as ab}
							<div class="flex items-center gap-2 p-2 bg-base-200/50 rounded-lg text-sm">
								<div class="w-6 h-6 rounded-full bg-gradient-to-br from-primary to-secondary text-primary-content flex items-center justify-center text-[10px] font-semibold shrink-0">
									{(ab.userFirstName?.[0] ?? '').toUpperCase()}{(ab.userLastName?.[0] ?? '').toUpperCase()}
								</div>
								<span class="font-medium">{ab.userFirstName} {ab.userLastName}</span>
								<span class="badge badge-xs {absenceColor(ab.type)}">{ab.customTypeName ?? absenceTypeLabel(ab.type ?? '')}</span>
								{#if ab.isHalfDay}<span class="badge badge-xs badge-outline">½</span>{/if}
								{#if ab.note}<span class="text-base-content/40 text-xs truncate">{ab.note}</span>{/if}
							</div>
						{/each}
					</div>
				</div>
				<div class="divider my-2 text-xs text-base-content/40">{willRequest ? 'Request absence' : 'Add new absence'}</div>
			{/if}

			{#if willRequest}
				<div class="alert alert-info text-sm py-1.5 px-2.5 mb-3">
					This will be sent as a request and needs approval by your group admin or an organization admin.
				</div>
			{/if}
			{#if errorMsg}
				<div class="alert alert-error text-sm py-1.5 px-2.5 mb-3">{errorMsg}</div>
			{/if}

			<form onsubmit={submit} class="flex flex-col gap-3">
				{#if members}
					<div class="flex flex-col gap-1">
						<span class="text-sm font-medium text-base-content/70">Member</span>
						<select bind:value={selectedUserId} class="select select-bordered select-sm" required>
							<option value={null}>Select member...</option>
							{#each members as m}
								<option value={m.id}>{m.firstName} {m.lastName}{m.id === auth.user?.id ? ' (You)' : ''}</option>
							{/each}
						</select>
					</div>
				{/if}
				<div class="grid grid-cols-2 gap-3">
					<div class="flex flex-col gap-1">
						<span class="text-sm font-medium text-base-content/70">From</span>
						<input type="date" class="input input-bordered input-sm" bind:value={fromDate} required />
					</div>
					<div class="flex flex-col gap-1">
						<span class="text-sm font-medium text-base-content/70">To (optional)</span>
						<input type="date" class="input input-bordered input-sm" bind:value={toDate} min={fromDate} />
					</div>
				</div>
				<div class="flex flex-col gap-1">
					<span class="text-sm font-medium text-base-content/70">Type</span>
					<select bind:value={typeValue} class="select select-bordered select-sm">
						<option value="0">Sick Day</option>
						<option value="1">Vacation</option>
						<option value="2">Other</option>
						{#each absenceTypes as t}
							<option value={'c' + t.id}>{t.name}</option>
						{/each}
					</select>
					{#if typeValue.startsWith('c')}
						{@const selected = absenceTypes.find(t => 'c' + t.id === typeValue)}
						{#if selected}
							<span class="text-xs text-base-content/40">
								{selected.creditMode === 'FillToTarget' ? 'Hours are filled up to the expected daily target.'
								: selected.creditMode === 'FullCredit' ? 'The full daily target is credited.'
								: 'Hours are kept as tracked — no credit.'}
							</span>
						{/if}
					{/if}
				</div>
				<div class="flex flex-col gap-1">
					<span class="text-sm font-medium text-base-content/70">Note (optional)</span>
					<input type="text" class="input input-bordered input-sm" bind:value={note} placeholder="e.g. Doctor's appointment" />
				</div>
				<label class="label cursor-pointer flex items-center gap-2 text-sm">
					<input type="checkbox" class="checkbox checkbox-sm" bind:checked={halfDay} />
					Half day
				</label>
				<p class="text-xs text-base-content/40 m-0">Only workdays (Mon–Fri) in the range are counted.</p>
				<div class="flex justify-end gap-2 mt-2">
					<button type="button" class="btn btn-ghost btn-sm" onclick={onclose}>Cancel</button>
					<button type="submit" class="btn btn-primary btn-sm" disabled={saving}>
						{saving ? (willRequest ? 'Sending...' : 'Adding...') : willRequest ? 'Send Request' : 'Add Absence'}
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}
