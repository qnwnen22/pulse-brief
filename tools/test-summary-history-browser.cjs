const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { chromium } = require(path.resolve(path.dirname(process.execPath), "../node_modules/playwright"));

const target = process.argv[2] || "http://127.0.0.1:4187";
const viewports = process.argv.includes("--smoke")
  ? [[1440, 1000], [390, 844]]
  : [[1440, 1000], [820, 1180], [390, 844], [375, 812], [320, 740]];
const output = path.resolve(__dirname, "../publish/summary-history-qa");
fs.mkdirSync(output, { recursive: true });

async function waitForSummary(page, type) {
  await page.waitForFunction(type => {
    const history = type === "daily" ? dailySummaryHistory : weeklySummaryHistory;
    return history.status === "ready";
  }, type);
}

async function selectedDates(page) {
  return page.evaluate(() => ({ daily: dailySummaryHistory.selectedDate, weekly: weeklySummaryHistory.selectedDate }));
}

async function showPeriod(page, period) {
  await page.locator(`[data-summary-period="${period}"]`).click();
  assert.equal(await page.locator(`[data-summary-period="${period}"]`).getAttribute("aria-selected"), "true");
  assert.ok(await page.locator(`[data-summary-panel="${period}"]`).isVisible());
  assert.ok(!(await page.locator(`[data-summary-panel="${period === "daily" ? "weekly" : "daily"}"]`).isVisible()));
}

