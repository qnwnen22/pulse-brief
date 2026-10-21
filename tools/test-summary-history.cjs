const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

function summary(type, date) {
  if (type === "daily") return { date, categories: [], topIssues: [] };
  const end = new Date(`${date}T00:00:00Z`);
  const start = new Date(end.getTime() - 6 * 86400000).toISOString().slice(0, 10);
  return { date: `weekly:${start}:${date}`, categories: [], topIssues: [] };
}

function response(value, status = 200) {
  return { ok: status === 200, status, json: async () => value };
}

function fixture(fetch, query = "") {
  const elements = new Map();
  const location = { protocol: "https:", search: query, href: `https://example.com/${query}` };
  const warnings = [];
  const context = vm.createContext({
    fetch,
    location,
    URL,
    URLSearchParams,
    Intl,
    AbortController,
    console: { warn: message => warnings.push(message) },
    window: {
      location,
      setTimeout,
      clearTimeout,
      history: {
        replaceState(_state, _title, url) {
          location.href = String(url);
          location.search = url.search;
        }
      }
    },
    document: {
      querySelector(selector) {
        if (!elements.has(selector)) elements.set(selector, { innerHTML: "", value: "", disabled: false });
        return elements.get(selector);
      },
      querySelectorAll: () => []
    }
  });
  for (const file of ["state.js", "summaries.js", "api.js"]) {
    vm.runInContext(fs.readFileSync(path.join(__dirname, "../wwwroot/js", file), "utf8"), context);
  }
  vm.runInContext("renderWeeklySummary = () => {};", context);
  return {
    warnings,
    elements,
    evaluate: script => vm.runInContext(script, context),
    history: type => JSON.parse(vm.runInContext(`JSON.stringify(${type}SummaryHistory)`, context)),
    load: (type, date = "") => vm.runInContext(`loadSummaryHistory(${JSON.stringify(type)}, ${JSON.stringify(date)})`, context),
    select: (type, date) => vm.runInContext(`load${type === "daily" ? "Daily" : "Weekly"}Summary(${JSON.stringify(date)})`, context)
  };
}

test("weekly history filters invalid dates, sorts newest first and requests only one saved week", async () => {
  const requests = [];
  const view = fixture(async url => {
    requests.push(url);
    return url.endsWith("/dates")
      ? response(["2026-10-11", "invalid", "2026-10-18", "2026-10-11", "2026-02-30"])
      : response(summary("weekly", "2026-10-18"));
  });
  assert.equal(await view.load("weekly"), true);
  assert.deepEqual(requests, ["/api/weekly-summary/dates", "/api/weekly-summary?endDate=2026-10-18"]);
  assert.deepEqual(view.history("weekly").dates, ["2026-10-18", "2026-10-11"]);
  const select = view.elements.get("#weeklySummaryDateSelect");
  assert.equal(select.value, "2026-10-18");
  assert.equal(select.disabled, false);
  assert.match(select.innerHTML, /2026.10.12 ~ 2026.10.18 \(최신\)/);
});

test("daily and weekly URL selections remain independent and survive address updates", async () => {
  const view = fixture(async url => {
    const weekly = url.includes("weekly");
    const type = weekly ? "weekly" : "daily";
    if (url.endsWith("/dates")) {
      return response(weekly ? ["2026-10-18", "2026-10-11"] : ["2026-10-21", "2026-10-20"]);
    }
    const date = new URL(url, "https://example.com").searchParams.get(weekly ? "endDate" : "date");
    return response(summary(type, date));
  }, "?summaryDate=2026-10-20&weeklyEndDate=2026-10-11");
  await view.load("daily");
  await view.load("weekly");
  assert.equal(view.history("daily").selectedDate, "2026-10-20");
  assert.equal(view.history("weekly").selectedDate, "2026-10-11");
  view.evaluate('updateWeeklySummaryAddress("2026-10-18")');
  assert.equal(view.evaluate('getRequestedDailySummaryDate()'), "2026-10-20");
  assert.equal(view.evaluate('getRequestedWeeklySummaryDate()'), "2026-10-18");
  assert.equal(view.evaluate('dailyBrief.date'), "2026-10-20");
});

