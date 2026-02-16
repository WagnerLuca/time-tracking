// Auth types
export interface UserInfo {
	id: number;
	email: string;
	firstName: string;
	lastName: string;
	profileImageUrl?: string;
	emailConfirmed: boolean;
}

export interface LoginRequest {
	email: string;
	password: string;
}

export interface RegisterRequest {
	email: string;
	password: string;
	firstName: string;
	lastName: string;
}

export interface LoginResponse {
	accessToken: string;
	refreshToken: string;
	expiresAt: string;
	user: UserInfo;
}

export interface AuthResponse {
	success: boolean;
	message: string;
}

// Organization types
export interface OrganizationResponse {
	id: number;
	name: string;
	description?: string;
	slug: string;
	website?: string;
	logoUrl?: string;
	createdAt: string;
	memberCount: number;
}

export interface OrganizationDetailResponse {
	id: number;
	name: string;
	description?: string;
	slug: string;
	website?: string;
	logoUrl?: string;
	autoPauseEnabled: boolean;
	allowEditPastEntries: boolean;
	createdAt: string;
	members: OrganizationMemberResponse[];
	pauseRules?: PauseRuleResponse[];
}

export interface OrganizationMemberResponse {
	id: number;
	email: string;
	firstName: string;
	lastName: string;
	profileImageUrl?: string;
	role: string;
	joinedAt: string;
}

export interface UserOrganizationResponse {
	organizationId: number;
	name: string;
	description?: string;
	slug: string;
	role: string;
	joinedAt: string;
	memberCount: number;
}

export interface CreateOrganizationRequest {
	name: string;
	description?: string;
	slug: string;
	website?: string;
	logoUrl?: string;
}

export interface UpdateOrganizationRequest {
	name?: string;
	description?: string;
	slug?: string;
	website?: string;
	logoUrl?: string;
}

export interface AddMemberRequest {
	userId: number;
	role: number; // 0=Member, 1=Admin, 2=Owner
}

export interface UpdateMemberRoleRequest {
	role: number;
}

// Time Tracking types
export interface StartTimeEntryRequest {
	description?: string;
	organizationId?: number;
}

export interface StopTimeEntryRequest {
	description?: string;
}

export interface TimeEntryResponse {
	id: number;
	userId: number;
	organizationId?: number;
	organizationName?: string;
	description?: string;
	startTime: string;
	endTime?: string;
	isRunning: boolean;
	durationMinutes?: number;
	pauseDurationMinutes: number;
	netDurationMinutes?: number;
	createdAt: string;
}

export interface UpdateTimeEntryRequest {
	startTime?: string;
	endTime?: string;
	description?: string;
	organizationId?: number;
}

// Pause Rule types
export interface PauseRuleResponse {
	id: number;
	organizationId: number;
	minHours: number;
	pauseMinutes: number;
	createdAt: string;
}

export interface CreatePauseRuleRequest {
	minHours: number;
	pauseMinutes: number;
}

export interface UpdatePauseRuleRequest {
	minHours: number;
	pauseMinutes: number;
}

// Organization Settings types
export interface UpdateOrganizationSettingsRequest {
	autoPauseEnabled?: boolean;
	allowEditPastEntries?: boolean;
}

// Work Schedule types
export interface WorkScheduleResponse {
	userId: number;
	organizationId: number;
	weeklyWorkHours?: number;
	targetMon: number;
	targetTue: number;
	targetWed: number;
	targetThu: number;
	targetFri: number;
}

export interface UpdateWorkScheduleRequest {
	weeklyWorkHours?: number;
	distributeEvenly: boolean;
	targetMon?: number;
	targetTue?: number;
	targetWed?: number;
	targetThu?: number;
	targetFri?: number;
}

// Admin Time Overview types
export interface MemberTimeOverviewResponse {
	userId: number;
	firstName: string;
	lastName: string;
	email: string;
	role: string;
	weeklyWorkHours?: number;
	totalTrackedMinutes: number;
	netTrackedMinutes: number;
	entryCount: number;
}
