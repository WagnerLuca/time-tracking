<script lang="ts">
	import { t } from '$lib/i18n';

	/** Inline "decline with optional reason" step for request approvals; the reason goes to the member. */
	interface Props {
		busy?: boolean;
		onconfirm: (reason: string | undefined) => void;
		oncancel: () => void;
	}
	let { busy = false, onconfirm, oncancel }: Props = $props();

	const MAX = 1000;
	const id = $props.id();
	let reason = $state('');
	let textarea = $state<HTMLTextAreaElement>();
	$effect(() => textarea?.focus());

	function submit(e: SubmitEvent) {
		e.preventDefault();
		onconfirm(reason.trim() || undefined);
	}
</script>

<form class="flex flex-col gap-2 w-full" onsubmit={submit}>
	<label for="{id}-reason" class="text-sm font-medium text-base-content/80">{t('common.declineReason')} ({t('common.optional')})</label>
	<textarea
		bind:this={textarea}
		id="{id}-reason"
		class="textarea w-full h-20"
		maxlength={MAX}
		placeholder={t('common.declineReasonPlaceholder')}
		bind:value={reason}
		disabled={busy}
		aria-describedby="{id}-hint"
	></textarea>
	<p id="{id}-hint" class="text-xs text-base-content/50 m-0">{t('common.declineReasonHint')}</p>
	<div class="flex gap-2 justify-end flex-wrap">
		<button type="button" class="btn btn-ghost btn-sm" onclick={oncancel} disabled={busy}>{t('common.cancel')}</button>
		<button type="submit" class="btn btn-error btn-sm" disabled={busy}>{t('common.declineConfirm')}</button>
	</div>
</form>
