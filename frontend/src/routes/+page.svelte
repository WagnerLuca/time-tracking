<script lang="ts">
	import { onMount } from 'svelte';
	import { apiService } from '$lib/apiService';

	let weatherData: any[] = [];
	let loading = true;
	let error = '';

	onMount(async () => {
		try {
			weatherData = await apiService.getWeatherForecast();
		} catch (err) {
			error = 'Failed to fetch weather data. Make sure the API is running.';
			console.error(err);
		} finally {
			loading = false;
		}
	});
</script>

<div class="container">
	<h1>Time Tracking App</h1>
	<p>SvelteKit frontend connected to ASP.NET Core Web API</p>

	<section class="api-demo">
		<h2>API Connection Demo</h2>
		
		{#if loading}
			<p>Loading weather data...</p>
		{:else if error}
			<div class="error">
				<p>{error}</p>
				<p>To test the API connection:</p>
				<ol>
					<li>Start your .NET API: <code>cd backend && dotnet run</code></li>
					<li>Make sure the API URL in <code>src/lib/apiService.ts</code> matches your API</li>
				</ol>
			</div>
		{:else}
			<div class="weather-data">
				<h3>Weather Forecast (from API)</h3>
				{#each weatherData as forecast}
					<div class="forecast-item">
						<strong>Date:</strong> {forecast.date}<br>
						<strong>Temperature:</strong> {forecast.temperatureC}°C ({forecast.temperatureF}°F)<br>
						<strong>Summary:</strong> {forecast.summary}
					</div>
				{/each}
			</div>
		{/if}
	</section>
</div>

<style>
	.container {
		max-width: 800px;
		margin: 0 auto;
		padding: 2rem;
		font-family: Arial, sans-serif;
	}

	.api-demo {
		margin-top: 2rem;
		padding: 1.5rem;
		border: 1px solid #e0e0e0;
		border-radius: 8px;
		background-color: #f9f9f9;
	}

	.error {
		color: #d32f2f;
		background-color: #ffebee;
		padding: 1rem;
		border-radius: 4px;
		border-left: 4px solid #d32f2f;
	}

	.weather-data {
		background-color: white;
		padding: 1rem;
		border-radius: 4px;
		border: 1px solid #e0e0e0;
	}

	.forecast-item {
		margin-bottom: 1rem;
		padding: 0.5rem;
		background-color: #f5f5f5;
		border-radius: 4px;
	}

	code {
		background-color: #f5f5f5;
		padding: 0.25rem 0.5rem;
		border-radius: 3px;
		font-family: 'Courier New', monospace;
	}

	ol {
		margin: 1rem 0;
		padding-left: 2rem;
	}

	h1 {
		color: #1976d2;
	}

	h2, h3 {
		color: #424242;
	}
</style>
