import { timeTrackingApi, requestsApi } from '$lib/apiClient';
import type { TimeEntryResponse, UpdateTimeEntryRequest, OrganizationDetailResponse } from '$lib/api';
import { RequestType } from '$lib/api';
import { toLocalDateTimeInput } from '$lib/utils/dateHelpers';
import { extractErrorMessage } from '$lib/utils/errorHandler';
import { canEditPause } from '$lib/utils/orgRules';

/**
 * Creates a reusable entry editing + request state manager.
 * Call once per page to get state and handlers.
 */
export function createEntryEditor(options: {
	getOrgDetail: () => OrganizationDetailResponse | null;
	getOrgSlug: () => string | null | undefined;
	onSaved: () => Promise<void>;
	onDeleted: () => Promise<void>;
	findEntry: (id: number) => TimeEntryResponse | undefined;
}) {
	// Edit state
	let editingEntryId = $state<number | null>(null);
	let editStartTime = $state('');
	let editEndTime = $state('');
	let editDescription = $state('');
	let editPause = $state<number>(0);
	let editError = $state('');
	let editSaving = $state(false);

	// Request state
	let requestingEntryId = $state<number | null>(null);
	let requestType = $state<'edit' | 'pause' | null>(null);
	let requestMessage = $state('');
	let requestSending = $state(false);
	let requestSuccess = $state('');
	let requestNewStart = $state('');
	let requestNewEnd = $state('');
	let requestNewPause = $state(0);
	let actionError = $state('');

	function startEditEntry(entry: TimeEntryResponse) {
		editingEntryId = entry.id ?? null;
		editStartTime = toLocalDateTimeInput(entry.startTime!);
		editEndTime = entry.endTime ? toLocalDateTimeInput(entry.endTime) : '';
		editDescription = entry.description ?? '';
		editPause = entry.pauseDurationMinutes ?? 0;
		editError = '';
	}

	function cancelEditEntry() {
		editingEntryId = null;
		editError = '';
	}

	async function saveEditEntry(entryId: number) {
		editError = '';
		editSaving = true;
		try {
			const payload: UpdateTimeEntryRequest = {};
			if (editStartTime) payload.startTime = new Date(editStartTime).toISOString();
			if (editEndTime) payload.endTime = new Date(editEndTime).toISOString();
			payload.description = editDescription.trim() || undefined;
			if (canEditPause(options.getOrgDetail())) {
				payload.pauseDurationMinutes = Math.max(0, Number(editPause) || 0);
			}
			await timeTrackingApi.apiV1TimeTrackingIdPut(entryId, payload);
			await options.onSaved();
			editingEntryId = null;
		} catch (err) {
			editError = extractErrorMessage(err, 'Failed to update entry.');
		} finally {
			editSaving = false;
		}
	}

	async function deleteEntry(id: number) {
		if (!confirm('Delete this time entry?')) return;
		try {
			await timeTrackingApi.apiV1TimeTrackingIdDelete(id);
			await options.onDeleted();
		} catch (err) {
			actionError = extractErrorMessage(err, 'Failed to delete entry.');
		}
	}

	function startRequest(entryId: number, type: 'edit' | 'pause') {
		requestingEntryId = entryId;
		requestType = type;
		requestMessage = '';
		requestSuccess = '';
		const entry = options.findEntry(entryId);
		if (entry) {
			if (type === 'edit') {
				requestNewStart = entry.startTime ? toLocalDateTimeInput(entry.startTime) : '';
				requestNewEnd = entry.endTime ? toLocalDateTimeInput(entry.endTime) : '';
			} else {
				requestNewPause = entry.pauseDurationMinutes ?? 0;
			}
		}
	}

	function cancelRequest() {
		requestingEntryId = null;
		requestType = null;
		requestMessage = '';
	}

	async function submitRequest() {
		if (!requestingEntryId || !requestType) return;
		const slug = options.getOrgSlug();
		if (!slug) return;
		requestSending = true;
		try {
			const rType = requestType === 'edit' ? RequestType.NUMBER_1 : RequestType.NUMBER_2;
			let requestData: string | undefined;
			if (requestType === 'edit') {
				const data: Record<string, string> = {};
				if (requestNewStart) data.startTime = new Date(requestNewStart).toISOString();
				if (requestNewEnd) data.endTime = new Date(requestNewEnd).toISOString();
				requestData = JSON.stringify(data);
			} else {
				requestData = String(Math.max(0, requestNewPause));
			}
			await requestsApi.apiV1OrganizationsSlugRequestsPost(slug, {
				type: rType,
				relatedEntityId: requestingEntryId,
				requestData,
				message: requestMessage || undefined
			});
			requestSuccess = 'Request submitted! An admin will review it.';
			requestingEntryId = null;
			requestType = null;
			requestMessage = '';
			setTimeout(() => (requestSuccess = ''), 4000);
		} catch (err) {
			actionError = extractErrorMessage(err, 'Failed to submit request.');
		} finally {
			requestSending = false;
		}
	}

	return {
		// Edit state (getters)
		get editingEntryId() { return editingEntryId; },
		get editStartTime() { return editStartTime; },
		set editStartTime(v: string) { editStartTime = v; },
		get editEndTime() { return editEndTime; },
		set editEndTime(v: string) { editEndTime = v; },
		get editDescription() { return editDescription; },
		set editDescription(v: string) { editDescription = v; },
		get editPause() { return editPause; },
		set editPause(v: number) { editPause = v; },
		get editError() { return editError; },
		get editSaving() { return editSaving; },

		// Request state (getters)
		get requestingEntryId() { return requestingEntryId; },
		get requestType() { return requestType; },
		get requestMessage() { return requestMessage; },
		set requestMessage(v: string) { requestMessage = v; },
		get requestSending() { return requestSending; },
		get requestSuccess() { return requestSuccess; },
		get requestNewStart() { return requestNewStart; },
		set requestNewStart(v: string) { requestNewStart = v; },
		get requestNewEnd() { return requestNewEnd; },
		set requestNewEnd(v: string) { requestNewEnd = v; },
		get requestNewPause() { return requestNewPause; },
		set requestNewPause(v: number) { requestNewPause = v; },
		get actionError() { return actionError; },
		set actionError(v: string) { actionError = v; },

		// Methods
		startEditEntry,
		cancelEditEntry,
		saveEditEntry,
		deleteEntry,
		startRequest,
		cancelRequest,
		submitRequest
	};
}
