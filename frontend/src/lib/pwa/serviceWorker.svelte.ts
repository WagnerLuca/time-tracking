/**
 * Registers the service worker (src/service-worker.ts) in production builds and offers its
 * updates (issue #67): a new version installs in the background and waits; the layout then
 * shows "Neue Version verfügbar – neu laden", and `applyUpdate()` lets it take over and reloads
 * once it controls the page. Updates are looked for when the app returns to the foreground and
 * hourly. The worker also asks the page to open a notification's link (`NAVIGATE`).
 */
function createServiceWorker() {
	let waiting = $state<ServiceWorker | null>(null);
	let reloading = false;

	async function register(onNavigate: (path: string) => void): Promise<ServiceWorkerRegistration | null> {
		if (typeof navigator === 'undefined' || !('serviceWorker' in navigator)) return null;
		const container = navigator.serviceWorker;
		container.addEventListener('message', (e: MessageEvent) => {
			const url = e.data?.type === 'NAVIGATE' ? e.data.url : null;
			if (typeof url === 'string' && url.startsWith('/') && !url.startsWith('//')) onNavigate(url);
		});
		container.addEventListener('controllerchange', () => {
			if (reloading) location.reload();
		});

		const reg = await container.register('/service-worker.js');
		const watch = (worker: ServiceWorker | null) =>
			worker?.addEventListener('statechange', () => {
				// The first install has no controller yet: nothing to update then
				if (worker.state === 'installed' && container.controller) waiting = worker;
			});
		if (reg.waiting && container.controller) waiting = reg.waiting;
		watch(reg.installing);
		reg.addEventListener('updatefound', () => watch(reg.installing));

		const check = () => reg.update().catch(() => {});
		document.addEventListener('visibilitychange', () => {
			if (document.visibilityState === 'visible') check();
		});
		setInterval(check, 60 * 60 * 1000);
		return reg;
	}

	function applyUpdate() {
		if (!waiting) return location.reload();
		reloading = true;
		waiting.postMessage({ type: 'SKIP_WAITING' });
	}

	return {
		get updateAvailable() { return waiting !== null; },
		register,
		applyUpdate
	};
}

export const serviceWorker = createServiceWorker();
