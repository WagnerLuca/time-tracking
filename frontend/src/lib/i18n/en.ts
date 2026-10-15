/** English, loaded on demand by `dictionaries.ts` — never import this file statically (it would land in the initial bundle). */
import { en as common } from './messages/common/en';
import { en as format } from './messages/format/en';
import { en as nav } from './messages/nav/en';
import { en as auth } from './messages/auth/en';
import { en as dashboard } from './messages/dashboard/en';
import { en as timer } from './messages/timer/en';
import { en as entries } from './messages/entries/en';
import { en as calendar } from './messages/calendar/en';
import { en as absences } from './messages/absences/en';
import { en as orgs } from './messages/orgs/en';
import { en as orgHome } from './messages/orgHome/en';
import { en as schedule } from './messages/schedule/en';
import { en as memberList } from './messages/memberList/en';
import { en as memberDetail } from './messages/memberDetail/en';
import { en as timeOverview } from './messages/timeOverview/en';
import { en as groups } from './messages/groups/en';
import { en as holidays } from './messages/holidays/en';
import { en as orgSettings } from './messages/orgSettings/en';
import { en as settings } from './messages/settings/en';
import { en as exportPage } from './messages/export/en';
import { en as errors } from './messages/errors/en';
import { en as invitations } from './messages/invitations/en';
import { en as visibility } from './messages/visibility/en';
import { en as inbox } from './messages/inbox/en';
import { en as onboarding } from './messages/onboarding/en';
import { en as closedPeriods } from './messages/closedPeriods/en';
import { en as orgChart } from './messages/orgChart/en';
import type { Dictionary } from './de';

export const en: Dictionary = {
	common,
	format,
	nav,
	auth,
	dashboard,
	timer,
	entries,
	calendar,
	absences,
	orgs,
	orgHome,
	schedule,
	memberList,
	memberDetail,
	timeOverview,
	groups,
	holidays,
	orgSettings,
	settings,
	export: exportPage,
	errors,
	invitations,
	visibility,
	inbox,
	onboarding,
	closedPeriods,
	orgChart
};
