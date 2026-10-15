<script lang="ts">
	/** Settings → App (issue #67): install the PWA, push on this device, push categories. */
	import { onMount } from 'svelte';
	import { auth } from '$lib/stores/auth.svelte';
	import { locale } from '$lib/stores/locale.svelte';
	import { toast } from '$lib/stores/toast.svelte';
	import { install } from '$lib/pwa/install.svelte';
	import { disablePush, enablePush, pushState, type PushState } from '$lib/pwa/push';
	import { pushApi } from '$lib/apiClient';
	import type { PushSettingsDto } from '$lib/api';
	import { t } from '$lib/i18n';

	let pushStatus = $state<PushState | null>(null);
	let busy = $state(false);
	let categories = $state<PushSettingsDto | null>(null);
	const CATEGORY_KEYS = ['reminders', 'requests', 'coreHours', 'organization'] as const;

	onMount(() => {
		pushState(auth.user?.id).then((s) => (pushStatus = s), () => (pushStatus = 'unsupported'));
	});

	$effect(() => {
		if (pushStatus === 'on' && !categories)
			pushApi.apiV1PushSettingsGet().then(({ data }) => (categories = data), (err) => toast.error(err, t('pwa.push.saveFailed')));
	});

	async function enable() {
		if (!auth.user?.id) return;
		busy = true;
		try {
			pushStatus = await enablePush(auth.user.id, locale.current);
			if (pushStatus === 'on') toast.success(t('pwa.push.enabled'));
		} catch (err) {
			toast.error(err, t('pwa.push.failed'));
		} finally {
			busy = false;
		}
	}

	async function disable() {
		busy = true;
		try {
			await disablePush();
			pushStatus = 'off';
			toast.success(t('pwa.push.disabled'));
		} catch (err) {
			toast.error(err, t('pwa.push.failed'));
		} finally {
			busy = false;
		}
	}

	async function toggleCategory(key: (typeof CATEGORY_KEYS)[number], on: boolean) {
		if (!categories) return;
		const previous = categories;
		categories = { ...categories, [key]: on };
		try {
			categories = (await pushApi.apiV1PushSettingsPut(categories)).data;
		} catch (err) {
			categories = previous;
			toast.error(err, t('pwa.push.saveFailed'));
		}
	}
</script>

<section id="app" class="card bg-base-100 border border-base-300 shadow-sm mb-5 scroll-mt-20">
	<div class="card-body gap-3">
		<h2 class="card-title text-base">{t('pwa.install.title')}</h2>
		{#if install.installed}
			<p class="text-sm text-base-content/70">{t('pwa.install.installed')}</p>
		{:else}
			<p class="text-sm text-base-content/70">{t('pwa.install.text')}</p>
			{#if install.canPrompt}
				<div><button type="button" class="btn btn-primary btn-sm" onclick={() => install.prompt()}>{t('pwa.install.button')}</button></div>
			{:else}
				<p class="text-sm" data-testid="install-hint">{install.ios ? t('pwa.install.ios') : t('pwa.install.manual')}</p>
			{/if}
		{/if}
	</div>
</section>

<section id="push" class="card bg-base-100 border border-base-300 shadow-sm mb-5 scroll-mt-20" data-testid="push-settings">
	<div class="card-body gap-3">
		<h2 class="card-title text-base">{t('pwa.push.title')}</h2>
		<p class="text-sm text-base-content/70">{t('pwa.push.text')}</p>
		{#if pushStatus === 'unsupported'}
			<p class="text-sm">{t('pwa.push.unsupported')}</p>
		{:else if pushStatus === 'denied'}
			<p class="text-sm">{t('pwa.push.denied')}</p>
		{:else if pushStatus === 'off'}
			<div><button type="button" class="btn btn-primary btn-sm" onclick={enable} disabled={busy}>{t('pwa.push.enable')}</button></div>
		{:else if pushStatus === 'on'}
			<div class="flex flex-wrap items-center gap-3">
				<span class="text-sm">{t('pwa.push.on')}</span>
				<button type="button" class="btn btn-ghost btn-sm border-base-300" onclick={disable} disabled={busy}>{t('pwa.push.disable')}</button>
			</div>
			{#if categories}
				<fieldset class="mt-1">
					<legend class="text-sm font-medium mb-2">{t('pwa.push.categories')}</legend>
					<ul class="flex flex-col gap-2">
						{#each CATEGORY_KEYS as key (key)}
							<li>
								<label class="flex items-start gap-3 cursor-pointer">
									<input type="checkbox" class="toggle toggle-primary mt-0.5" checked={categories[key]} onchange={(e) => toggleCategory(key, e.currentTarget.checked)} />
									<span class="text-sm">
										<span class="font-medium">{t(`pwa.push.${key}`)}</span>
										<span class="block text-xs text-base-content/70">{t(`pwa.push.${key}Hint`)}</span>
									</span>
								</label>
							</li>
						{/each}
					</ul>
				</fieldset>
			{/if}
		{/if}
	</div>
</section>
