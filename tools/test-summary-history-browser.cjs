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
    const dates = await page.locator("#weeklySummaryDateSelect option").evaluateAll(options => options.map(option => option.value));
    assert.ok(dates.length >= 2, "At least two saved weeks are required to verify history selection.");
    assert.deepEqual(dates, [...new Set(dates)].sort().reverse());
    assert.ok((await page.locator("#weeklySummaryDateSelect option").first().textContent()).includes("최신"));

    for (const [width, height] of viewports) {
      await page.setViewportSize({ width, height });
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
      await page.locator("#dailySummaryDateSelect").selectOption(dailyOptions.at(-1));
      await waitForSummary(page, "daily");
      assert.equal((await selectedDates(page)).weekly, dates[1], "Daily selection changed the weekly date.");
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

      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Horizontal overflow at ${width}px.`);
      for (const selector of ["#dailySummaryDateSelect", "#weeklySummaryDateSelect"]) {
        const box = await page.locator(selector).boundingBox();
        assert.ok(box.x >= 0 && box.x + box.width <= width, `${selector} leaves the viewport at ${width}px.`);
      }
      if (width <= 720) assert.ok((await page.locator(".sidebar").boundingBox()).height <= 80);
      await page.locator(".weekly-box").screenshot({ path: path.join(output, `weekly-${width}.png`) });
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
    assert.deepEqual(errors, []);
    console.log(`PASS: historical weekly/daily selection, one-document requests, refresh and URL restore, ${viewports.map(([width]) => width).join("/")}px, error/missing/empty states; no writes or AI calls`);
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
