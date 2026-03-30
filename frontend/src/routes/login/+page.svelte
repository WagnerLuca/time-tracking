<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { orgContext } from '$lib/stores/orgContext.svelte';
	import { goto } from '$app/navigation';
	import { extractErrorMessage } from '$lib/utils/errorHandler';

	let email = $state('');
	let password = $state('');
	let error = $state('');
	let submitting = $state(false);

	// 2FA state
	let twoFactorCode = $state('');

	async function navigateAfterLogin() {
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
	}

	async function handleLogin(e: Event) {
		e.preventDefault();
		error = '';
		submitting = true;
		try {
			const result = await auth.login({ email, password });
			if (result === '2fa') {
				// 2FA step — stay on page, show code input
				submitting = false;
				return;
			}
			await navigateAfterLogin();
		} catch (err) {
			error = extractErrorMessage(err, 'Login failed. Please check your credentials.');
		} finally {
			submitting = false;
		}
	}

	async function handleTwoFactor(e: Event) {
		e.preventDefault();
		error = '';
		submitting = true;
		try {
			await auth.verifyTwoFactor(twoFactorCode);
			await navigateAfterLogin();
		} catch (err) {
			error = extractErrorMessage(err, 'Invalid verification code. Please try again.');
		} finally {
			submitting = false;
		}
	}

	function cancelTwoFactor() {
		auth.clearTwoFactor();
		twoFactorCode = '';
		error = '';
	}
</script>

<svelte:head>
	<title>Login - Time Tracking</title>
</svelte:head>

<div class="min-h-screen flex items-center justify-center bg-base-200 p-4">
	<div class="card bg-base-100 shadow-lg w-full max-w-[420px]">
		<div class="card-body p-10">
			{#if auth.twoFactorRequired}
				<h1 class="text-2xl font-bold mb-1">Two-Factor Authentication</h1>
				<p class="text-base-content/60 mb-6">Enter the 6-digit code from your authenticator app</p>

				{#if error}
					<div class="alert alert-error text-sm mb-4">{error}</div>
				{/if}

				<form onsubmit={handleTwoFactor}>
					<div class="mb-5">
						<label for="twoFactorCode" class="block font-medium mb-1.5 text-base-content/80 text-sm">Verification Code</label>
						<input
							id="twoFactorCode"
							type="text"
							inputmode="numeric"
							autocomplete="one-time-code"
							class="input input-bordered w-full text-center text-2xl tracking-[0.3em] font-mono"
							bind:value={twoFactorCode}
							placeholder="000000"
							maxlength="10"
							required
							disabled={submitting}
						/>
						<p class="text-xs text-base-content/50 mt-2">You can also use a backup code.</p>
					</div>

					<button type="submit" class="btn btn-primary w-full" disabled={submitting || !twoFactorCode}>
						{submitting ? 'Verifying...' : 'Verify'}
					</button>
				</form>

				<button type="button" class="btn btn-ghost btn-sm mt-3 w-full" onclick={cancelTwoFactor}>
					Back to Login
				</button>
			{:else}
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
			{/if}
		</div>
	</div>
</div>

