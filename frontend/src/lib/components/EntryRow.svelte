<script lang="ts">
	import type { TimeEntryResponse } from '$lib/api';
	import type { EntryEditor } from '$lib/utils/entryEditor.svelte';
	import { timer } from '$lib/stores/timer.svelte';
	import { formatTime, formatDateShort, formatDuration, formatClock } from '$lib/utils/formatters';
	import EntryEditForm from './EntryEditForm.svelte';
	import { t } from '$lib/i18n';

	/**
	 * One time entry: time range, note, badges (org, running, needs correction, pause),
	 * duration and — when an editor is passed — edit / request / delete actions plus
	 * the inline edit form. Permissions are resolved by the caller (effective perms).
	 */
	interface Props {
		entry: TimeEntryResponse;
		/** Enables actions; omit for read-only rows (e.g. another member's entries). */
		editor?: EntryEditor;
		canEdit?: boolean;
		canRequestEdit?: boolean;
		canEditPause?: boolean;
		canRequestEditPause?: boolean;
		/** Show the entry's date under the time range. */
		showDate?: boolean;
		/** Show the organization badge. */
		showOrg?: boolean;
		compact?: boolean;
	}
	let {
		entry,
		editor,
		canEdit = false,
		canRequestEdit = false,
		canEditPause = false,
		canRequestEditPause = false,
		showDate = false,
		showOrg = false,
		compact = false
	}: Props = $props();

	const editing = $derived(!!editor && editor.editingEntryId === entry.id);
	const actionable = $derived(!!editor && !entry.isRunning);
	const pause = $derived(entry.pauseDurationMinutes ?? 0);
	const liveElapsed = $derived(entry.isRunning && timer.current?.id === entry.id ? formatClock(timer.elapsedSeconds) : null);
</script>

{#if editing && editor}
	<EntryEditForm
		bind:startTime={editor.editStartTime}
		bind:endTime={editor.editEndTime}
		bind:description={editor.editDescription}
		bind:pause={editor.editPause}
		error={editor.editError}
		saving={editor.editSaving}
		{canEditPause}
		onsave={() => editor.saveEditEntry(entry.id!)}
		oncancel={editor.cancelEditEntry}
	/>
{:else}
	<div class="flex flex-wrap items-center gap-x-3 gap-y-1.5 {compact ? 'px-3 py-2' : 'px-4 py-3'} {entry.isRunning ? 'bg-success/5' : ''}">
		<div class="min-w-0 flex-1 basis-48">
			<div class="flex flex-wrap items-baseline gap-x-2">
				<span class="text-sm font-medium text-base-content tabular-nums">
					{formatTime(entry.startTime!)} – {entry.endTime ? formatTime(entry.endTime) : '…'}
				</span>
				{#if showDate}
					<span class="text-xs text-base-content/50">{formatDateShort(entry.startTime!)}</span>
				{/if}
			</div>
			{#if entry.description}
				<div class="text-sm text-base-content/60 break-words">{entry.description}</div>
			{/if}
			<div class="flex flex-wrap gap-1 mt-1 empty:hidden">
				{#if showOrg && entry.organizationName}
					<span class="badge badge-info badge-sm">{entry.organizationName}</span>
				{/if}
				{#if entry.isRunning}
					<span class="badge badge-success badge-sm font-semibold">{t('entries.running')}</span>
				{/if}
				{#if entry.isFailure}
					<span class="badge badge-error badge-sm" title={t('entries.needsCorrectionHint')}>{t('entries.needsCorrection')}</span>
				{/if}
				{#if !entry.isRunning}
					{#if pause > 0}
						{#if actionable && canEditPause}
							<button type="button" class="badge badge-warning badge-sm badge-outline cursor-pointer hover:bg-warning/20" title={t('entries.editPause')} onclick={() => editor!.startEditEntry(entry)}>{t('entries.pauseBadge', { minutes: pause })} <span aria-hidden="true">✎</span></button>
						{:else if actionable && canRequestEditPause}
							<button type="button" class="badge badge-warning badge-sm badge-outline border-dashed cursor-pointer hover:bg-warning/10" title={t('entries.requestPauseEdit')} onclick={() => editor!.startRequest(entry.id!, 'pause')}>{t('entries.pauseBadge', { minutes: pause })} <span aria-hidden="true">✉</span></button>
						{:else}
							<span class="badge badge-warning badge-sm badge-outline">{t('entries.pauseBadge', { minutes: pause })}</span>
						{/if}
					{:else if actionable && canEditPause}
						<button type="button" class="badge badge-ghost badge-sm cursor-pointer hover:badge-warning hover:badge-outline" title={t('entries.addPause')} onclick={() => editor!.startEditEntry(entry)}>{t('entries.addPauseBadge')} <span aria-hidden="true">✎</span></button>
					{:else if actionable && canRequestEditPause}
						<button type="button" class="badge badge-ghost badge-sm cursor-pointer border-dashed border-warning hover:bg-warning/10" title={t('entries.requestPause')} onclick={() => editor!.startRequest(entry.id!, 'pause')}>{t('entries.addPauseBadge')} <span aria-hidden="true">✉</span></button>
					{/if}
				{/if}
			</div>
		</div>

		<div class="ml-auto flex items-center gap-1">
			<div class="text-right text-sm font-semibold text-base-content/70 tabular-nums min-w-14">
				{#if entry.isRunning}
					<span class="text-success">{liveElapsed ?? '…'}</span>
				{:else if entry.isFailure}
					<span class="text-base-content/40">–</span>
				{:else if pause > 0}
					<span class="block">{formatDuration(entry.netDurationMinutes)}</span>
					<span class="block text-xs text-base-content/40 font-normal">({formatDuration(entry.durationMinutes)})</span>
				{:else}
					{formatDuration(entry.durationMinutes)}
				{/if}
			</div>
			{#if actionable}
				{#if canEdit}
					<button type="button" class="btn btn-ghost btn-sm btn-square text-primary" title={t('entries.editEntry')} aria-label={t('entries.editEntry')} onclick={() => editor!.startEditEntry(entry)}>✎</button>
				{:else if canRequestEdit}
					<button type="button" class="btn btn-ghost btn-sm btn-square text-warning" title={t('entries.requestEdit')} aria-label={t('entries.requestEdit')} onclick={() => editor!.startRequest(entry.id!, 'edit')}>✉</button>
				{/if}
				<button type="button" class="btn btn-ghost btn-sm btn-square text-error" title={t('entries.deleteEntry')} aria-label={t('entries.deleteEntry')} onclick={() => editor!.deleteEntry(entry.id!)}>✕</button>
			{/if}
		</div>
	</div>
{/if}
