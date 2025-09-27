import { json } from '@sveltejs/kit';

/** @type {import('./$types').RequestHandler} */
export async function GET() {
	return json({
		status: 'healthy',
		timestamp: new Date().toISOString(),
		service: 'sveltekit-frontend'
	});
}