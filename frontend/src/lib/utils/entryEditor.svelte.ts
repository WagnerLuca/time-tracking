import { timeTrackingApi, requestsApi } from '$lib/apiClient';
import type { TimeEntryResponse, UpdateTimeEntryRequest, OrganizationDetailResponse, EffectivePermissionsResponse } from '$lib/api';
import { RequestType } from '$lib/api';
import { toLocalDateTimeInput } from '$lib/utils/dateHelpers';
import { extractErrorMessage } from '$lib/utils/errorHandler';
import { canEditPause } from '$lib/utils/orgRules';
import { confirmDialog } from '$lib/stores/confirm.svelte';
import { toast } from '$lib/stores/toast.svelte';
import { t } from '$lib/i18n';

/**
 * Creates a reusable entry editing + request state manager.
 * Call once per page to get state and handlers.
 */
export function createEntryEditor(options: {
	getOrgDetail: () => OrganizationDetailResponse | null;
	/** The caller's effective permissions (group overrides applied); win over the org defaults. */
	getPermissions?: () => EffectivePermissionsResponse | null;
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
	let requestError = $state('');
	let requestNewStart = $state('');
	let requestNewEnd = $state('');
	let requestNewPause = $state(0);

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
			if (canEditPause(options.getPermissions?.(), options.getOrgDetail())) {
				payload.pauseDurationMinutes = Math.max(0, Number(editPause) || 0);
			}
			await timeTrackingApi.apiV1TimeTrackingIdPut(entryId, payload);
			editingEntryId = null;
			toast.success(t('entries.saved'));
			await options.onSaved();
		} catch (err) {
			editError = extractErrorMessage(err, t('entries.saveFailed'));
		} finally {
			editSaving = false;
		}
	}

	async function deleteEntry(id: number) {
		const confirmed = await confirmDialog({
			title: t('entries.deleteTitle'),
			message: t('entries.deleteMessage'),
			confirmLabel: t('common.delete'),
			danger: true
		});
		if (!confirmed) return;
		try {
			await timeTrackingApi.apiV1TimeTrackingIdDelete(id);
			if (editingEntryId === id) editingEntryId = null;
			toast.success(t('entries.deleted'));
			await options.onDeleted();
		} catch (err) {
			toast.error(err, t('entries.deleteFailed'));
		}
	}

	function startRequest(entryId: number, type: 'edit' | 'pause') {
		requestingEntryId = entryId;
		requestType = type;
		requestMessage = '';
		requestError = '';
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
		requestError = '';
	}

	async function submitRequest() {
		if (!requestingEntryId || !requestType) return;
		const slug = options.getOrgSlug();
		if (!slug) return;
		requestSending = true;
		requestError = '';
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
			toast.success(t('entries.request.submitted'));
			cancelRequest();
		} catch (err) {
			requestError = extractErrorMessage(err, t('entries.request.submitFailed'));
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
		get requestError() { return requestError; },
		get requestNewStart() { return requestNewStart; },
		set requestNewStart(v: string) { requestNewStart = v; },
		get requestNewEnd() { return requestNewEnd; },
		set requestNewEnd(v: string) { requestNewEnd = v; },
		get requestNewPause() { return requestNewPause; },
		set requestNewPause(v: number) { requestNewPause = v; },

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

export type EntryEditor = ReturnType<typeof createEntryEditor>;
