import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * The PWA helpers (issue #67) with stubbed browser globals: install prompt, service worker
 * update prompt, push opt-in (mocked Notification/PushManager), online state.
 */
const api = vi.hoisted(() => ({
	pushApi: {
		apiV1PushPublicKeyGet: vi.fn(),
		apiV1PushSubscriptionsPost: vi.fn(),
		apiV1PushSubscriptionsRemovePost: vi.fn()
	}
}));
vi.mock('$lib/apiClient', () => api);

/** A tiny EventTarget stand-in that lets tests fire events. */
function emitter<T extends object>(extra: T) {
	const handlers: Record<string, ((e: unknown) => void)[]> = {};
	return Object.assign(extra, {
		addEventListener: (name: string, fn: (e: unknown) => void) => (handlers[name] ??= []).push(fn),
		removeEventListener: (name: string, fn: (e: unknown) => void) => (handlers[name] = (handlers[name] ?? []).filter((h) => h !== fn)),
		emit: (name: string, e: unknown = {}) => (handlers[name] ?? []).forEach((h) => h(e)),
		count: (name: string) => (handlers[name] ?? []).length
	});
}

function memoryStorage(initial: Record<string, string> = {}) {
	const map = new Map(Object.entries(initial));
	return {
		getItem: (k: string) => map.get(k) ?? null,
		setItem: (k: string, v: string) => void map.set(k, v),
		removeItem: (k: string) => void map.delete(k),
		map
	};
}

beforeEach(() => {
	vi.resetModules();
	vi.clearAllMocks();
});
afterEach(() => {
	vi.unstubAllGlobals();
	vi.useRealTimers();
});

describe('install', () => {
	function stub({ standalone = false, ua = 'Mozilla/5.0 (Linux; Android 14) Chrome/130', platform = 'Linux', touch = 5 } = {}) {
		const win = emitter({});
		vi.stubGlobal('window', win);
		vi.stubGlobal('navigator', { userAgent: ua, platform, maxTouchPoints: touch });
		vi.stubGlobal('matchMedia', () => ({ matches: standalone }));
		return win;
	}

	it('keeps beforeinstallprompt and installs through it', async () => {
		const win = stub();
		const { install } = await import('./install.svelte');
		const stop = install.listen();
		expect(install.canPrompt).toBe(false);
		expect(install.installed).toBe(false);
		expect(install.ios).toBe(false);
		expect(await install.prompt()).toBe(false); // nothing offered yet

		const event = { prompt: vi.fn().mockResolvedValue(undefined), userChoice: Promise.resolve({ outcome: 'accepted' }) };
		win.emit('beforeinstallprompt', event);
		expect(install.canPrompt).toBe(true);
		expect(await install.prompt()).toBe(true);
		expect(event.prompt).toHaveBeenCalledOnce();
		expect(install.installed).toBe(true);
		expect(install.canPrompt).toBe(false); // a prompt event is single-use

		stop();
		expect(win.count('beforeinstallprompt')).toBe(0);
		expect(win.count('appinstalled')).toBe(0);
	});

	it('a dismissed prompt leaves the app uninstalled; appinstalled marks it installed', async () => {
		const win = stub();
		const { install } = await import('./install.svelte');
		install.listen();
		win.emit('beforeinstallprompt', { prompt: vi.fn().mockResolvedValue(undefined), userChoice: Promise.resolve({ outcome: 'dismissed' }) });
		expect(await install.prompt()).toBe(false);
		expect(install.installed).toBe(false);
		win.emit('appinstalled');
		expect(install.installed).toBe(true);
	});

	it('detects the standalone app and iOS (iPhone, iPadOS as a Mac with touch)', async () => {
		stub({ standalone: true });
		let { install } = await import('./install.svelte');
		install.listen();
		expect(install.installed).toBe(true);

		vi.resetModules();
		stub({ ua: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)' });
		({ install } = await import('./install.svelte'));
		install.listen();
		expect(install.ios).toBe(true);

		vi.resetModules();
		stub({ ua: 'Mozilla/5.0 (Macintosh)', platform: 'MacIntel', touch: 5 });
		({ install } = await import('./install.svelte'));
		install.listen();
		expect(install.ios).toBe(true);
	});
});

describe('service worker update prompt', () => {
	function stub({ controller = {} as object | null, waiting = null as object | null } = {}) {
		const reg = emitter({ waiting, installing: null as unknown, update: vi.fn().mockResolvedValue(undefined) });
		const container = emitter({ controller, register: vi.fn().mockResolvedValue(reg) });
		const doc = emitter({ visibilityState: 'visible' });
		const reload = vi.fn();
		vi.stubGlobal('navigator', { serviceWorker: container });
		vi.stubGlobal('document', doc);
		vi.stubGlobal('location', { reload });
		return { reg, container, doc, reload };
	}

	it('shows the prompt when a new version is installed and reloads once it took over', async () => {
		vi.useFakeTimers();
		const { reg, container, doc, reload } = stub();
		const { serviceWorker } = await import('./serviceWorker.svelte');
		const navigate = vi.fn();
		expect(await serviceWorker.register(navigate)).toBe(reg);
		expect(container.register).toHaveBeenCalledWith('/service-worker.js');
		expect(serviceWorker.updateAvailable).toBe(false);

		const worker = emitter({ state: 'installing', postMessage: vi.fn() });
		reg.installing = worker;
		reg.emit('updatefound');
		worker.state = 'installed';
		worker.emit('statechange');
		expect(serviceWorker.updateAvailable).toBe(true);

		container.emit('controllerchange'); // not asked for: no reload
		expect(reload).not.toHaveBeenCalled();
		serviceWorker.applyUpdate();
		expect(worker.postMessage).toHaveBeenCalledWith({ type: 'SKIP_WAITING' });
		container.emit('controllerchange');
		expect(reload).toHaveBeenCalledOnce();

		// Looks for updates in the foreground and hourly
		doc.emit('visibilitychange');
		vi.advanceTimersByTime(60 * 60 * 1000);
		expect(reg.update).toHaveBeenCalledTimes(2);
	});

	it('the first install is no update; a worker already waiting is', async () => {
		let { reg } = stub({ controller: null });
		let { serviceWorker } = await import('./serviceWorker.svelte');
		await serviceWorker.register(vi.fn());
		const worker = emitter({ state: 'installed', postMessage: vi.fn() });
		reg.installing = worker;
		reg.emit('updatefound');
		worker.emit('statechange');
		expect(serviceWorker.updateAvailable).toBe(false);

		vi.resetModules();
		({ reg } = stub({ waiting: { postMessage: vi.fn() } }));
		({ serviceWorker } = await import('./serviceWorker.svelte'));
		await serviceWorker.register(vi.fn());
		expect(serviceWorker.updateAvailable).toBe(true);
	});

	it('opens notification links in the app (own paths only); reloads when nothing waits', async () => {
		const { container, reload } = stub();
		const { serviceWorker } = await import('./serviceWorker.svelte');
		const navigate = vi.fn();
		await serviceWorker.register(navigate);
		container.emit('message', { data: { type: 'NAVIGATE', url: '/history?date=2026-03-02' } });
		container.emit('message', { data: { type: 'NAVIGATE', url: '//evil.example' } });
		container.emit('message', { data: { type: 'NAVIGATE', url: 'https://evil.example' } });
		container.emit('message', { data: { type: 'OTHER', url: '/x' } });
		expect(navigate).toHaveBeenCalledExactlyOnceWith('/history?date=2026-03-02');

		serviceWorker.applyUpdate();
		expect(reload).toHaveBeenCalledOnce();
	});

	it('does nothing without service worker support', async () => {
		vi.stubGlobal('navigator', {});
		const { serviceWorker } = await import('./serviceWorker.svelte');
		expect(await serviceWorker.register(vi.fn())).toBeNull();
	});
});

describe('push opt-in', () => {
	const KEY = 'BA' + 'A'.repeat(85); // 65 bytes base64url

	function stub({ permission = 'default', subscribed = false, registered = true, answer = 'granted' } = {}) {
		const storage = memoryStorage();
		const state = { permission, sub: null as null | { endpoint: string; toJSON: () => unknown; unsubscribe: ReturnType<typeof vi.fn> } };
		const makeSub = (endpoint: string) => ({
			endpoint,
			toJSON: () => ({ endpoint, keys: { p256dh: 'p', auth: 'a' } }),
			unsubscribe: vi.fn(async () => {
				state.sub = null;
				return true;
			})
		});
		if (subscribed) state.sub = makeSub('https://fcm.googleapis.com/fcm/send/old');
		const pushManager = {
			getSubscription: vi.fn(async () => state.sub),
			subscribe: vi.fn(async () => (state.sub = makeSub('https://fcm.googleapis.com/fcm/send/new')))
		};
		const Notification = {
			get permission() { return state.permission; },
			requestPermission: vi.fn(async () => (state.permission = answer))
		};
		vi.stubGlobal('localStorage', storage);
		vi.stubGlobal('Notification', Notification);
		vi.stubGlobal('window', { PushManager: {}, Notification });
		vi.stubGlobal('navigator', { serviceWorker: { getRegistration: vi.fn(async () => (registered ? { pushManager } : undefined)) } });
		api.pushApi.apiV1PushPublicKeyGet.mockResolvedValue({ data: { publicKey: KEY } });
		api.pushApi.apiV1PushSubscriptionsPost.mockResolvedValue({});
		api.pushApi.apiV1PushSubscriptionsRemovePost.mockResolvedValue({});
		return { storage, state, pushManager, Notification };
	}

	it('decodes the base64url VAPID key', async () => {
		const { base64UrlToBytes } = await import('./push');
		expect(Array.from(base64UrlToBytes('_-8'))).toEqual([255, 239]);
		expect(base64UrlToBytes(KEY)).toHaveLength(65);
	});

	it('asks only when enabled, subscribes freshly and registers the device', async () => {
		const { storage, state, pushManager, Notification } = stub({ subscribed: true });
		const push = await import('./push');
		expect(await push.pushState(7)).toBe('off'); // reading the state never asks
		expect(Notification.requestPermission).not.toHaveBeenCalled();

		const old = state.sub!;
		expect(await push.enablePush(7, 'en')).toBe('on');
		expect(Notification.requestPermission).toHaveBeenCalledOnce();
		expect(old.unsubscribe).toHaveBeenCalled(); // never reuse an old subscription
		expect(pushManager.subscribe).toHaveBeenCalledWith(expect.objectContaining({ userVisibleOnly: true }));
		expect(api.pushApi.apiV1PushSubscriptionsPost).toHaveBeenCalledWith({
			endpoint: 'https://fcm.googleapis.com/fcm/send/new', p256dh: 'p', auth: 'a', locale: 'en'
		});
		expect(storage.map.get('wl-push-user')).toBe('7');
		expect(await push.pushState(7)).toBe('on');
		expect(await push.pushState(8)).toBe('off'); // another account on this device
	});

	it('a declined or dismissed permission, a missing server key, no support', async () => {
		stub({ answer: 'denied' });
		let push = await import('./push');
		expect(await push.enablePush(7, 'de')).toBe('denied');
		expect(await push.pushState(7)).toBe('denied');

		vi.resetModules();
		stub({ answer: 'default' });
		push = await import('./push');
		expect(await push.enablePush(7, 'de')).toBe('off');

		vi.resetModules();
		stub();
		api.pushApi.apiV1PushPublicKeyGet.mockResolvedValue({ data: { publicKey: null } });
		push = await import('./push');
		await expect(push.enablePush(7, 'de')).rejects.toEqual({ response: { data: { code: 'push.notConfigured' } } });

		vi.resetModules();
		stub({ registered: false }); // the dev server registers no service worker
		push = await import('./push');
		expect(await push.pushState(7)).toBe('unsupported');
		expect(await push.enablePush(7, 'de')).toBe('unsupported');

		vi.resetModules();
		vi.stubGlobal('window', {});
		vi.stubGlobal('navigator', {});
		push = await import('./push');
		expect(await push.pushState(7)).toBe('unsupported');
	});

	it('disable removes the device on the server and in the browser', async () => {
		const { storage, state } = stub({ permission: 'granted', subscribed: true });
		storage.setItem('wl-push-user', '7');
		const push = await import('./push');
		const sub = state.sub!;
		await push.disablePush();
		expect(api.pushApi.apiV1PushSubscriptionsRemovePost).toHaveBeenCalledWith({ endpoint: sub.endpoint });
		expect(sub.unsubscribe).toHaveBeenCalled();
		expect(storage.map.has('wl-push-user')).toBe(false);
		await push.disablePush(); // nothing left: no call
		expect(api.pushApi.apiV1PushSubscriptionsRemovePost).toHaveBeenCalledOnce();
	});

	it('the browser side goes even when the server cannot be reached', async () => {
		const { state } = stub({ permission: 'granted', subscribed: true });
		api.pushApi.apiV1PushSubscriptionsRemovePost.mockRejectedValue(new Error('offline'));
		const push = await import('./push');
		const sub = state.sub!;
		await expect(push.disablePush()).rejects.toThrow('offline');
		expect(sub.unsubscribe).toHaveBeenCalled();
	});

	it('sync re-registers the own device and drops another account’s', async () => {
		const { storage, state } = stub({ permission: 'granted', subscribed: true });
		storage.setItem('wl-push-user', '7');
		const push = await import('./push');
		await push.syncPush(7, 'de');
		expect(api.pushApi.apiV1PushSubscriptionsPost).toHaveBeenCalledWith(expect.objectContaining({ locale: 'de' }));

		const sub = state.sub!;
		await push.syncPush(8, 'de');
		expect(sub.unsubscribe).toHaveBeenCalled();
		expect(storage.map.has('wl-push-user')).toBe(false);
		expect(api.pushApi.apiV1PushSubscriptionsPost).toHaveBeenCalledOnce();
		await push.syncPush(8, 'de'); // nothing subscribed: nothing to do
	});

	it('survives blocked storage', async () => {
		stub({ permission: 'granted', subscribed: true });
		vi.stubGlobal('localStorage', {
			getItem: () => { throw new Error('blocked'); },
			setItem: () => { throw new Error('blocked'); },
			removeItem: () => { throw new Error('blocked'); }
		});
		const push = await import('./push');
		expect(await push.pushState(7)).toBe('off');
		expect(await push.enablePush(7, 'de')).toBe('on');
	});
});

describe('network', () => {
	it('follows online/offline and counts a reconnect only after a missed read', async () => {
		const win = emitter({});
		const nav = { onLine: true };
		vi.stubGlobal('window', win);
		vi.stubGlobal('navigator', nav);
		const { network, offlineError } = await import('./network.svelte');
		const stop = network.listen();
		expect(network.online).toBe(true);

		win.emit('offline');
		expect(network.online).toBe(false);
		win.emit('online');
		expect(network.reconnects).toBe(0); // nothing failed meanwhile

		win.emit('offline');
		network.markMissed();
		win.emit('online');
		expect(network.online).toBe(true);
		expect(network.reconnects).toBe(1);

		stop();
		expect(win.count('online')).toBe(0);
		expect(offlineError(true)).toMatchObject({ quiet: true, response: { data: { code: 'network.offline' } } });
	});

	it('starts online without a navigator (server side)', async () => {
		vi.stubGlobal('navigator', undefined);
		const { network } = await import('./network.svelte');
		expect(network.online).toBe(true);
	});
});
