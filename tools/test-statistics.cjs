const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const source = fs.readFileSync(path.join(__dirname, "../wwwroot/js/statistics.js"), "utf8");

function fixture(fetchImpl = () => { throw new Error("Unexpected request"); }) {
  const attributes = new Map();
  const content = {
    innerHTML: "",
    setAttribute: (name, value) => attributes.set(name, value)
  };
  const rendered = [];
  const warnings = [];
  const context = vm.createContext({
    document: { querySelector: () => content },
    location: { protocol: "https:" },
    fetchWithTimeout: fetchImpl,
    console: { warn: message => warnings.push(message) },
    escapeHtml: value => String(value).replaceAll("<", "&lt;").replaceAll(">", "&gt;"),
    content,
    rendered
  });
  vm.runInContext(source, context);
  vm.runInContext("renderCollectionStatistics = data => { content.innerHTML = data.period; rendered.push(data.period); };", context);
  return {
    content,
    attributes,
    rendered,
    warnings,
    evaluate: script => vm.runInContext(script, context),
    load: (period, force = false) => vm.runInContext(
      `statisticsPeriod = ${JSON.stringify(period)}; loadCollectionStatistics(${force});`,
      context
    )
  };
}

function statisticsResponse(period) {
  return { ok: true, json: async () => ({ period, trend: [], publishers: [] }) };
}

test("monthly trends preserve zeros and propagate a missing day only within that month", () => {
  const view = fixture();
  const result = view.evaluate(`JSON.stringify(statisticsTrendPoints([
    { date: "2026-09-01", count: 0 },
    { date: "2026-09-02", count: 10 },
    { date: "2026-09-03", count: null },
    { date: "2026-09-04", count: 5 },
    { date: "2026-10-01", count: 0 }
  ], "all"))`);
  assert.deepEqual(JSON.parse(result), [
    { date: "2026-09", count: null },
    { date: "2026-10", count: 0 }
  ]);
});

test("calendar levels and comparisons distinguish zero, missing and no comparison", () => {
  const view = fixture();
  for (const [count, expected] of [[null, "missing"], [0, "level-0"], [1, "level-1"], [100, "level-4"]]) {
    assert.equal(view.evaluate(`statisticsCalendarLevel(${count}, 100)`), expected);
  }
  assert.match(view.evaluate('statisticsComparisonLabel({ period: "all" })'), /비교 대상 없음/);
  assert.match(view.evaluate('statisticsComparisonLabel({ period: "7", previousTotal: 0 })'), /계산 제외/);
  assert.match(view.evaluate('statisticsComparisonLabel({ period: "7", previousTotal: null })'), /집계 부족/);
  assert.equal(view.evaluate('statisticsChange(-0, "%", 1)'), "0%");
});

test("share tables retain pending, empty and escaped content states", () => {
  const view = fixture();
  assert.match(view.evaluate('renderStatisticsShareTable({ isReady: false, pendingText: "<대기>" })'), /&lt;대기&gt;/);
  assert.match(view.evaluate('renderStatisticsShareTable({ isReady: true, items: [] })'), /기사가 없습니다/);
  const html = view.evaluate(`renderStatisticsShareTable({
    id: "publisher", label: "언론사", labelKey: "publisher", isReady: true,
    items: [{ publisher: "<시험>", count: 0, share: 0 }]
  })`);
  assert.match(html, /&lt;시험&gt;/);
  assert.match(html, /width:0%/);
});

test("period cache is reused while forced refresh makes a new bounded request", async () => {
  let requests = 0;
  const view = fixture(async (url, options, timeout) => {
    requests++;
    assert.equal(url, "/api/collection-statistics?period=7");
    assert.equal(options.cache, "no-store");
    assert.equal(timeout, 7000);
    return statisticsResponse("7");
  });
  await view.load("7");
  await view.load("7");
  assert.equal(requests, 1);
  await view.load("7", true);
  assert.equal(requests, 2);
  assert.equal(view.attributes.get("aria-busy"), "false");
});

test("out-of-order responses cache their data without replacing the selected period", async () => {
  const requests = [];
  const view = fixture(() => new Promise(resolve => requests.push(resolve)));
  const oldRequest = view.load("7");
  const selectedRequest = view.load("30");
  requests[0](statisticsResponse("7"));
  await oldRequest;
  assert.equal(view.attributes.get("aria-busy"), "true");
  assert.deepEqual(view.rendered, []);
  requests[1](statisticsResponse("30"));
  await selectedRequest;
  assert.deepEqual(view.rendered, ["30"]);
  await view.load("7");
  assert.equal(requests.length, 2);
  assert.deepEqual(view.rendered, ["30", "7"]);
});

test("stale request errors stay invisible and invalid current responses are not cached", async () => {
  const requests = [];
  const view = fixture(() => new Promise((resolve, reject) => requests.push({ resolve, reject })));
  const oldRequest = view.load("7");
  const selectedRequest = view.load("30");
  requests[1].resolve(statisticsResponse("30"));
  await selectedRequest;
  requests[0].reject(new Error("Old request failed"));
  await oldRequest;
  assert.equal(view.content.innerHTML, "30");
  assert.equal(view.warnings.length, 0);

  const invalidRequest = view.load("7");
  requests[2].resolve(statisticsResponse("all"));
  await invalidRequest;
  assert.match(view.content.innerHTML, /불러오지 못했습니다/);
  assert.equal(view.evaluate('statisticsCache.has("7")'), false);
  assert.equal(view.attributes.get("aria-busy"), "false");
});
