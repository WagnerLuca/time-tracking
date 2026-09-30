<script lang="ts">
	import type { EffectivePermissionsResponse } from '$lib/api';
	import { ruleModeLabel, visibilityModeLabel } from '$lib/utils/orgRules';

	/** Effective rights of a member (org rules with group overrides applied), read-only. */
	interface Props {
		orgSlug: string;
		perms: EffectivePermissionsResponse;
		isSelf: boolean;
		/** Caller is org admin → link to the groups page. */
		canManage: boolean;
	}
	let { orgSlug, perms, isSelf, canManage }: Props = $props();

	let rules = $derived([
		{ label: 'Edit past entries', mode: perms.editPastEntriesMode },
		{ label: 'Edit pause duration', mode: perms.editPauseMode },
		{ label: 'Absences & vacation', mode: perms.absenceSelfServiceMode },
		{ label: 'Change work schedule', mode: perms.workScheduleChangeMode },
		{ label: 'Set initial overtime', mode: perms.initialOvertimeMode },
		{ label: 'CSV import', mode: perms.csvImportMode }
	]);
	let visibility = $derived([
		{ label: 'Vacation visibility', mode: perms.vacationVisibility },
		{ label: 'Sick day visibility', mode: perms.sickDayVisibility },
		{ label: 'Time entry visibility', mode: perms.timeEntryVisibility }
	]);
	let capabilities = $derived([
		{ label: 'Approve absence requests', on: perms.canApproveAbsences },
		{ label: 'Approve time edits', on: perms.canApproveTimeEdits },
		{ label: 'Approve schedule changes', on: perms.canApproveScheduleChanges },
		{ label: 'Manage group members', on: perms.canManageGroupMembers },
		{ label: 'View all time entries', on: perms.canViewAllTimeEntries },
		{ label: 'Export data', on: perms.canExportData }
	]);

	function modeBadge(mode: string | null | undefined): string {
		return mode === 'Allowed' ? 'badge-success badge-outline' : mode === 'RequiresApproval' ? 'badge-warning badge-outline' : 'badge-ghost';
	}
</script>

<section class="card bg-base-100 border border-base-300 p-5 mb-5">
	<div class="flex items-center justify-between gap-2 mb-1 flex-wrap">
		<h2 class="text-base font-bold text-base-content m-0">{isSelf ? 'Your permissions' : 'Permissions'}</h2>
		{#if canManage}
			<a href="/organizations/{orgSlug}/groups" class="btn btn-ghost btn-xs">Manage via groups →</a>
		{/if}
	</div>
	<p class="text-xs text-base-content/50 mb-4">Effective rights from the organization rules and group overrides.</p>

	<div class="grid gap-x-8 gap-y-1.5 sm:grid-cols-2">
		<div>
			<h3 class="text-xs font-semibold uppercase tracking-wide text-base-content/40 mb-2">Actions</h3>
			{#each rules as r}
				<div class="flex items-center justify-between gap-2 py-1 text-sm">
					<span class="text-base-content/70">{r.label}</span>
					<span class="badge badge-sm shrink-0 {modeBadge(r.mode)}">{ruleModeLabel(r.mode)}</span>
				</div>
			{/each}
			<h3 class="text-xs font-semibold uppercase tracking-wide text-base-content/40 mb-2 mt-4">Visibility</h3>
			{#each visibility as v}
				<div class="flex items-center justify-between gap-2 py-1 text-sm">
					<span class="text-base-content/70">{v.label}</span>
					<span class="badge badge-sm badge-ghost shrink-0">{visibilityModeLabel(v.mode)}</span>
				</div>
			{/each}
		</div>
		<div>
			<h3 class="text-xs font-semibold uppercase tracking-wide text-base-content/40 mb-2">Approval & admin capabilities</h3>
			<ul class="list-none p-0 m-0">
				{#each capabilities as c}
					<li class="flex items-center gap-2 py-1 text-sm">
						{#if c.on}
							<svg class="text-success shrink-0" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" aria-hidden="true"><polyline points="20 6 9 17 4 12"/></svg>
						{:else}
							<svg class="text-base-content/20 shrink-0" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" aria-hidden="true"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
						{/if}
						<span class={c.on ? 'text-base-content/80' : 'text-base-content/40'}>{c.label}</span>
						<span class="sr-only">{c.on ? 'yes' : 'no'}</span>
					</li>
				{/each}
			</ul>
		</div>
	</div>
</section>
