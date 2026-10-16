import { de as common } from './messages/common/de';
import { de as format } from './messages/format/de';
import { de as nav } from './messages/nav/de';
import { de as auth } from './messages/auth/de';
import { de as dashboard } from './messages/dashboard/de';
import { de as timer } from './messages/timer/de';
import { de as entries } from './messages/entries/de';
import { de as calendar } from './messages/calendar/de';
import { de as absences } from './messages/absences/de';
import { de as orgs } from './messages/orgs/de';
import { de as orgHome } from './messages/orgHome/de';
import { de as schedule } from './messages/schedule/de';
import { de as memberList } from './messages/memberList/de';
import { de as memberDetail } from './messages/memberDetail/de';
import { de as timeOverview } from './messages/timeOverview/de';
import { de as groups } from './messages/groups/de';
import { de as holidays } from './messages/holidays/de';
import { de as orgSettings } from './messages/orgSettings/de';
import { de as settings } from './messages/settings/de';
import { de as exportPage } from './messages/export/de';
import { de as errors } from './messages/errors/de';
import { de as invitations } from './messages/invitations/de';
import { de as visibility } from './messages/visibility/de';
import { de as inbox } from './messages/inbox/de';
import { de as onboarding } from './messages/onboarding/de';
import { de as closedPeriods } from './messages/closedPeriods/de';
import { de as orgChart } from './messages/orgChart/de';
import { de as pwa } from './messages/pwa/de';
import { de as accounting } from './messages/accounting/de';

/** German is the default and the fallback for any missing English string. */
export const de = {
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
	orgChart,
	pwa,
	accounting
};

export type Dictionary = typeof de;
