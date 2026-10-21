const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const root = path.resolve(__dirname, "..");
const scriptPaths = process.argv[2] ? [process.argv[2]] : [...fs.readFileSync(path.join(root, "wwwroot/index.html"), "utf8")
  .matchAll(/<script\b[^>]*\bsrc="([^"]+)"/g)]
  .map((match) => path.join(root, "wwwroot", match[1].split("?")[0]));
const source = scriptPaths.map((file) => fs.readFileSync(file, "utf8")).join("\n");
const daily = JSON.parse(fs.readFileSync(path.join(root, "manual-summaries/2026-09-06.json"), "utf8"));
const weekly = JSON.parse(fs.readFileSync(path.join(root, "manual-summaries/weekly-2026-08-31_2026-09-06.json"), "utf8"));

function camelCaseKeys(value) {
  if (Array.isArray(value)) return value.map(camelCaseKeys);
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [
      key[0].toLowerCase() + key.slice(1), camelCaseKeys(item),
    ]));
  }
  return value;
}

function render(feed, dailySummary, weeklySummary, category = "") {
  const elements = new Map();
  const location = { protocol: "https:", search: "", href: "https://example.com/" };
  const window = {
    location,
    history: { replaceState() {} },
    setTimeout,
    clearTimeout,
    scrollTo() {},
  };
  const document = {
    querySelector(selector) {
      if (!elements.has(selector)) elements.set(selector, {
        innerHTML: "", value: "", addEventListener() {}, querySelectorAll: () => [],
      });
      return elements.get(selector);
    },
    querySelectorAll: () => [],
    addEventListener() {},
  };
  const context = vm.createContext({ document, console, URL, URLSearchParams, Intl, location, window });
  assert.match(source, /initializeApp\(\);\s*$/);
  vm.runInContext(source.replace(/initializeApp\(\);\s*$/, ""), context);
  Object.assign(context, {
    testFeed: feed,
    testDaily: camelCaseKeys(dailySummary),
    testWeekly: camelCaseKeys(weeklySummary),
    testCategory: category,
  });
  vm.runInContext(`
    issues = testFeed;
    dailyBrief = testDaily;
    weeklyBrief = testWeekly;
    selectedSummaryCategories.daily = testCategory;
    selectedSummaryCategories.weekly = testCategory;
    activeSummaryPeriod = "daily";
    renderSummaries();
    activeSummaryPeriod = "weekly";
    renderSummaries();
  `, context);
  return {
    context,
    html: (selector) => elements.get(selector)?.innerHTML || "",
    text: (selector) => (elements.get(selector)?.innerHTML || "")
      .replace(/<[^>]*>/g, "").replace(/&#39;/g, "'")
      .replace(/&quot;/g, '"').replace(/&gt;/g, ">").replace(/&lt;/g, "<").replace(/&amp;/g, "&"),
  };
}

const dailyData = camelCaseKeys(daily);
const weeklyData = camelCaseKeys(weekly);
const currentFeed = [{
  title: "Current news", category: "사회", latestPublishedAt: "2026-09-07T09:00:00Z",
  impact: 40, source: "Current publisher", articleCount: 1,
}];

for (const feed of [[], currentFeed]) {
  for (const category of dailyData.categories.map((item) => item.category)) {
    const page = render(feed, daily, weekly, category);
    assert.ok(page.text("#categorySummary").includes(dailyData.categories.find((item) => item.category === category).summary), `daily ${category}`);
    assert.ok(page.text("#weeklySummary").includes(weeklyData.categories.find((item) => item.category === category).summary), `weekly ${category}`);
    assert.equal((page.html("#summaryCategoryTabs").match(/data-summary-category=/g) || []).length, 9);
    assert.ok(!page.html("#summaryStats").includes("NaN"));
  }
}

const dailyOnly = render([], daily, null);
assert.ok(dailyOnly.html("#categorySummary").includes(dailyData.categories.find((item) => item.category === "정치/정책").summary));
assert.ok(dailyOnly.html("#weeklySummary").includes("주간 이슈 데이터가 없습니다."));

const weeklyOnly = render([], null, weekly);
assert.ok(weeklyOnly.html("#weeklySummary").includes(weeklyData.categories.find((item) => item.category === "정치/정책").summary));
assert.ok(weeklyOnly.html("#categorySummary").includes("요약할 이슈 데이터가 없습니다."));

const empty = render([], null, null);
assert.ok(empty.html("#categorySummary").includes("요약할 이슈 데이터가 없습니다."));
assert.equal(empty.html("#summaryCategoryTabs"), "");

const loading = render([], daily, weekly, "정치/정책");
vm.runInContext('dailySummaryHistory.status = "loading"; renderCategorySummary();', loading.context);
assert.ok(loading.html("#categorySummary").includes("불러오는 중입니다."));
vm.runInContext('dailySummaryHistory.status = "missing"; dailySummaryHistory.selectedDate = "2026-09-05"; renderCategorySummary();', loading.context);
assert.ok(loading.html("#categorySummary").includes("2026년 9월 5일에 저장된 일간 요약이 없습니다."));

const weeklyStates = render([], daily, weekly, "정치/정책");
vm.runInContext('weeklySummaryHistory.status = "loading"; renderSummaries();', weeklyStates.context);
assert.ok(weeklyStates.html("#weeklySummary").includes("주간 요약을 불러오는 중"));
assert.equal(weeklyStates.html("#summaryStats"), "");
assert.ok(weeklyStates.html("#categorySummary").includes(dailyData.categories.find(item => item.category === "정치/정책").summary));
vm.runInContext('weeklySummaryHistory.status = "missing"; weeklySummaryHistory.selectedDate = "2026-09-06"; renderSummaries();', weeklyStates.context);
assert.ok(weeklyStates.html("#weeklySummary").includes("2026.08.31 ~ 2026.09.06에 저장된 주간 요약이 없습니다."));
vm.runInContext('weeklySummaryHistory.status = "error"; renderSummaries();', weeklyStates.context);
assert.ok(weeklyStates.html("#weeklySummary").includes("주간 이슈 요약을 불러오지 못했습니다."));

// Categories belong to their selected period, even when the other response arrives later.
const union = render([], { categories: [{ category: "Daily only", summary: "Daily text" }] }, weekly, "Daily only");
assert.ok(union.html("#categorySummary").includes("Daily text"));
assert.ok(!union.html("#summaryCategoryTabs").includes("Daily only"));
vm.runInContext('activeSummaryPeriod = "daily"; renderSummaries();', union.context);
assert.ok(union.html("#summaryCategoryTabs").includes("Daily only"));

const modes = render([], daily, weekly, "정치/정책");
vm.runInContext('selectSummaryPeriod("daily");', modes.context);
assert.ok(modes.html("#summaryStats").includes("일간 이슈"));
assert.ok(!modes.html("#summaryStats").includes("주간 이슈"));
assert.equal((modes.html("#summaryStats").match(/class="metric"/g) || []).length, 3);
assert.ok(modes.html("#summaryStats").includes(dailyData.categories.find(item => item.category === "정치/정책").articleCount.toLocaleString("ko-KR")));
vm.runInContext('selectedSummaryCategories.daily = "사회"; renderSummaries(); selectSummaryPeriod("weekly");', modes.context);
assert.equal(vm.runInContext('selectedSummaryCategories.weekly', modes.context), "정치/정책");
assert.ok(modes.html("#summaryStats").includes("주간 이슈"));
vm.runInContext('selectSummaryPeriod("daily");', modes.context);
assert.equal(vm.runInContext('selectedSummaryCategories.daily', modes.context), "사회");
assert.ok(modes.text("#categorySummary").includes("사회 일간 이슈 요약"));

const zeroCounts = render([], {
  categories: [{ category: "사회", summary: "기사 없음", issueCount: 0, articleCount: 0 }],
  topIssues: []
}, weekly, "사회");
vm.runInContext('selectSummaryPeriod("daily");', zeroCounts.context);
assert.equal((zeroCounts.html("#summaryStats").match(/>0<\/strong>/g) || []).length, 3);

const related = render([], daily, weekly);
related.context.testIssue = {
  title: "Archived issue", category: "사회", articleCount: 1,
  relatedLinks: [{ title: "Original article", source: "Publisher", url: "https://example.com/news/1" }],
};
const linkHtml = vm.runInContext('renderTrackedIssueItem(testIssue, [], "weekly-source-picker", "Related articles")', related.context);
assert.ok(linkHtml.includes('href="https://example.com/news/1"'));
assert.ok(!linkHtml.includes("연결된 관련 기사를 찾지 못했습니다."));

const local = render(currentFeed, null, null, "사회");
assert.ok(local.html("#weeklySummary").includes("Current news"));
const labels = render([], daily, weekly);
assert.equal(
  vm.runInContext('getSummaryProviderLabel({ provider: "manual", model: "\\uB2F7\\uCE20 \\uD074\\uB77C\\uC6B0\\uB4DC" })', labels.context),
  "AI \uC694\uC57D \u00B7 \uB2F7\uCE20 \uD074\uB77C\uC6B0\uB4DC",
);
assert.equal(
  vm.runInContext('getSummaryProviderLabel({ provider: "manual", model: "Codex CLI" })', labels.context),
  "\uC218\uB3D9 \uC694\uC57D \u00B7 Codex CLI",
);
assert.equal(
  vm.runInContext('getSummaryProviderLabel({ provider: "manual", model: "Other model" })', labels.context),
  "\uC218\uB3D9 \uC694\uC57D \u00B7 Other model",
);
assert.equal(
  vm.runInContext('getSummaryProviderLabel({ provider: "openai", model: "GPT test" })', labels.context),
  "AI \uC694\uC57D \u00B7 GPT test",
);
console.log("PASS: saved daily/weekly summaries across 9 categories, missing feeds, independent responses, empty state, archived links, local fallback and summary provider labels");
