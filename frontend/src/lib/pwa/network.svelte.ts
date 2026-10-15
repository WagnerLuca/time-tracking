/**
 * Online/offline state (issue #67). While offline the root layout shows a banner and locks the
 * page's controls (a disabled <fieldset>), and apiClient refuses requests at once instead of
 * waiting for a timeout. Reads that failed offline are retried on reconnect: `reconnects` counts
 * up and the layout remounts the page, so its loaders run again. Writes are never replayed.
 */
function createNetwork() {
	let online = $state(typeof navigator === 'undefined' ? true : navigator.onLine !== false);
	let reconnects = $state(0);
	let missed = false;

	function update(value: boolean) {
		online = value;
		if (value && missed) {
			missed = false;
			reconnects++;
		}
	}

	/** Follows the browser's online/offline events; returns the cleanup. */
	function listen(): () => void {
		const on = () => update(true);
		const off = () => update(false);
		window.addEventListener('online', on);
		window.addEventListener('offline', off);
		update(navigator.onLine !== false);
		return () => {
			window.removeEventListener('online', on);
			window.removeEventListener('offline', off);
		};
	}

	return {
		get online() { return online; },
		/** Bumped when the connection is back after a read failed offline. */
		get reconnects() { return reconnects; },
		/** A read failed offline: reload the page once the connection is back. */
		markMissed() { missed = true; },
		listen
	};
}

export const network = createNetwork();

/** The error apiClient answers with while offline; `quiet` reads are explained by the banner, not a toast. */
export function offlineError(quiet: boolean) {
	return Object.assign(new Error('offline'), { quiet, response: { data: { code: 'network.offline' } } });
}
