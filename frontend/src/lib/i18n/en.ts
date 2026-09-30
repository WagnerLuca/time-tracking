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
import type { Dictionary } from './de';

export const en: Dictionary = {
	common: common.en,
	format: format.en,
	nav: nav.en,
	auth: auth.en,
	dashboard: dashboard.en,
	timer: timer.en,
	entries: entries.en,
	calendar: calendar.en,
	absences: absences.en,
	orgs: orgs.en,
	orgHome: orgHome.en,
	schedule: schedule.en,
	memberList: memberList.en,
	memberDetail: memberDetail.en,
	timeOverview: timeOverview.en,
	groups: groups.en,
	holidays: holidays.en,
	orgSettings: orgSettings.en,
	settings: settings.en,
	export: exportPage.en
};
