import { timeTrackingApi } from '$lib/apiClient';
import { getErrorStatus } from '$lib/utils/errorHandler';
import { t } from '$lib/i18n';
import { toastEntryWarnings } from '$lib/utils/entryEditor.svelte';
import type { StartTimeEntryRequest, StopTimeEntryRequest, TimeEntryResponse } from '$lib/api';

/**
 * The caller's running timer, shared by the nav indicator, the dashboard and
 * the timer page so they never disagree. Ticks once a second while running.
 * Note: POST /time-tracking/start stops an already running timer first (backend).
 */
function createTimerStore() {
	let current = $state<TimeEntryResponse | null>(null);
	let now = $state(Date.now());
	let loaded = $state(false);
	let interval: ReturnType<typeof setInterval> | null = null;

	const elapsedSeconds = $derived(
		current?.startTime ? Math.max(0, Math.floor((now - new Date(current.startTime).getTime()) / 1000)) : 0
	);

	function setCurrent(entry: TimeEntryResponse | null) {
		current = entry;
		now = Date.now();
		if (entry && !interval) {
			interval = setInterval(() => (now = Date.now()), 1000);
		} else if (!entry && interval) {
			clearInterval(interval);
			interval = null;
		}
	}

	/** Reload the running entry from the server (errors leave the last known state). */
	async function refresh(): Promise<void> {
		try {
			const { data } = await timeTrackingApi.apiV1TimeTrackingCurrentGet();
			// The endpoint answers 204/empty when nothing runs
			setCurrent(data && (data as TimeEntryResponse).id ? data : null);
		} finally {
			loaded = true;
		}
	}

	async function start(request: StartTimeEntryRequest): Promise<TimeEntryResponse> {
		try {
			const { data } = await timeTrackingApi.apiV1TimeTrackingStartPost(request);
			setCurrent(data);
			return data;
		} catch (err) {
			// 409: a timer was started meanwhile (another tab, a terminal) — show that one instead
			if (getErrorStatus(err) === 409) {
				await refresh().catch(() => {});
				throw new Error(t('timer.alreadyRunning'));
			}
			throw err;
		}
	}

	async function stop(request: StopTimeEntryRequest = {}): Promise<void> {
		const { data } = await timeTrackingApi.apiV1TimeTrackingStopPost(request);
		setCurrent(null);
		// Working Time Act (more than 10 h that day, under 11 h rest): tell the user right away
		toastEntryWarnings(data);
	}

	function reset() {
		setCurrent(null);
		loaded = false;
	}

	return {
		get current() { return current; },
		get running() { return !!current; },
		get loaded() { return loaded; },
		get elapsedSeconds() { return elapsedSeconds; },
		get elapsedMinutes() { return elapsedSeconds / 60; },
		refresh,
		start,
		stop,
		reset
	};
}

export const timer = createTimerStore();
