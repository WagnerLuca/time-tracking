<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { apiService } from '$lib/apiService';
	import { goto } from '$app/navigation';
	import type { CreateOrganizationRequest } from '$lib/types';

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
			const created = await apiService.post<any>('/api/Organizations', payload);
			goto(`/organizations/${created.id}`);
		} catch (err: any) {
			error = err.response?.data?.message || 'Failed to create organization.';
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head>
	<title>New Organization - Time Tracking</title>
</svelte:head>

<div class="page">
	<a href="/organizations" class="back-link">&larr; Back to Organizations</a>
	<h1>Create Organization</h1>

	{#if error}
		<div class="error-banner">{error}</div>
	{/if}

	<form class="form-card" onsubmit={handleCreate}>
		<div class="field">
			<label for="name">Name <span class="required">*</span></label>
			<input
				id="name"
				type="text"
				bind:value={name}
				oninput={handleNameInput}
				placeholder="My Organization"
				required
				disabled={submitting}
			/>
		</div>

		<div class="field">
			<label for="slug">Slug <span class="required">*</span></label>
			<input
				id="slug"
				type="text"
				bind:value={slug}
				oninput={handleSlugInput}
				placeholder="my-organization"
				required
				disabled={submitting}
			/>
			<span class="hint">Used in URLs. Only lowercase letters, numbers, and hyphens.</span>
		</div>

		<div class="field">
			<label for="description">Description</label>
			<textarea
				id="description"
				bind:value={description}
				placeholder="A short description of the organization..."
				rows="3"
				disabled={submitting}
			></textarea>
		</div>

		<div class="field">
			<label for="website">Website</label>
			<input
				id="website"
				type="url"
				bind:value={website}
				placeholder="https://example.com"
				disabled={submitting}
			/>
		</div>

		<div class="form-actions">
			<a href="/organizations" class="btn-secondary">Cancel</a>
			<button type="submit" class="btn-primary" disabled={submitting}>
				{submitting ? 'Creating...' : 'Create Organization'}
			</button>
		</div>
	</form>
</div>

<style>
	.back-link {
		color: #6b7280;
		text-decoration: none;
		font-size: 0.875rem;
		display: inline-block;
		margin-bottom: 0.75rem;
	}

	.back-link:hover {
		color: #3b82f6;
	}

	h1 {
		margin: 0 0 1.5rem;
		font-size: 1.75rem;
		color: #1a1a2e;
	}

	.error-banner {
		background: #fef2f2;
		color: #dc2626;
		padding: 0.75rem 1rem;
		border-radius: 8px;
		margin-bottom: 1rem;
		font-size: 0.875rem;
		border-left: 3px solid #dc2626;
	}

	.form-card {
		background: white;
		border-radius: 12px;
		padding: 2rem;
		border: 1px solid #e5e7eb;
		max-width: 560px;
	}

	.field {
		margin-bottom: 1.25rem;
	}

	label {
		display: block;
		font-weight: 500;
		margin-bottom: 0.375rem;
		color: #374151;
		font-size: 0.875rem;
	}

	.required {
		color: #dc2626;
	}

	.hint {
		display: block;
		font-size: 0.75rem;
		color: #9ca3af;
		margin-top: 0.25rem;
	}

	input,
	textarea {
		width: 100%;
		padding: 0.625rem 0.75rem;
		border: 1px solid #d1d5db;
		border-radius: 8px;
		font-size: 0.9375rem;
		transition: border-color 0.15s;
		box-sizing: border-box;
		font-family: inherit;
	}

	input:focus,
	textarea:focus {
		outline: none;
		border-color: #3b82f6;
		box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
	}

	textarea {
		resize: vertical;
	}

	.form-actions {
		display: flex;
		gap: 0.75rem;
		justify-content: flex-end;
		margin-top: 1.5rem;
	}

	.btn-primary {
		background: #3b82f6;
		color: white;
		padding: 0.625rem 1.25rem;
		border-radius: 8px;
		font-size: 0.9375rem;
		font-weight: 600;
		border: none;
		cursor: pointer;
		transition: background 0.15s;
	}

	.btn-primary:hover:not(:disabled) {
		background: #2563eb;
	}

	.btn-primary:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-secondary {
		background: white;
		color: #4b5563;
		padding: 0.625rem 1.25rem;
		border-radius: 8px;
		font-size: 0.9375rem;
		font-weight: 500;
		border: 1px solid #d1d5db;
		text-decoration: none;
		display: inline-flex;
		align-items: center;
		transition: background 0.15s;
	}

	.btn-secondary:hover {
		background: #f9fafb;
	}
</style>
