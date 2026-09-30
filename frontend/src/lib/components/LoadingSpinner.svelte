<script lang="ts">
	import { t } from '$lib/i18n';

	let { message, size = 'md' }: { message?: string; size?: 'sm' | 'md' | 'lg' } = $props();
</script>

<div class="flex flex-col items-center justify-center py-12 gap-3 text-base-content/60" role="status">
	<span class="loading loading-spinner loading-{size}" aria-hidden="true"></span>
	<p class="m-0">{message ?? t('common.loading')}</p>
</div>
