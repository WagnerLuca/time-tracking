<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { organizationsApi } from '$lib/apiClient';
	import { goto } from '$app/navigation';
	import { toast } from '$lib/stores/toast.svelte';
	import { t } from '$lib/i18n';
	import type { CreateOrganizationRequest } from '$lib/api';
	import { extractErrorMessage } from '$lib/utils/errorHandler';

	let name = $state('');
	let slug = $state('');
	let description = $state('');
	let website = $state('');
	let error = $state('');
	let submitting = $state(false);
	let attempted = $state(false);
	let autoSlug = $state(true);

	/** Same pattern the backend validates (CreateOrganizationRequest.Slug). */
	const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

	function generateSlug(text: string): string {
		return text
			.toLowerCase()
			.replace(/ä/g, 'ae').replace(/ö/g, 'oe').replace(/ü/g, 'ue').replace(/ß/g, 'ss')
			.normalize('NFD').replace(/[̀-ͯ]/g, '')
			.replace(/[^a-z0-9]+/g, '-')
			.replace(/^-|-$/g, '')
			.slice(0, 100)
			.replace(/-$/, '');
	}

	// The slug follows the name until the user edits it; clearing it re-enables that
	const effectiveSlug = $derived(slug.trim() || generateSlug(name));
	const nameError = $derived(attempted && !name.trim());
	const slugError = $derived((attempted || !autoSlug) && effectiveSlug !== '' && !SLUG_PATTERN.test(effectiveSlug));
	const slugMissing = $derived(attempted && !!name.trim() && !effectiveSlug);
	const websiteError = $derived(attempted && !!website.trim() && !/^https?:\/\/\S+\.\S+/.test(website.trim()));

	function handleNameInput() {
		if (autoSlug) {
			slug = generateSlug(name);
		}
	}

	function handleSlugInput() {
		autoSlug = slug.trim() === '';
	}

	async function handleCreate(e: Event) {
		e.preventDefault();
		error = '';
		attempted = true;

		if (!name.trim() || !effectiveSlug || !SLUG_PATTERN.test(effectiveSlug) || websiteError) return;

		submitting = true;
		try {
			const payload: CreateOrganizationRequest = {
				name: name.trim(),
				slug: effectiveSlug,
				description: description.trim() || undefined,
				website: website.trim() || undefined
			};
			const { data: created } = await organizationsApi.apiV1OrganizationsPost(payload);
			// Refresh org context so the new org appears immediately in the UI
			if (auth.user?.id) {
				await orgContext.loadOrganizations(auth.user.id);
				orgContext.select(created.id!);
			}
			toast.success(t('orgs.create.created', { name: created.name ?? '' }));
			goto(`/organizations/${created.slug}`);
		} catch (err) {
			error = extractErrorMessage(err, t('orgs.create.failed'));
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head>
	<title>{t('common.pageTitle', { page: t('orgs.create.pageTitle') })}</title>
</svelte:head>

<div class="page">
	<a href="/organizations" class="text-base-content/60 text-sm inline-block mb-3 no-underline hover:text-primary">{t('orgs.create.back')}</a>
	<h1 class="text-2xl font-bold mb-6">{t('orgs.create.title')}</h1>

	{#if error}
		<div class="alert alert-error text-sm mb-4 max-w-xl" role="alert">{error}</div>
	{/if}

	<form class="card bg-base-100 border border-base-300 max-w-xl" onsubmit={handleCreate} novalidate>
		<div class="card-body">
			<div class="mb-5">
				<label for="name" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('orgs.create.name')} <span class="text-error" aria-hidden="true">*</span></label>
				<input
					id="name"
					type="text"
					class="input w-full"
					class:input-error={nameError}
					bind:value={name}
					oninput={handleNameInput}
					placeholder={t('orgs.create.namePlaceholder')}
					maxlength="200"
					required
					aria-invalid={nameError}
					aria-describedby={nameError ? 'nameError' : undefined}
					disabled={submitting}
				/>
				{#if nameError}
					<p id="nameError" class="text-xs text-error mt-1">{t('orgs.create.nameRequired')}</p>
				{/if}
			</div>

			<div class="mb-5">
				<label for="slug" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('orgs.create.slug')}</label>
				<input
					id="slug"
					type="text"
					class="input w-full"
					class:input-error={slugError || slugMissing}
					bind:value={slug}
					oninput={handleSlugInput}
					placeholder={generateSlug(name) || t('orgs.join.placeholder')}
					maxlength="100"
					autocapitalize="none"
					spellcheck="false"
					aria-invalid={slugError || slugMissing}
					aria-describedby="slugHint"
					disabled={submitting}
				/>
				<span id="slugHint" class="block text-xs mt-1 {slugError || slugMissing ? 'text-error' : 'text-base-content/60'}">
					{slugMissing ? t('orgs.create.slugMissing') : t('orgs.create.slugHint')}
				</span>
			</div>

			<div class="mb-5">
				<label for="description" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('orgs.create.description')} <span class="text-base-content/50 font-normal">({t('common.optional')})</span></label>
				<textarea
					id="description"
					class="textarea w-full"
					bind:value={description}
					placeholder={t('orgs.create.descriptionPlaceholder')}
					rows="3"
					disabled={submitting}
				></textarea>
			</div>

			<div class="mb-5">
				<label for="website" class="block font-medium mb-1.5 text-base-content/80 text-sm">{t('orgs.create.website')} <span class="text-base-content/50 font-normal">({t('common.optional')})</span></label>
				<input
					id="website"
					type="url"
					class="input w-full"
					bind:value={website}
					placeholder="https://example.com"
					class:input-error={websiteError}
					aria-invalid={websiteError}
					aria-describedby={websiteError ? 'websiteError' : undefined}
					disabled={submitting}
				/>
				{#if websiteError}
					<p id="websiteError" class="text-xs text-error mt-1">{t('orgs.create.websiteInvalid')}</p>
				{/if}
			</div>

			<div class="flex flex-wrap gap-3 justify-end mt-6">
				<a href="/organizations" class="btn btn-ghost border border-base-300">{t('common.cancel')}</a>
				<button type="submit" class="btn btn-primary" disabled={submitting}>
					{submitting ? t('common.creating') : t('orgs.create.submit')}
				</button>
			</div>
		</div>
	</form>
</div>

