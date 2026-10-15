<script lang="ts">
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { getOrgDetail, invalidateOrgDetail } from '$lib/stores/orgDetail';
	import { toast } from '$lib/stores/toast.svelte';
	import { organizationsApi } from '$lib/apiClient';
	import { extractErrorMessage, getErrorStatus } from '$lib/utils/errorHandler';
	import { isOrgAdmin } from '$lib/utils/orgRules';
	import { t } from '$lib/i18n';
	import type { OrganizationDetailResponse } from '$lib/api';
	import Skeleton from '$lib/components/Skeleton.svelte';
	import ClosedPeriodsPanel from '$lib/components/ClosedPeriodsPanel.svelte';

	/** "Allgemein" (admins only): the organization's profile, time reference and the danger zone. */
	const orgSlug = $derived(page.params.slug ?? '');
	let org = $state<OrganizationDetailResponse | null>(null);
	let error = $state('');

	const myRole = $derived(org?.members?.find((m) => m.id === auth.user?.id)?.role ?? null);
	const isOwner = $derived(myRole === 'Owner');

	let name = $state('');
	let slug = $state('');
	let description = $state('');
	let website = $state('');
	let timeZone = $state('Europe/Berlin');
	let businessDayEnd = $state('00:00');
	let submitted = $state(false);
	let saving = $state(false);
	let saveError = $state('');
	const nameError = $derived(submitted && !name.trim() ? t('orgHome.edit.nameRequired') : '');
	const slugError = $derived(
		!submitted ? '' : !slug.trim() ? t('orgHome.edit.slugRequired') : !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(slug.trim()) ? t('orgHome.edit.slugInvalid') : ''
	);
	const timeZones = Intl.supportedValuesOf('timeZone');

	let confirmName = $state('');
	let deleting = $state(false);
	const nameMatches = $derived(!!org?.name && confirmName.trim() === org.name);

	$effect(() => {
		const s = orgSlug;
		untrack(() => load(s));
	});

	async function load(s: string) {
		org = null;
		error = '';
		try {
			const data = await getOrgDetail(s);
			// Members never see this section; send them to their own page in the org
			if (!isOrgAdmin(data.members?.find((m) => m.id === auth.user?.id)?.role)) {
				await goto(`/organizations/${s}`, { replaceState: true });
				return;
			}
			org = data;
			name = data.name ?? '';
			slug = data.slug ?? '';
			description = data.description ?? '';
			website = data.website ?? '';
			timeZone = data.timeZone ?? 'Europe/Berlin';
			businessDayEnd = data.businessDayEnd ?? '00:00';
			submitted = false;
			saveError = '';
			confirmName = '';
		} catch (err) {
			error = getErrorStatus(err) === 404 ? t('orgHome.notFound') : extractErrorMessage(err, t('orgHome.loadFailed'));
		}
	}

	async function save(e: Event) {
		e.preventDefault();
		submitted = true;
		if (nameError || slugError || !org) return;
		saving = true;
		saveError = '';
		const newSlug = slug.trim();
		try {
			// Time settings under the current slug first; a slug change comes last
			if (timeZone !== org.timeZone || businessDayEnd !== org.businessDayEnd)
				await organizationsApi.apiV1OrganizationsSlugSettingsPut(orgSlug, { timeZone, businessDayEnd });
			await organizationsApi.apiV1OrganizationsSlugPut(orgSlug, {
				name: name.trim(),
				description: description.trim(),
				slug: newSlug,
				website: website.trim() || undefined
			});
			invalidateOrgDetail(orgSlug);
			toast.success(t('orgHome.edit.saved'));
			// The org switcher and the section tabs look the org up by slug and name
			if (auth.user?.id) await orgContext.loadOrganizations(auth.user.id);
			if (newSlug !== orgSlug) await goto(`/organizations/${newSlug}/general`, { replaceState: true });
			else await load(orgSlug);
		} catch (err) {
			saveError = extractErrorMessage(err, t('orgHome.edit.saveFailed'));
		} finally {
			saving = false;
		}
	}

	async function deleteOrg(e: Event) {
		e.preventDefault();
		if (!nameMatches) return;
		deleting = true;
		try {
			await organizationsApi.apiV1OrganizationsSlugDelete(orgSlug);
			invalidateOrgDetail(orgSlug);
			toast.success(t('orgHome.delete.deleted'));
			if (auth.user?.id) await orgContext.loadOrganizations(auth.user.id);
			await goto('/organizations');
		} catch (err) {
			toast.error(err, t('orgHome.delete.failed'));
			deleting = false;
		}
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: `${t('orgHome.general.title')} – ${org?.name ?? t('orgHome.fallbackName')}` })}</title>
</svelte:head>

