<script lang="ts">
	import { auth } from '$lib/stores/auth.svelte';
	import { goto } from '$app/navigation';

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
			goto('/');
		} catch (err: any) {
			error = err.response?.data?.message || 'Registration failed. Please try again.';
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head>
	<title>Register - Time Tracking</title>
</svelte:head>

<div class="auth-page">
	<div class="auth-card">
		<h1>Create Account</h1>
		<p class="subtitle">Get started with Time Tracking</p>

		{#if error}
			<div class="error-banner">{error}</div>
		{/if}

		<form onsubmit={handleRegister}>
			<div class="row">
				<div class="field">
					<label for="firstName">First Name</label>
					<input
						id="firstName"
						type="text"
						bind:value={firstName}
						placeholder="John"
						required
						disabled={submitting}
					/>
				</div>
				<div class="field">
					<label for="lastName">Last Name</label>
					<input
						id="lastName"
						type="text"
						bind:value={lastName}
						placeholder="Doe"
						required
						disabled={submitting}
					/>
				</div>
			</div>

			<div class="field">
				<label for="email">Email</label>
				<input
					id="email"
					type="email"
					bind:value={email}
					placeholder="you@example.com"
					required
					disabled={submitting}
				/>
			</div>

			<div class="field">
				<label for="password">Password</label>
				<input
					id="password"
					type="password"
					bind:value={password}
					placeholder="••••••••"
					required
					minlength="6"
					disabled={submitting}
				/>
			</div>

			<div class="field">
				<label for="confirmPassword">Confirm Password</label>
				<input
					id="confirmPassword"
					type="password"
					bind:value={confirmPassword}
					placeholder="••••••••"
					required
					disabled={submitting}
				/>
			</div>

			<button type="submit" class="btn-primary" disabled={submitting}>
				{submitting ? 'Creating account...' : 'Create Account'}
			</button>
		</form>

		<p class="switch-auth">
			Already have an account? <a href="/login">Sign in</a>
		</p>
	</div>
</div>

<style>
	.auth-page {
		min-height: 100vh;
		display: flex;
		align-items: center;
		justify-content: center;
		background: #f0f2f5;
		padding: 1rem;
	}

	.auth-card {
		background: white;
		border-radius: 12px;
		padding: 2.5rem;
		width: 100%;
		max-width: 420px;
		box-shadow: 0 2px 12px rgba(0, 0, 0, 0.08);
	}

	h1 {
		margin: 0 0 0.25rem;
		color: #1a1a2e;
		font-size: 1.75rem;
	}

	.subtitle {
		color: #6b7280;
		margin: 0 0 1.5rem;
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

	.row {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 0.75rem;
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

	input {
		width: 100%;
		padding: 0.625rem 0.75rem;
		border: 1px solid #d1d5db;
		border-radius: 8px;
		font-size: 0.9375rem;
		transition: border-color 0.15s;
		box-sizing: border-box;
	}

	input:focus {
		outline: none;
		border-color: #3b82f6;
		box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
	}

	.btn-primary {
		width: 100%;
		padding: 0.75rem;
		background: #3b82f6;
		color: white;
		border: none;
		border-radius: 8px;
		font-size: 1rem;
		font-weight: 600;
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

	.switch-auth {
		text-align: center;
		margin-top: 1.5rem;
		color: #6b7280;
		font-size: 0.875rem;
	}

	.switch-auth a {
		color: #3b82f6;
		text-decoration: none;
		font-weight: 500;
	}

	.switch-auth a:hover {
		text-decoration: underline;
	}
</style>
