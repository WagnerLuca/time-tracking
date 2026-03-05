<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { organizationsApi } from '$lib/apiClient';
	import { goto } from '$app/navigation';
	import type { CreateOrganizationRequest } from '$lib/api';
	import { extractErrorMessage } from '$lib/utils/errorHandler';

	let name = $state('');
	let slug = $state('');
	let description = $state('');
	let website = $state('');
	let error = $state('');
	let submitting = $state(false);
	let autoSlug = $state(true);

	function generateSlug(text: string): string {
		return text
			.toLowerCase()
			.replace(/[^a-z0-9]+/g, '-')
			.replace(/^-|-$/g, '');
	}

	function handleNameInput() {
		if (autoSlug) {
			slug = generateSlug(name);
		}
	}

	function handleSlugInput() {
		autoSlug = false;
	}

	async function handleCreate(e: Event) {
		e.preventDefault();
		error = '';

		if (!name.trim() || !slug.trim()) {
			error = 'Name and slug are required.';
			return;
		}

		submitting = true;
		try {
			const payload: CreateOrganizationRequest = {
				name: name.trim(),
				slug: slug.trim(),
				description: description.trim() || undefined,
				website: website.trim() || undefined
			};
			const { data: created } = await organizationsApi.apiOrganizationsPost(payload);
			// Refresh org context so the new org appears immediately in the UI
			if (auth.user?.id) {
				await orgContext.loadOrganizations(auth.user.id);
				orgContext.select(created.id!);
			}
			goto(`/organizations/${created.slug}`);
		} catch (err) {
			error = extractErrorMessage(err, 'Failed to create organization.');
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head>
	<title>New Organization - Time Tracking</title>
</svelte:head>

<div class="page">
	<a href="/organizations" class="text-base-content/60 text-sm inline-block mb-3 no-underline hover:text-primary">&larr; Back to Organizations</a>
	<h1 class="text-2xl font-bold mb-6">Create Organization</h1>

	{#if error}
		<div class="alert alert-error text-sm mb-4">{error}</div>
	{/if}

	<form class="card bg-base-100 border border-base-300 max-w-xl" onsubmit={handleCreate}>
		<div class="card-body">
			<div class="mb-5">
				<label for="name" class="block font-medium mb-1.5 text-base-content/80 text-sm">Name <span class="text-error">*</span></label>
				<input
					id="name"
					type="text"
					class="input input-bordered w-full"
					bind:value={name}
					oninput={handleNameInput}
					placeholder="My Organization"
					required
					disabled={submitting}
				/>
			</div>

			<div class="mb-5">
				<label for="slug" class="block font-medium mb-1.5 text-base-content/80 text-sm">Slug <span class="text-error">*</span></label>
				<input
					id="slug"
					type="text"
					class="input input-bordered w-full"
					bind:value={slug}
					oninput={handleSlugInput}
					placeholder="my-organization"
					required
					disabled={submitting}
				/>
				<span class="block text-xs text-base-content/50 mt-1">Used in URLs. Only lowercase letters, numbers, and hyphens.</span>
			</div>

			<div class="mb-5">
				<label for="description" class="block font-medium mb-1.5 text-base-content/80 text-sm">Description</label>
				<textarea
					id="description"
					class="textarea textarea-bordered w-full"
					bind:value={description}
					placeholder="A short description of the organization..."
					rows="3"
					disabled={submitting}
				></textarea>
			</div>

			<div class="mb-5">
				<label for="website" class="block font-medium mb-1.5 text-base-content/80 text-sm">Website</label>
				<input
					id="website"
					type="url"
					class="input input-bordered w-full"
					bind:value={website}
					placeholder="https://example.com"
					disabled={submitting}
				/>
			</div>

			<div class="flex gap-3 justify-end mt-6">
				<a href="/organizations" class="btn btn-ghost border border-base-300">Cancel</a>
				<button type="submit" class="btn btn-primary" disabled={submitting}>
					{submitting ? 'Creating...' : 'Create Organization'}
				</button>
			</div>
		</div>
	</form>
</div>

