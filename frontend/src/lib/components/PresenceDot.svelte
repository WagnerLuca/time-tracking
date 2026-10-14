<script lang="ts">
	import { t } from '$lib/i18n';

	/** Presence badge on an avatar: green while a timer runs; nothing when presence is not visible (null). */
	let { present }: { present: boolean | null | undefined } = $props();
</script>

{#if present != null}
	<span
		class="absolute -bottom-0.5 -right-0.5 w-3.5 h-3.5 rounded-full border-2 border-base-100 {present ? 'bg-success' : 'bg-base-300'}"
		role="img"
		aria-label={present ? t('visibility.present') : t('visibility.absent')}
		title={present ? t('visibility.present') : t('visibility.absent')}
	></span>
{/if}