(async () => {
  const browser = await chromium.launch({ channel: "msedge", headless: true });
  try {
    const page = await browser.newPage();
    const errors = [];
    const requests = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("request", request => {
      if (/\/api\/(daily|weekly)-summary/.test(request.url())) requests.push(new URL(request.url()));
    });
    await page.goto(target);
    await page.locator("#appLoading").waitFor({ state: "hidden" });
    await waitForSummary(page, "daily");
    await waitForSummary(page, "weekly");
    assert.equal(await page.evaluate(() => activeSummaryPeriod), "daily");
    const beforeTabs = requests.length;
    await page.locator("#dailySummaryTab").focus();
    for (const [key, expected] of [["ArrowRight", "weekly"], ["ArrowLeft", "daily"], ["End", "weekly"], ["Home", "daily"]]) {
      await page.keyboard.press(key);
      assert.equal(await page.evaluate(() => activeSummaryPeriod), expected);
      assert.equal(await page.locator(":focus").getAttribute("data-summary-period"), expected);
    }
    assert.equal(requests.length, beforeTabs, "Tab switching must reuse loaded summaries.");
    await showPeriod(page, "weekly");
    const dates = await page.locator("#weeklySummaryDateSelect option").evaluateAll(options => options.map(option => option.value));
    assert.ok(dates.length >= 2, "At least two saved weeks are required to verify history selection.");
    assert.deepEqual(dates, [...new Set(dates)].sort().reverse());
    assert.ok((await page.locator("#weeklySummaryDateSelect option").first().textContent()).includes("최신"));

    for (const [width, height] of viewports) {
      await page.setViewportSize({ width, height });
      await showPeriod(page, "weekly");
      await page.locator("#weeklySummaryDateSelect").selectOption(dates[0]);
      await waitForSummary(page, "weekly");
      const labelSize = await page.locator("#weeklySummaryDateSelect").evaluate(select => {
        const style = getComputedStyle(select);
        const context = document.createElement("canvas").getContext("2d");
        context.font = style.font;
        return {
          text: context.measureText(select.selectedOptions[0].label).width,
          available: select.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight)
        };
      });
      assert.ok(labelSize.text <= labelSize.available, `Period label is truncated at ${width}px: ${JSON.stringify(labelSize)}`);
      const previousDaily = (await selectedDates(page)).daily;
      const beforeSelection = requests.length;
      await page.locator("#weeklySummaryDateSelect").selectOption(dates[1]);
      await waitForSummary(page, "weekly");
      assert.equal(await page.evaluate(() => parseWeeklySummaryRange(weeklyBrief).end), dates[1]);
      assert.equal((await selectedDates(page)).daily, previousDaily, "Weekly selection changed the daily date.");
      assert.equal(new URL(page.url()).searchParams.get("weeklyEndDate"), dates[1]);
      assert.equal(requests.length - beforeSelection, 1, "Weekly selection must fetch only one stored summary.");
      assert.equal(requests.at(-1).searchParams.get("endDate"), dates[1]);
      assert.ok(!(await page.locator("#weeklySummary .empty-state").count()));

      const dailyOptions = await page.locator("#dailySummaryDateSelect option").evaluateAll(options => options.map(option => option.value));
      await showPeriod(page, "daily");
      await page.locator("#dailySummaryDateSelect").selectOption(dailyOptions.at(-1));
      await waitForSummary(page, "daily");
      assert.equal((await selectedDates(page)).weekly, dates[1], "Daily selection changed the weekly date.");
      await page.locator(".briefing-panel").screenshot({ path: path.join(output, `daily-${width}.png`) });
      const selection = await selectedDates(page);

      await page.locator("#refreshButton").click();
      await page.waitForFunction(() => !document.querySelector("#refreshButton").disabled);
      await waitForSummary(page, "weekly");
      await waitForSummary(page, "daily");
      assert.deepEqual(await selectedDates(page), selection, "Refresh discarded a historical selection.");
      await page.reload();
      await page.locator("#appLoading").waitFor({ state: "hidden" });
      await waitForSummary(page, "weekly");
      await waitForSummary(page, "daily");
      assert.deepEqual(await selectedDates(page), selection, "URL restore discarded a historical selection.");
      assert.equal(await page.evaluate(() => activeSummaryPeriod), "daily", "URL restore discarded the selected tab.");

      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Horizontal overflow at ${width}px.`);
      for (const selector of ["#dailySummaryDateSelect", "#weeklySummaryDateSelect"]) {
        await showPeriod(page, selector === "#dailySummaryDateSelect" ? "daily" : "weekly");
        const box = await page.locator(selector).boundingBox();
        assert.ok(box.x >= 0 && box.x + box.width <= width, `${selector} leaves the viewport at ${width}px.`);
      }
      if (width <= 720) assert.ok((await page.locator(".sidebar").boundingBox()).height <= 80);
      assert.equal(await page.locator('[data-summary-panel]:visible').count(), 1);
      assert.equal(await page.locator('[data-summary-history]:visible').count(), 1);
      assert.equal(await page.locator("#summaryStats .metric").count(), 3);
      assert.ok(await page.locator("#summaryStats .metric strong").evaluateAll(values => values.every(value => value.scrollWidth <= value.clientWidth)), `Summary values overflow at ${width}px.`);
      for (const help of await page.locator("#summaryStats .metric-help").all()) {
        await help.focus();
        const tooltip = await help.locator(".metric-tooltip").boundingBox();
        assert.ok(tooltip.x >= 0 && tooltip.x + tooltip.width <= width, `Tooltip leaves the viewport at ${width}px.`);
      }
      await page.locator("#weeklySummaryTab").focus();
      await page.locator(".briefing-panel").screenshot({ path: path.join(output, `weekly-${width}.png`) });
    }

    await page.route("**/api/weekly-summary?*", route => route.fulfill({ status: 503, contentType: "application/json", body: "{}" }));
    await page.locator("#weeklySummaryDateSelect").selectOption(dates[0]);
    await page.waitForFunction(() => weeklySummaryHistory.status === "error");
    assert.ok((await page.locator("#weeklySummary").textContent()).includes("불러오지 못했습니다"));
    assert.ok(await page.locator("#weeklySummaryDateSelect").isEnabled());
    await page.unroute("**/api/weekly-summary?*");

    await page.route("**/api/weekly-summary?*", route => route.fulfill({ status: 404, contentType: "application/json", body: "{}" }));
    await page.locator("#weeklySummaryDateSelect").selectOption(dates[1]);
    await page.waitForFunction(() => weeklySummaryHistory.status === "missing");
    assert.ok((await page.locator("#weeklySummary").textContent()).includes("저장된 주간 요약이 없습니다"));
    await page.unroute("**/api/weekly-summary?*");

    await page.route("**/api/weekly-summary/dates", route => route.fulfill({ status: 200, contentType: "application/json", body: "[]" }));
    await page.route("**/api/weekly-summary", route => route.fulfill({ status: 404, contentType: "application/json", body: "{}" }));
    await page.reload();
    await page.waitForFunction(() => weeklySummaryHistory.status === "missing");
    assert.ok(await page.locator("#weeklySummaryDateSelect").isDisabled());
    assert.ok((await page.locator("#weeklySummaryDateSelect").textContent()).includes("저장된 요약 없음"));
    const linkedPage = await browser.newPage();
    const linkedUrl = new URL(target);
    linkedUrl.searchParams.set("weeklyEndDate", dates[1]);
    await linkedPage.goto(linkedUrl.href);
    await waitForSummary(linkedPage, "weekly");
    assert.equal(await linkedPage.evaluate(() => activeSummaryPeriod), "weekly", "Historical weekly URLs must open the weekly tab.");
    assert.equal(await linkedPage.locator("#weeklySummaryDateSelect").inputValue(), dates[1]);
    await linkedPage.setViewportSize({ width: 320, height: 740 });
    await linkedPage.evaluate(() => {
      const category = weeklyBrief.categories[0];
      category.issueCount = 350000;
      category.articleCount = 350000;
      selectedSummaryCategories.weekly = category.category;
      renderSummaries();
    });
    assert.ok(await linkedPage.locator("#summaryStats .metric strong").evaluateAll(values => values.every(value => value.scrollWidth <= value.clientWidth)), "Large summary counts overflow on mobile.");
    await linkedPage.close();
    assert.deepEqual(errors, []);
    console.log(`PASS: summary tabs, keyboard and request reuse, independent history, URL restore, three matching metrics, ${viewports.map(([width]) => width).join("/")}px, error/missing/empty states; no writes or AI calls`);
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
