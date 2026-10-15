/// <reference types="@sveltejs/kit" />
/// <reference no-default-lib="true"/>
/// <reference lib="esnext" />
/// <reference lib="webworker" />

/*
 * The PWA's service worker (issue #67), registered by lib/pwa/serviceWorker.ts in production
 * builds only. One cache per deployed version holds the built app (`build`), the static files
 * (`files`) and the app shell ("/", the same HTML for every route since ssr = false). Old caches
 * go when a new version activates. /api/* is never touched: network only, nothing private is
 * stored. A new version waits until the page asks it to take over ("Neue Version verfügbar").
 */
import { build, files, version } from '$service-worker';

const sw = self as unknown as ServiceWorkerGlobalScope;
const CACHE = `wl-time-${version}`;
const SHELL = '/';
const PRECACHE = [...build, ...files, SHELL];

sw.addEventListener('install', (event) => {
	event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(PRECACHE)));
});

sw.addEventListener('activate', (event) => {
	event.waitUntil(
		(async () => {
			for (const key of await caches.keys()) if (key !== CACHE) await caches.delete(key);
			await sw.clients.claim();
		})()
	);
});

sw.addEventListener('message', (event) => {
	if (event.data?.type === 'SKIP_WAITING') sw.skipWaiting();
});

sw.addEventListener('fetch', (event) => {
	const request = event.request;
	const url = new URL(request.url);
	if (request.method !== 'GET' || url.origin !== sw.location.origin) return;
	if (url.pathname.startsWith('/api/') || url.pathname === '/health') return;

	if (request.mode === 'navigate') {
		// Network first so a deploy shows at once; offline, the cached shell renders the route
		event.respondWith(fetch(request).catch(async () => (await caches.match(SHELL, { cacheName: CACHE })) ?? Response.error()));
		return;
	}
	if (PRECACHE.includes(url.pathname)) {
		event.respondWith(caches.match(url.pathname, { cacheName: CACHE }).then((hit) => hit ?? fetch(request)));
	}
});

/** Payload from the backend's PushDispatcher: { title, body, url, tag }. */
sw.addEventListener('push', (event) => {
	let data: { title?: string; body?: string; url?: string; tag?: string } = {};
	try {
		data = event.data?.json() ?? {};
	} catch {
		data = { body: event.data?.text() };
	}
	event.waitUntil(
		sw.registration.showNotification(data.title ?? 'WL Zeiterfassung', {
			body: data.body,
			tag: data.tag,
			icon: '/icons/icon-192.png',
			badge: '/icons/badge-96.png',
			data: { url: data.url ?? '/' }
		})
	);
});

sw.addEventListener('notificationclick', (event) => {
	event.notification.close();
	const target = new URL(event.notification.data?.url ?? '/', sw.location.origin);
	// Only ever open our own pages
	const path = target.origin === sw.location.origin ? target.pathname + target.search : '/';
	event.waitUntil(
		(async () => {
			const windows = await sw.clients.matchAll({ type: 'window', includeUncontrolled: true });
			const open = windows.find((w) => new URL(w.url).origin === sw.location.origin);
			if (open) {
				// The app navigates itself (client-side, keeps its state): lib/pwa/serviceWorker.ts
				open.postMessage({ type: 'NAVIGATE', url: path });
				await open.focus();
			} else {
				await sw.clients.openWindow(path);
			}
		})()
	);
});
