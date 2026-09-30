<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { goto } from '$app/navigation';
	import { extractErrorMessage } from '$lib/utils/errorHandler';

	let email = $state('');
	let password = $state('');
	let confirmPassword = $state('');
	let firstName = $state('');
	let lastName = $state('');
	let error = $state('');
	let submitting = $state(false);
	/** Inline errors show after the first submit attempt. */
	let attempted = $state(false);

	/** Mirrors the backend's RegisterRequest ([MinLength(8), MaxLength(128)]). */
	const MIN_PASSWORD = 8;
	const passwordTooShort = $derived(password.length < MIN_PASSWORD);
	const passwordMismatch = $derived((attempted || confirmPassword.length > 0) && password !== confirmPassword);

	const emailValid = $derived(/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim()));
	const formValid = $derived(!!firstName.trim() && !!lastName.trim() && emailValid && !passwordTooShort && password === confirmPassword);

	async function handleRegister(e: Event) {
		e.preventDefault();
		error = '';
		attempted = true;
		if (!formValid) return;

		submitting = true;
		try {
			await auth.register({ email: email.trim(), password, firstName: firstName.trim(), lastName: lastName.trim() });
			// TODO(backend): email verification step once available
			goto('/organizations');
		} catch (err) {
			error = extractErrorMessage(err, 'Registration failed. Please try again.');
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head>
	<title>Register - Time Tracking</title>
</svelte:head>

<div class="min-h-screen flex items-center justify-center bg-base-200 p-4">
	<div class="card bg-base-100 shadow-lg w-full max-w-[420px]">
		<div class="card-body p-6 sm:p-10">
			<h1 class="text-2xl font-bold mb-1">Create Account</h1>
			<p class="text-base-content/60 mb-6">Get started with Time Tracking</p>

			{#if error}
				<div class="alert alert-error text-sm mb-4" role="alert">{error}</div>
			{/if}

			<form onsubmit={handleRegister} novalidate>
				<div class="grid grid-cols-1 sm:grid-cols-2 gap-x-3">
					<div class="mb-5">
						<label for="firstName" class="block font-medium mb-1.5 text-base-content/80 text-sm">First name</label>
						<input
							id="firstName"
							type="text"
							autocomplete="given-name"
							class="input input-bordered w-full"
							class:input-error={attempted && !firstName.trim()}
							bind:value={firstName}
							placeholder="Max"
							required
							disabled={submitting}
						/>
					</div>
					<div class="mb-5">
						<label for="lastName" class="block font-medium mb-1.5 text-base-content/80 text-sm">Last name</label>
						<input
							id="lastName"
							type="text"
							autocomplete="family-name"
							class="input input-bordered w-full"
							class:input-error={attempted && !lastName.trim()}
							bind:value={lastName}
							placeholder="Mustermann"
							required
							disabled={submitting}
						/>
					</div>
				</div>

				<div class="mb-5">
					<label for="email" class="block font-medium mb-1.5 text-base-content/80 text-sm">Email</label>
					<input
						id="email"
						type="email"
						autocomplete="email"
						class="input input-bordered w-full"
						class:input-error={attempted && !emailValid}
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
						autocomplete="new-password"
						class="input input-bordered w-full"
						class:input-error={attempted && passwordTooShort}
						bind:value={password}
						placeholder="••••••••"
						required
						minlength={MIN_PASSWORD}
						aria-invalid={attempted && passwordTooShort}
						aria-describedby="passwordHint"
						disabled={submitting}
					/>
					<p id="passwordHint" class="text-xs mt-1 {attempted && passwordTooShort ? 'text-error' : 'text-base-content/60'}">
						At least {MIN_PASSWORD} characters.
					</p>
				</div>

				<div class="mb-5">
					<label for="confirmPassword" class="block font-medium mb-1.5 text-base-content/80 text-sm">Confirm password</label>
					<input
						id="confirmPassword"
						type="password"
						autocomplete="new-password"
						class="input input-bordered w-full"
						class:input-error={passwordMismatch}
						bind:value={confirmPassword}
						placeholder="••••••••"
						required
						aria-invalid={passwordMismatch}
						aria-describedby={passwordMismatch ? 'confirmError' : undefined}
						disabled={submitting}
					/>
					{#if passwordMismatch}
						<p id="confirmError" class="text-xs mt-1 text-error">Passwords do not match.</p>
					{/if}
				</div>

				<button type="submit" class="btn btn-primary w-full" disabled={submitting || (attempted && !formValid)}>
					{submitting ? 'Creating account…' : 'Create account'}
				</button>
			</form>

			<p class="text-center mt-6 text-sm text-base-content/60">
				Already have an account? <a href="/login" class="link link-primary">Sign in</a>
			</p>
		</div>
	</div>
</div>

