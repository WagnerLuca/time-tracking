<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { goto } from '$app/navigation';
	import { extractErrorMessage } from '$lib/utils/errorHandler';

	let email = $state('');
	let password = $state('');
	let error = $state('');
	let submitting = $state(false);

	async function handleLogin(e: Event) {
		e.preventDefault();
		error = '';
		submitting = true;
		try {
			await auth.login({ email, password });
			// Load orgs and auto-select if just one
			if (auth.user?.id) {
				await orgContext.loadOrganizations(auth.user.id);
			}
			if (orgContext.selectedOrgId) {
				goto('/');
			} else if (orgContext.organizations.length > 0) {
				goto('/select-org');
			} else {
				goto('/organizations');
			}
		} catch (err) {
			error = extractErrorMessage(err, 'Login failed. Please check your credentials.');
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head>
	<title>Login - Time Tracking</title>
</svelte:head>

<div class="min-h-screen flex items-center justify-center bg-base-200 p-4">
	<div class="card bg-base-100 shadow-lg w-full max-w-[420px]">
		<div class="card-body p-10">
			<h1 class="text-2xl font-bold mb-1">Sign In</h1>
			<p class="text-base-content/60 mb-6">Welcome back to Time Tracking</p>

			{#if error}
				<div class="alert alert-error text-sm mb-4">{error}</div>
			{/if}

			<form onsubmit={handleLogin}>
				<div class="mb-5">
					<label for="email" class="block font-medium mb-1.5 text-base-content/80 text-sm">Email</label>
					<input
						id="email"
						type="email"
						class="input input-bordered w-full"
						bind:value={email}
						placeholder="you@example.com"
						required
						disabled={submitting}
					/>
				</div>

				<div class="mb-5">
					<label for="password" class="block font-medium mb-1.5 text-base-content/80 text-sm">Password</label>
					<input
						id="password"
						type="password"
						class="input input-bordered w-full"
						bind:value={password}
						placeholder="••••••••"
						required
						disabled={submitting}
					/>
				</div>

				<button type="submit" class="btn btn-primary w-full" disabled={submitting}>
					{submitting ? 'Signing in...' : 'Sign In'}
				</button>
			</form>

			<p class="text-center mt-6 text-sm text-base-content/60">
				Don't have an account? <a href="/register" class="link link-primary">Create one</a>
			</p>
		</div>
	</div>
</div>