<div class="max-w-5xl mx-auto sm:px-6 pb-6">
	{#if error}
		<div class="alert alert-error" role="alert">{error}</div>
	{:else if !org}
		<Skeleton />
	{:else}
		<h1 class="text-2xl font-bold text-base-content mb-1">{t('orgHome.general.title')}</h1>
		<p class="text-sm text-base-content/70 mt-0 mb-5">{t('orgHome.general.intro')}</p>

		<form onsubmit={save} novalidate class="flex flex-col gap-6 max-w-2xl">
			{#if saveError}<div class="alert alert-error text-sm" role="alert">{saveError}</div>{/if}

			<section class="card bg-base-100 border border-base-300 p-4 sm:p-5 flex flex-col gap-4" aria-labelledby="general-profile">
				<h2 id="general-profile" class="text-base font-semibold m-0">{t('orgHome.general.profile')}</h2>
				<div>
					<label for="org-name" class="label text-sm font-medium text-base-content/80">{t('orgHome.edit.name')}</label>
					<input id="org-name" type="text" class="input w-full" class:input-error={nameError} aria-invalid={!!nameError} aria-describedby={nameError ? 'org-name-error' : undefined} bind:value={name} disabled={saving} />
					{#if nameError}<p id="org-name-error" class="text-error text-xs mt-1 mb-0">{nameError}</p>{/if}
				</div>
				<div>
					<label for="org-slug" class="label text-sm font-medium text-base-content/80">{t('orgHome.edit.slug')}</label>
					<input id="org-slug" type="text" class="input w-full" class:input-error={slugError} aria-invalid={!!slugError} aria-describedby={slugError ? 'org-slug-error' : undefined} bind:value={slug} disabled={saving} />
					{#if slugError}<p id="org-slug-error" class="text-error text-xs mt-1 mb-0">{slugError}</p>{/if}
				</div>
				<div>
					<label for="org-description" class="label text-sm font-medium text-base-content/80">{t('orgHome.edit.description')}</label>
					<textarea id="org-description" class="textarea w-full" rows="3" bind:value={description} disabled={saving}></textarea>
				</div>
				<div>
					<label for="org-website" class="label text-sm font-medium text-base-content/80">{t('orgHome.edit.website')}</label>
					<input id="org-website" type="url" class="input w-full" placeholder="https://" bind:value={website} disabled={saving} />
				</div>
			</section>

			<section class="card bg-base-100 border border-base-300 p-4 sm:p-5 flex flex-col gap-4" aria-labelledby="general-time">
				<div>
					<h2 id="general-time" class="text-base font-semibold m-0">{t('orgHome.general.time')}</h2>
					<p class="text-sm text-base-content/70 mt-1 mb-0">{t('orgHome.general.timeIntro')}</p>
				</div>
				<div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
					<div class="min-w-0">
						<label for="org-timezone" class="label text-sm font-medium text-base-content/80">{t('orgSettings.businessDayEnd.timeZone')}</label>
						<select id="org-timezone" class="select w-full" bind:value={timeZone} disabled={saving}>
							{#each timeZones as tz (tz)}<option value={tz}>{tz}</option>{/each}
						</select>
					</div>
					<div>
						<label for="org-day-end" class="label text-sm font-medium text-base-content/80">{t('orgSettings.businessDayEnd.label')}</label>
						<input id="org-day-end" type="time" class="input w-full" bind:value={businessDayEnd} required disabled={saving} aria-describedby="org-day-end-hint" />
					</div>
				</div>
				<p id="org-day-end-hint" class="text-xs text-base-content/70 m-0">{t('orgSettings.businessDayEnd.description')}</p>
			</section>

			<div>
				<button type="submit" class="btn btn-primary max-sm:w-full" disabled={saving}>{saving ? t('common.saving') : t('common.saveChanges')}</button>
			</div>
		</form>

		<ClosedPeriodsPanel {orgSlug} />

		<section class="card border border-error/50 bg-error/5 p-4 sm:p-5 mt-8 max-w-2xl" aria-labelledby="general-danger" data-testid="danger-zone">
			<h2 id="general-danger" class="text-base font-semibold text-error m-0">{t('orgHome.general.danger')}</h2>
			<p class="text-sm text-base-content/80 mt-1">{t('orgHome.general.deleteIntro')}</p>
			{#if isOwner}
				<form onsubmit={deleteOrg} novalidate class="flex flex-col gap-2 mt-2">
					<label for="org-confirm-name" class="text-sm font-medium text-base-content/80">{t('orgHome.general.typeName', { name: org.name ?? '' })}</label>
					<input id="org-confirm-name" type="text" class="input w-full sm:max-w-sm" autocomplete="off" bind:value={confirmName} disabled={deleting} />
					<div>
						<button type="submit" class="btn btn-error max-sm:w-full" disabled={!nameMatches || deleting}>{deleting ? t('common.deleting') : t('orgHome.delete.confirm')}</button>
					</div>
				</form>
			{:else}
				<p class="text-sm text-base-content/70 m-0">{t('orgHome.general.ownerOnly')}</p>
			{/if}
		</section>
	{/if}
</div>
