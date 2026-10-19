// k6 load test for one perf-seed organization (docs/PERFORMANCE_PLAN.md, Phase 0.3). See loadtest/README.md.
//
//   morning_rush  100 users start their timer within 15 min (ramping arrival rate), every second one by
//                 badge at the terminal (POST terminal/punch with an API key + cards created in setup)
//   polling       every user polls the inbox (5 calls) every 60 s, balance on the first "page load";
//                 10 admin sessions poll the team overview every 60 s
//   browsing      users open dashboard / time / history with the calls the frontend makes for them
//   month_end     2 accountants: summary, payroll CSV, vacation overview, monthly PDFs of 10 members
//
// Env: BASE_URL (http://localhost:7000), ORG_SLUG (perf-100-10), USERS (100), DURATION (15m),
// BROWSE_RATE (page views per minute, 30), SCENARIOS (comma list, default all), LOGINS_PER_MINUTE (9),
// PASSWORD (required: the one perf-seed printed).
import http from 'k6/http';
import exec from 'k6/execution';
import { check, sleep, fail } from 'k6';
import { API, auth, emails, login, loginAll } from './lib/auth.js';

const SLUG = __ENV.ORG_SLUG || 'perf-100-10';
const USERS = Number(__ENV.USERS || 100);
const DURATION = __ENV.DURATION || '15m';
const BROWSE_RATE = Number(__ENV.BROWSE_RATE || 30);
const ENABLED = (__ENV.SCENARIOS || 'morning_rush,polling,polling_team,browsing,month_end').split(',').map((s) => s.trim());

const allScenarios = {
  morning_rush: {
    executor: 'ramping-arrival-rate',
    exec: 'morningRush',
    timeUnit: '1m',
    startRate: 0,
    preAllocatedVUs: 20,
    maxVUs: 50,
    // ≈ 120 arrivals in 15 min; iterations beyond the number of users do nothing
    stages: [
      { duration: '3m', target: 10 },
      { duration: '9m', target: 10 },
      { duration: '3m', target: 0 }
    ]
  },
  polling: {
    executor: 'constant-vus',
    exec: 'polling',
    vus: USERS,
    duration: DURATION
  },
  polling_team: {
    executor: 'constant-vus',
    exec: 'pollingTeam',
    vus: 10,
    duration: DURATION
  },
  browsing: {
    executor: 'constant-arrival-rate',
    exec: 'browsing',
    rate: BROWSE_RATE,
    timeUnit: '1m',
    duration: DURATION,
    preAllocatedVUs: 10,
    maxVUs: 50
  },
  month_end: {
    executor: 'per-vu-iterations',
    exec: 'monthEnd',
    vus: 2,
    iterations: 1,
    startTime: '5m',
    maxDuration: '10m'
  }
};

export const options = {
  scenarios: Object.fromEntries(Object.entries(allScenarios).filter(([name]) => ENABLED.includes(name))),
  setupTimeout: '30m',
  teardownTimeout: '10m',
  thresholds: {
    'http_req_duration{kind:interactive}': ['p(95)<300'],
    'http_req_duration{kind:export}': ['p(95)<2000'],
    // Setup/teardown requests (kind:setup) may answer 409 (a card from an earlier run) and are not counted
    'http_req_failed{kind:interactive}': ['rate<0.001'],
    'http_req_failed{kind:export}': ['rate<0.001']
  },
  summaryTrendStats: ['avg', 'p(50)', 'p(95)', 'p(99)', 'max']
};

// ── Dates (UTC approximations of the org-local ranges the frontend asks for) ──────────────

const day = (d) => d.toISOString().slice(0, 10);
function weekRange() {
  const now = new Date();
  const monday = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() - ((now.getUTCDay() + 6) % 7)));
  return { from: monday.toISOString(), to: new Date(monday.getTime() + 7 * 86400000).toISOString() };
}
function monthRange(offset = 0) {
  const now = new Date();
  const first = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + offset, 1));
  const last = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + offset + 1, 0));
  return { first, last, year: first.getUTCFullYear(), month: first.getUTCMonth() + 1 };
}

// ── Setup: logins, terminal key + cards ───────────────────────────────────────────────────

