import { timeTrackingApi, requestsApi, memberEntriesApi } from '$lib/apiClient';
import type { TimeEntryResponse, UpdateTimeEntryRequest, OrganizationDetailResponse, EffectivePermissionsResponse } from '$lib/api';
import { RequestType } from '$lib/api';
import { toLocalDateTimeInput } from '$lib/utils/dateHelpers';
import { extractErrorMessage, getErrorCode, getErrorStatus } from '$lib/utils/errorHandler';
import { canEditPause } from '$lib/utils/orgRules';
import { defaultEntryTimes, newEntryPayload, warningMessages } from '$lib/utils/entryPayloads';
import { confirmDialog } from '$lib/stores/confirm.svelte';
import { toast } from '$lib/stores/toast.svelte';
import { t } from '$lib/i18n';

/** Working Time Act warnings of a saved entry, as toasts that stay a little longer. */
export function toastEntryWarnings(entry: TimeEntryResponse | null | undefined) {
	warningMessages(entry?.warnings).forEach((w) => toast.show('warning', w, 10_000));
}

/**
 * Creates a reusable entry editing + request state manager.
 * Call once per page to get state and handlers.
 *
 * Two modes: the caller's own entries (default; manual entries may turn into a request),
 * or — with `getMemberId` — an org/group admin correcting another member's entries through
 * the admin endpoints (pause always editable there).
 */
export function createEntryEditor(options: {
	getOrgDetail: () => OrganizationDetailResponse | null;
	/** The caller's effective permissions (group overrides applied); win over the org defaults. */
	getPermissions?: () => EffectivePermissionsResponse | null;
	getOrgSlug: () => string | null | undefined;
	/** The organization manual entries are added to (own entries). */
	getOrgId?: () => number | null | undefined;
	/** Admin corrections: the member whose entries are edited; null/absent = own entries. */
	getMemberId?: () => number | null | undefined;
	onSaved: () => Promise<void>;
	onDeleted: () => Promise<void>;
	findEntry: (id: number) => TimeEntryResponse | undefined;
}) {
	const memberId = () => options.getMemberId?.() ?? null;
	const pauseEditable = () => memberId() != null || canEditPause(options.getPermissions?.(), options.getOrgDetail());

	// Edit state
	let editingEntryId = $state<number | null>(null);
	let editStartTime = $state('');
	let editEndTime = $state('');
	let editDescription = $state('');
	let editPause = $state<number | null>(0);
	let editError = $state('');
	let editSaving = $state(false);

	// New entry state (manual entry / admin addition); `creatingDay` = 'YYYY-MM-DD' while the form is open
	let creatingDay = $state<string | null>(null);
	let createStart = $state('');
	let createEnd = $state('');
	let createDescription = $state('');
	let createPause = $state<number | null>(null);
	let createError = $state('');
	let createSaving = $state(false);

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
		creatingDay = null;
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
			if (pauseEditable()) payload.pauseDurationMinutes = Math.max(0, Number(editPause) || 0);
			const member = memberId();
			const slug = options.getOrgSlug();
			const { data } = member != null && slug
				? await memberEntriesApi.apiV1OrganizationsSlugMembersMemberIdEntriesEntryIdPut(slug, member, entryId, payload)
				: await timeTrackingApi.apiV1TimeTrackingIdPut(entryId, payload);
			editingEntryId = null;
			toast.success(t('entries.saved'));
			toastEntryWarnings(data);
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
			const member = memberId();
			const slug = options.getOrgSlug();
			if (member != null && slug) await memberEntriesApi.apiV1OrganizationsSlugMembersMemberIdEntriesEntryIdDelete(slug, member, id);
			else await timeTrackingApi.apiV1TimeTrackingIdDelete(id);
			if (editingEntryId === id) editingEntryId = null;
			toast.success(t('entries.deleted'));
			await options.onDeleted();
		} catch (err) {
			toast.error(err, t('entries.deleteFailed'));
		}
	}

	/** Opens the new-entry form for `day` ('YYYY-MM-DD'), prefilled 09:00–17:00. */
	function startCreate(day: string) {
		editingEntryId = null;
		const times = defaultEntryTimes(day);
		creatingDay = day;
		createStart = times.start;
		createEnd = times.end;
		createDescription = '';
		createPause = null;
		createError = '';
	}

	function cancelCreate() {
		creatingDay = null;
		createError = '';
	}

	async function saveCreate() {
		if (!createEnd) {
			createError = t('entries.manual.endRequired');
			return;
		}
		createError = '';
		createSaving = true;
		try {
			const form = { start: createStart, end: createEnd, description: createDescription, pause: createPause };
			const member = memberId();
			const slug = options.getOrgSlug();
			if (member != null && slug) {
				const { data } = await memberEntriesApi.apiV1OrganizationsSlugMembersMemberIdEntriesPost(slug, member, newEntryPayload(form, true));
				toast.success(t('entries.manual.created'));
				toastEntryWarnings(data);
			} else {
				const { data } = await timeTrackingApi.apiV1TimeTrackingManualPost({
					...newEntryPayload(form, pauseEditable()),
					organizationId: options.getOrgId?.() ?? null
				});
				if (data.request) toast.info(t('entries.manual.requested'));
				else {
					toast.success(t('entries.manual.created'));
					toastEntryWarnings(data.entry);
				}
			}
			creatingDay = null;
			await options.onSaved();
		} catch (err) {
			// The manual-entry endpoint's 409 (overlap) and 403 (disabled) carry no code yet
			const status = getErrorStatus(err);
			createError = status === 409 && !getErrorCode(err)
				? t('entries.manual.overlap')
				: extractErrorMessage(err, t('entries.manual.failed'));
		} finally {
			createSaving = false;
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
		set editPause(v: number | null) { editPause = v; },
		get editError() { return editError; },
		get editSaving() { return editSaving; },

		// New entry state
		get creatingDay() { return creatingDay; },
		get createStart() { return createStart; },
		set createStart(v: string) { createStart = v; },
		get createEnd() { return createEnd; },
		set createEnd(v: string) { createEnd = v; },
		get createDescription() { return createDescription; },
		set createDescription(v: string) { createDescription = v; },
		get createPause() { return createPause; },
		set createPause(v: number | null) { createPause = v; },
		get createError() { return createError; },
		get createSaving() { return createSaving; },
		/** Whether the pause field applies (own entries: the edit-pause rule; admin corrections: always). */
		get pauseEditable() { return pauseEditable(); },

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
		startCreate,
		cancelCreate,
		saveCreate,
		startRequest,
		cancelRequest,
		submitRequest
	};
}

export type EntryEditor = ReturnType<typeof createEntryEditor>;
