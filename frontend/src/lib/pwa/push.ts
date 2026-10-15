/**
 * Web Push opt-in per device (issue #67). Permission is only ever asked from a click
 * (`enablePush`). The device belongs to the account that enabled it (`wl-push-user`): signing
 * out turns it off, and `syncPush` on startup drops a subscription another account left behind
 * and re-registers this one (its language, fresh keys).
 */
import { pushApi } from '$lib/apiClient';

export type PushState = 'unsupported' | 'denied' | 'off' | 'on';

const USER_KEY = 'wl-push-user';

function storedUser(): string | null {
	try { return localStorage.getItem(USER_KEY); } catch { return null; }
}
function setStoredUser(userId: number | null) {
	try {
		if (userId == null) localStorage.removeItem(USER_KEY);
		else localStorage.setItem(USER_KEY, String(userId));
	} catch { /* storage blocked */ }
}

/** The service worker registration, or null (no support, or the dev server, which has none). */
async function registration(): Promise<ServiceWorkerRegistration | null> {
	if (typeof window === 'undefined' || !('serviceWorker' in navigator) || !('PushManager' in window) || !('Notification' in window))
		return null;
	return (await navigator.serviceWorker.getRegistration()) ?? null;
}

/** VAPID public key (base64url) → the bytes PushManager.subscribe wants. */
export function base64UrlToBytes(value: string): Uint8Array<ArrayBuffer> {
	const base64 = (value + '='.repeat((4 - (value.length % 4)) % 4)).replace(/-/g, '+').replace(/_/g, '/');
	return Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
}

async function register(sub: PushSubscription, locale: string) {
	const json = sub.toJSON();
	await pushApi.apiV1PushSubscriptionsPost({ endpoint: json.endpoint!, p256dh: json.keys!.p256dh, auth: json.keys!.auth, locale });
}

export async function pushState(userId: number | null | undefined): Promise<PushState> {
	const reg = await registration();
	if (!reg) return 'unsupported';
	if (Notification.permission === 'denied') return 'denied';
	const sub = await reg.pushManager.getSubscription();
	return sub && Notification.permission === 'granted' && storedUser() === String(userId) ? 'on' : 'off';
}

/** Asks for permission (call from a click only) and registers this device for `userId`. */
export async function enablePush(userId: number, locale: string): Promise<PushState> {
	const reg = await registration();
	if (!reg) return 'unsupported';
	const permission = await Notification.requestPermission();
	if (permission !== 'granted') return permission === 'denied' ? 'denied' : 'off';
	const { data } = await pushApi.apiV1PushPublicKeyGet();
	if (!data.publicKey) throw { response: { data: { code: 'push.notConfigured' } } };
	// A fresh subscription: never reuse one from another account or an old server key
	await (await reg.pushManager.getSubscription())?.unsubscribe();
	const sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: base64UrlToBytes(data.publicKey) });
	await register(sub, locale);
	setStoredUser(userId);
	return 'on';
}

/** Turns push off on this device (server first, so nothing more is sent; the browser side always). */
export async function disablePush(): Promise<void> {
	const sub = await (await registration())?.pushManager.getSubscription();
	setStoredUser(null);
	if (!sub) return;
	try {
		await pushApi.apiV1PushSubscriptionsRemovePost({ endpoint: sub.endpoint });
	} finally {
		await sub.unsubscribe();
	}
}

/** On startup / language change: keep this device registered, or drop another account's subscription. */
export async function syncPush(userId: number, locale: string): Promise<void> {
	const sub = await (await registration())?.pushManager.getSubscription();
	if (!sub) return;
	if (storedUser() !== String(userId) || Notification.permission !== 'granted') {
		setStoredUser(null);
		await sub.unsubscribe();
		return;
	}
	await register(sub, locale);
}