export function setup() {
  const owner = login(emails.owner(SLUG));
  const detail = http.get(`${API}/Organizations/${SLUG}`, auth(owner, 'org detail', 'setup'));
  if (detail.status !== 200) fail(`organization ${SLUG}: ${detail.status} — seed it with perf-seed first`);
  const org = detail.json();
  const number = (email) => Number(/perf-user(\d+)@/.exec(email)?.[1] || 0);
  const memberEmails = org.members
    .filter((m) => m.email && m.email.startsWith('perf-user'))
    .sort((a, b) => number(a.email) - number(b.email))
    .slice(0, USERS)
    .map((m) => m.email);
  const adminEmails = org.members.filter((m) => m.role === 'Admin' && m.email).map((m) => m.email);

  const admins = [owner, ...loginAll(adminEmails)];
  const users = loginAll(memberEmails);

  // A terminal key for this run and a card for every second user (409 = the card exists from an earlier run)
  let apiKey = null;
  let apiKeyId = null;
  const cards = {};
  if (ENABLED.includes('morning_rush')) {
    const key = http.post(`${API}/organizations/${SLUG}/api-keys`, JSON.stringify({ name: `k6 ${new Date().toISOString()}` }),
      auth(owner, 'api key', 'setup'));
    if (key.status !== 201 && key.status !== 200) fail(`api key: ${key.status} ${key.body}`);
    apiKey = key.json().key;
    apiKeyId = key.json().id;
    users.forEach((u, i) => {
      if (i % 2 !== 0) return;
      const cardId = `K6${u.id.toString(16).toUpperCase().padStart(6, '0')}`;
      http.post(`${API}/organizations/${SLUG}/members/${u.id}/cards`, JSON.stringify({ cardId, label: 'k6' }),
        auth(owner, 'card', 'setup', { responseCallback: http.expectedStatuses(200, 201, 409) }));
      cards[u.id] = cardId;
    });
  }
  return { orgId: org.id, owner, admins, users, apiKey, apiKeyId, cards };
}

export function teardown(data) {
  // Leave no timers running and no extra terminal key behind (re-runnable)
  for (const u of data.users) http.post(`${API}/TimeTracking/stop`, '{}', auth(u, 'stop (teardown)', 'setup', { responseCallback: http.expectedStatuses(200, 400, 404) }));
  if (data.apiKeyId) http.del(`${API}/organizations/${SLUG}/api-keys/${data.apiKeyId}`, null, auth(data.owner, 'api key', 'setup'));
}

// ── Scenarios ─────────────────────────────────────────────────────────────────────────────

const ok = (res, name, also = []) =>
  check(res, { [`${name} 2xx`]: (r) => (r.status >= 200 && r.status < 300) || also.includes(r.status) });

export function morningRush(data) {
  const i = exec.scenario.iterationInTest;
  if (i >= data.users.length) return;
  const user = data.users[i];
  const card = data.cards[user.id];
  if (card) {
    const res = http.post(`${API}/terminal/punch`, JSON.stringify({ badgeId: card }), {
      headers: { 'X-Api-Key': data.apiKey, 'Content-Type': 'application/json' },
      tags: { name: 'terminal punch', kind: 'interactive' }
    });
    ok(res, 'terminal punch');
  } else {
    ok(http.post(`${API}/TimeTracking/start`, JSON.stringify({ organizationSlug: SLUG }), auth(user, 'timer start')), 'timer start');
  }
}

function inbox(user) {
  ok(http.get(`${API}/organizations/notifications`, auth(user, 'inbox: approvals')), 'inbox approvals');
  ok(http.get(`${API}/organizations/user-notifications`, auth(user, 'inbox: user-notifications')), 'inbox decisions');
  ok(http.get(`${API}/organizations/my-requests?limit=50`, auth(user, 'inbox: my-requests')), 'inbox my requests');
  ok(http.get(`${API}/Notifications?unreadOnly=true&limit=50`, auth(user, 'inbox: notifications')), 'inbox notifications');
  ok(http.get(`${API}/TimeTracking/failures`, auth(user, 'inbox: failures')), 'inbox failures');
}

const balance = (user) =>
  ok(http.get(`${API}/organizations/${SLUG}/members/${user.id}/balance`, auth(user, 'balance')), 'balance');

