<script lang="ts">
	import { locale, type Locale } from '$lib/stores/locale.svelte';
	import { t } from '$lib/i18n';

	/** DE/EN switch for the signed-out pages, which render without the app nav (and its toggle). */
	const LOCALES: Locale[] = ['de', 'en'];
</script>

<div class="join" role="group" aria-label={t('nav.switchLanguage')}>
	{#each LOCALES as l (l)}
		<button
			type="button"
			class="join-item btn btn-xs {locale.current === l ? 'btn-neutral' : 'btn-ghost'}"
			aria-pressed={locale.current === l}
			lang={l}
			onclick={() => locale.set(l)}
		>{l.toUpperCase()}</button>
	{/each}
</div>
