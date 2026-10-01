import * as common from './messages/common';
import * as format from './messages/format';
import * as nav from './messages/nav';
import * as auth from './messages/auth';
import * as dashboard from './messages/dashboard';
import * as timer from './messages/timer';
import * as entries from './messages/entries';
import * as calendar from './messages/calendar';
import * as absences from './messages/absences';
import * as orgs from './messages/orgs';
import * as orgHome from './messages/orgHome';
import * as schedule from './messages/schedule';
import * as memberList from './messages/memberList';
import * as memberDetail from './messages/memberDetail';
import * as timeOverview from './messages/timeOverview';
import * as groups from './messages/groups';
import * as holidays from './messages/holidays';
import * as orgSettings from './messages/orgSettings';
import * as settings from './messages/settings';
import * as exportPage from './messages/export';
import * as errors from './messages/errors';

/** German is the default and the fallback for any missing English string. */
export const de = {
	common: common.de,
	format: format.de,
	nav: nav.de,
	auth: auth.de,
	dashboard: dashboard.de,
	timer: timer.de,
	entries: entries.de,
	calendar: calendar.de,
	absences: absences.de,
	orgs: orgs.de,
	orgHome: orgHome.de,
	schedule: schedule.de,
	memberList: memberList.de,
	memberDetail: memberDetail.de,
	timeOverview: timeOverview.de,
	groups: groups.de,
	holidays: holidays.de,
	orgSettings: orgSettings.de,
	settings: settings.de,
	export: exportPage.de,
	errors: errors.de
};

export type Dictionary = typeof de;