// One user (admin) per VU of a polling scenario: a VU's first iteration number is unique within its scenario,
// and all VUs of a constant-vus scenario start at once, so the first iterations are 0..vus-1
let pollingUser = null;

export function polling(data) {
  if (pollingUser === null) pollingUser = data.users[exec.scenario.iterationInTest % data.users.length];
  const user = pollingUser;
  if (exec.vu.iterationInScenario === 0) {
    sleep(Math.random() * 60);   // spread the sessions over the first minute
    balance(user);
  }
  inbox(user);
  sleep(60);
}

export function pollingTeam(data) {
  if (pollingUser === null) pollingUser = data.admins[exec.scenario.iterationInTest % data.admins.length];
  const admin = pollingUser;
  if (exec.vu.iterationInScenario === 0) sleep(Math.random() * 60);
  ok(http.get(`${API}/organizations/${SLUG}/team-overview`, auth(admin, 'team-overview')), 'team overview');
  sleep(60);
}

export function browsing(data) {
  const user = data.users[Math.floor(Math.random() * data.users.length)];
  const week = weekRange();
  const month = monthRange();
  const page = ['dashboard', 'time', 'history'][Math.floor(Math.random() * 3)];
  const get = (path, name) => ok(http.get(`${API}/${path}`, auth(user, name)), name);
  // App shell on every page load; no running timer is a 404
  get('Organizations/mine', 'organizations mine');
  ok(http.get(`${API}/TimeTracking/current`, auth(user, 'timer current', 'interactive', { responseCallback: http.expectedStatuses(200, 404) })),
    'timer current', [404]);
  if (page === 'dashboard') {
    get(`Organizations/${SLUG}`, 'org detail');
    get(`organizations/${SLUG}/work-schedule`, 'work schedule');
    balance(user);
    get(`TimeTracking?from=${week.from}&to=${week.to}&limit=200`, 'entries week');
    get(`TimeTracking?from=${month.first.toISOString()}&to=${new Date(month.last.getTime() + 86400000).toISOString()}&limit=200`, 'entries month');
    inbox(user);
  } else if (page === 'time') {
    get(`organizations/${SLUG}/work-schedule`, 'work schedule');
    get(`organizations/${SLUG}/effective-permissions`, 'effective permissions');
    balance(user);
    get(`TimeTracking?from=${week.from}&to=${week.to}&limit=200`, 'entries week');
  } else {
    // History: MemberCalendar loads the visible month (± the weeks around it), compliance, schedules, types, rules, balance
    const from = new Date(month.first.getTime() - 6 * 86400000).toISOString();
    const to = new Date(month.last.getTime() + 7 * 86400000).toISOString();
    get(`TimeTracking?organizationId=${data.orgId}&from=${from}&to=${to}&limit=200`, 'entries calendar');
    get(`organizations/${SLUG}/members/${user.id}/compliance?from=${day(month.first)}&to=${day(month.last)}`, 'compliance month');
    get(`organizations/${SLUG}/work-schedules`, 'work schedules');
    get(`organizations/${SLUG}/absence-types`, 'absence types');
    get(`organizations/${SLUG}/effective-permissions`, 'effective permissions');
    get(`organizations/${SLUG}/absences?userId=${user.id}&from=${day(month.first)}&to=${day(month.last)}&limit=200`, 'absences month');
    balance(user);
  }
}

export function monthEnd(data) {
  const accountant = data.admins[exec.scenario.iterationInTest % Math.min(2, data.admins.length)];
  const m = monthRange(-1);
  const exp = (name) => auth(accountant, name, 'export', { timeout: '120s' });
  ok(http.get(`${API}/organizations/${SLUG}/accounting/summary?year=${m.year}&month=${m.month}`, exp('accounting summary')), 'summary');
  ok(http.post(`${API}/ExportImport/payroll`, JSON.stringify({ orgSlug: SLUG, from: day(m.first), to: day(m.last) }), exp('payroll csv')), 'payroll');
  ok(http.get(`${API}/organizations/${SLUG}/accounting/vacation?year=${m.year}`, exp('accounting vacation')), 'vacation');
  for (const u of data.users.slice(0, 10))
    ok(http.get(`${API}/organizations/${SLUG}/members/${u.id}/reports/monthly?year=${m.year}&month=${m.month}`, exp('monthly pdf')), 'monthly pdf');
}
