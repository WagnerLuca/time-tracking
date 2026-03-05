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

	async function handleRegister(e: Event) {
		e.preventDefault();
		error = '';

		if (password !== confirmPassword) {
			error = 'Passwords do not match.';
			return;
		}
		if (password.length < 6) {
			error = 'Password must be at least 6 characters.';
			return;
		}

		submitting = true;
		try {
			await auth.register({ email, password, firstName, lastName });
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
		<div class="card-body p-10">
			<h1 class="text-2xl font-bold mb-1">Create Account</h1>
			<p class="text-base-content/60 mb-6">Get started with Time Tracking</p>

			{#if error}
				<div class="alert alert-error text-sm mb-4">{error}</div>
			{/if}

			<form onsubmit={handleRegister}>
				<div class="grid grid-cols-2 gap-3">
					<div class="mb-5">
						<label for="firstName" class="block font-medium mb-1.5 text-base-content/80 text-sm">First Name</label>
						<input
							id="firstName"
							type="text"
							class="input input-bordered w-full"
							bind:value={firstName}
							placeholder="John"
							required
							disabled={submitting}
						/>
					</div>
					<div class="mb-5">
						<label for="lastName" class="block font-medium mb-1.5 text-base-content/80 text-sm">Last Name</label>
						<input
							id="lastName"
							type="text"
							class="input input-bordered w-full"
							bind:value={lastName}
							placeholder="Doe"
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
						minlength="6"
						disabled={submitting}
					/>
				</div>

				<div class="mb-5">
					<label for="confirmPassword" class="block font-medium mb-1.5 text-base-content/80 text-sm">Confirm Password</label>
					<input
						id="confirmPassword"
						type="password"
						class="input input-bordered w-full"
						bind:value={confirmPassword}
						placeholder="••••••••"
						required
						disabled={submitting}
					/>
				</div>

				<button type="submit" class="btn btn-primary w-full" disabled={submitting}>
					{submitting ? 'Creating account...' : 'Create Account'}
				</button>
			</form>

			<p class="text-center mt-6 text-sm text-base-content/60">
				Already have an account? <a href="/login" class="link link-primary">Sign in</a>
			</p>
		</div>
	</div>
</div>