test("refresh preserves an existing older selection and falls back when it disappears", async () => {
  let dates = ["2026-10-18", "2026-10-11"];
  const view = fixture(async url => {
    if (url.endsWith("/dates")) return response(dates);
    const date = new URL(url, "https://example.com").searchParams.get("endDate");
    return response(summary("weekly", date));
  });
  await view.load("weekly", "2026-10-11");
  assert.equal(view.history("weekly").selectedDate, "2026-10-11");
  dates = ["2026-10-18"];
  await view.load("weekly", "2026-10-11");
  assert.equal(view.history("weekly").selectedDate, "2026-10-18");
});

test("an empty history and missing summary do not reuse stale weekly data", async () => {
  const view = fixture(async url => url.endsWith("/dates") ? response([]) : response({}, 404));
  await view.load("weekly");
  assert.equal(view.history("weekly").status, "missing");
  assert.equal(view.evaluate("weeklyBrief"), null);
  assert.equal(view.elements.get("#weeklySummaryDateSelect").disabled, true);
  assert.match(view.elements.get("#weeklySummaryDateSelect").innerHTML, /저장된 요약 없음/);
});

test("a history-list failure still permits the default stored summary to load", async () => {
  const view = fixture(async url => {
    if (url.endsWith("/dates")) throw new Error("History unavailable");
    assert.equal(url, "/api/weekly-summary");
    return response(summary("weekly", "2026-10-18"));
  });
  await view.load("weekly");
  assert.equal(view.history("weekly").datesStatus, "error");
  assert.equal(view.history("weekly").status, "ready");
  assert.equal(view.history("weekly").selectedDate, "2026-10-18");
});

test("failed and missing period requests clear stale content and restore the selector", async () => {
  let status = 200;
  const view = fixture(async () => response(summary("weekly", "2026-10-18"), status));
  await view.select("weekly", "2026-10-18");
  status = 503;
  await view.select("weekly", "2026-10-11");
  assert.equal(view.history("weekly").status, "error");
  assert.equal(view.evaluate("weeklyBrief"), null);
  assert.equal(view.elements.get("#weeklySummaryDateSelect").disabled, false);
  status = 404;
  await view.select("weekly", "2026-10-11");
  assert.equal(view.history("weekly").status, "missing");
});

test("a slower period response cannot overwrite the newer selection", async () => {
  const requests = [];
  const view = fixture(() => new Promise(resolve => requests.push(resolve)));
  const old = view.select("weekly", "2026-10-11");
  const latest = view.select("weekly", "2026-10-18");
  requests[1](response(summary("weekly", "2026-10-18")));
  await latest;
  requests[0](response(summary("weekly", "2026-10-11")));
  await old;
  assert.equal(view.history("weekly").selectedDate, "2026-10-18");
  assert.equal(view.evaluate("weeklyBrief.date"), "weekly:2026-10-12:2026-10-18");
  assert.equal(view.history("weekly").status, "ready");
});

test("a stale period error does not clear a newer successful summary", async () => {
  const requests = [];
  const view = fixture(() => new Promise((resolve, reject) => requests.push({ resolve, reject })));
  const old = view.select("daily", "2026-10-20");
  const latest = view.select("daily", "2026-10-21");
  requests[1].resolve(response(summary("daily", "2026-10-21")));
  await latest;
  requests[0].reject(new Error("Old request failed"));
  await old;
  assert.equal(view.evaluate("dailyBrief.date"), "2026-10-21");
  assert.equal(view.warnings.length, 0);
});

test("a response for another period is rejected instead of relabeling it", async () => {
  const view = fixture(async () => response(summary("weekly", "2026-10-18")));
  assert.equal(await view.select("weekly", "2026-10-11"), false);
  assert.equal(view.history("weekly").status, "error");
  assert.equal(view.history("weekly").selectedDate, "2026-10-11");
  assert.equal(view.evaluate("weeklyBrief"), null);
});

test("a late date-list response cannot trigger another summary request after a selection", async () => {
  const requests = [];
  const view = fixture(url => new Promise(resolve => requests.push({ url, resolve })));
  const history = view.load("weekly");
  const selection = view.select("weekly", "2026-10-11");
  requests[1].resolve(response(summary("weekly", "2026-10-11")));
  await selection;
  requests[0].resolve(response(["2026-10-18", "2026-10-11"]));
  await history;
  assert.equal(requests.length, 2);
  assert.equal(view.history("weekly").selectedDate, "2026-10-11");
});
