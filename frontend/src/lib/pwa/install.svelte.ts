/**
 * "App installieren" (issue #67): keeps Chrome's `beforeinstallprompt` so the settings and the
 * "Mehr" menu can open the install dialog; Safari on iOS has no such event, so the UI explains
 * "Zum Home-Bildschirm" there instead. The browser's own install prompt stays untouched.
 */
interface BeforeInstallPromptEvent extends Event {
	prompt(): Promise<void>;
	userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

function createInstall() {
	let deferred = $state<BeforeInstallPromptEvent | null>(null);
	let installed = $state(false);
	let ios = $state(false);

	/** Starts listening; call once on startup (the event may come early). Returns the cleanup. */
	function listen(): () => void {
		const nav = navigator as Navigator & { standalone?: boolean };
		installed = matchMedia('(display-mode: standalone)').matches || nav.standalone === true;
		// iPadOS reports itself as a Mac with touch
		ios = /iphone|ipad|ipod/i.test(nav.userAgent) || (nav.platform === 'MacIntel' && nav.maxTouchPoints > 1);
		const onPrompt = (e: Event) => (deferred = e as BeforeInstallPromptEvent);
		const onInstalled = () => {
			installed = true;
			deferred = null;
		};
		window.addEventListener('beforeinstallprompt', onPrompt);
		window.addEventListener('appinstalled', onInstalled);
		return () => {
			window.removeEventListener('beforeinstallprompt', onPrompt);
			window.removeEventListener('appinstalled', onInstalled);
		};
	}

	/** Opens the browser's install dialog; true when the user installed. */
	async function prompt(): Promise<boolean> {
		const event = deferred;
		if (!event) return false;
		deferred = null; // a prompt event can be used once
		await event.prompt();
		const accepted = (await event.userChoice).outcome === 'accepted';
		if (accepted) installed = true;
		return accepted;
	}

	return {
		/** The browser offered installation (Chrome, Edge, Samsung Internet …). */
		get canPrompt() { return deferred !== null; },
		/** Running as the installed app (standalone window). */
		get installed() { return installed; },
		/** iPhone/iPad: install via Safari's share menu. */
		get ios() { return ios; },
		listen,
		prompt
	};
}

export const install = createInstall();
