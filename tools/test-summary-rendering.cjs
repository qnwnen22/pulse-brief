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
  const document = {
    querySelector(selector) {
      if (!elements.has(selector)) elements.set(selector, {
        innerHTML: "", value: "", addEventListener() {},
      });
      return elements.get(selector);
    },
    querySelectorAll: () => [],
    addEventListener() {},
  };
  const context = vm.createContext({ document, console, URL, Intl });
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
    activeWeeklyCategory = testCategory;
    renderWeeklySummary();
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
    assert.equal((page.html("#weeklyCategoryTabs").match(/data-weekly-category=/g) || []).length, 9);
    assert.ok(!page.html("#weeklyStats").includes("NaN"));
  }
}

const dailyOnly = render([], daily, null);
assert.ok(dailyOnly.html("#categorySummary").includes(dailyData.categories.find((item) => item.category === "정치/정책").summary));
assert.ok(dailyOnly.html("#weeklySummary").includes("선택한 카테고리의 주간 이슈가 없습니다."));

const weeklyOnly = render([], null, weekly);
assert.ok(weeklyOnly.html("#weeklySummary").includes(weeklyData.categories.find((item) => item.category === "정치/정책").summary));
assert.ok(weeklyOnly.html("#categorySummary").includes("선택한 카테고리의 요약 정보가 없습니다."));

const empty = render([], null, null);
assert.ok(empty.html("#categorySummary").includes("요약할 이슈 데이터가 없습니다."));
assert.equal(empty.html("#weeklyCategoryTabs"), "");

// A later weekly response must not hide a daily-only category.
const union = render([], { categories: [{ category: "Daily only", summary: "Daily text" }] }, weekly, "Daily only");
assert.ok(union.html("#categorySummary").includes("Daily text"));
assert.ok(union.html("#weeklyCategoryTabs").includes("Daily only"));

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
console.log("PASS: saved daily/weekly summaries across 9 categories, missing feeds, independent responses, empty state, archived links and local fallback");
