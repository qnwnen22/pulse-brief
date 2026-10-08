const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { chromium } = require(path.resolve(path.dirname(process.execPath), "../node_modules/playwright"));

const target = process.argv[2] || "http://127.0.0.1:4187";
const output = path.resolve(__dirname, "../publish/statistics-qa");
fs.mkdirSync(output, { recursive: true });

(async () => {
  const browser = await chromium.launch({ channel: "msedge", headless: true });
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    await page.goto(target);
    await page.locator("#appLoading").waitFor({ state: "hidden" });
    await page.locator('.nav-item[data-view="statistics"]').click();
    await page.locator("#collection-trend-title").waitFor();
    const sample = await page.evaluate(() => statisticsCache.get("7").data);
    assert.equal(await page.locator(".statistics-metrics .metric").count(), 4);
    assert.equal(await page.locator(".publisher-table tbody tr").count(), sample.publishers.length);
    assert.ok(sample.isReady, "Statistics cache must be complete before release QA.");
    for (const [width, height] of [[1440, 1000], [820, 1180], [390, 844], [375, 812], [320, 740]]) {
      await page.setViewportSize({ width, height });
      for (const period of ["7", "30", "all"]) {
        await page.locator(`#statisticsPeriods [data-period="${period}"]`).click();
        await page.waitForFunction(period => document.querySelector("#statisticsContent").getAttribute("aria-busy") === "false" && statisticsCache.has(period), period);
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `page overflows at ${width}, ${period}`);
        assert.equal(await page.locator('#statisticsPeriods [aria-pressed="true"]').getAttribute("data-period"), period);
        assert.ok(await page.locator(".statistics-metrics .metric strong").evaluateAll(elements => elements.every(el => el.scrollWidth <= el.clientWidth)), "metric value overflows");
        assert.ok(await page.locator(".statistics-toolbar button").evaluateAll(elements => elements.every(el => el.scrollWidth <= el.clientWidth)), "period button text overflows");
        assert.ok(await page.locator(".statistics-bar").count() > 0);
        assert.ok(await page.locator(".statistics-bar").evaluateAll(elements => elements.every(el => el.getBoundingClientRect().height > 0)), "chart is blank");
        if (width <= 720) {
          assert.ok((await page.locator(".sidebar").boundingBox()).height <= 80);
          assert.equal(await page.locator(".statistics-metrics").evaluate(el => getComputedStyle(el).gridTemplateColumns.split(" ").length), 2);
        }
        await page.screenshot({ path: path.join(output, `${period}-${width}.png`), fullPage: true });
      }
    }
    await page.locator(".statistics-details summary").click();
    assert.ok(await page.locator(".statistics-details table").isVisible());
    const monthlyMatches = await page.evaluate(() => {
      const data = statisticsCache.get("all").data;
      return collectionTrendPoints(data).reduce((sum, point) => sum + (point.count || 0), 0) === data.total;
    });
    assert.ok(monthlyMatches, "All-history monthly chart lost articles.");

    await page.route("**/api/collection-statistics?*", route => route.fulfill({ status: 503, contentType: "application/json", body: "{}" }));
    await page.locator("#statisticsRefresh").click();
    await page.locator("#statisticsContent [role=alert]").waitFor();
    assert.equal(await page.locator("#statisticsContent").getAttribute("aria-busy"), "false");
    await page.unroute("**/api/collection-statistics?*");
    await page.locator("#statisticsRefresh").click();
    await page.locator("#collection-trend-title").waitFor();

    await page.route("**/api/collection-statistics?*", route => {
      const period = new URL(route.request().url()).searchParams.get("period");
      return route.fulfill({ json: { ...sample, period, isReady: false, completedDays: 1, total: null, dailyAverage: null, publishers: [], trend: sample.trend.map((point, index) => ({ ...point, count: index === 0 ? point.count : null })) } });
    });
    await page.locator("#statisticsRefresh").click();
    await page.locator(".statistics-bar.missing").first().waitFor();
    assert.ok((await page.locator("#statisticsContent").innerText()).includes("집계 중"));
    assert.equal(await page.locator(".publisher-table").count(), 0);
    await page.unroute("**/api/collection-statistics?*");

    await page.evaluate(() => statisticsCache.clear());
    let releaseOld;
    await page.route("**/api/collection-statistics?*", async route => {
      const period = new URL(route.request().url()).searchParams.get("period");
      if (period === "30") await new Promise(resolve => { releaseOld = resolve; });
      await route.fulfill({ json: { ...sample, period } });
    });
    await page.locator('#statisticsPeriods [data-period="30"]').click();
    await page.waitForResponse(() => false, { timeout: 100 }).catch(() => {});
    await page.locator('#statisticsPeriods [data-period="all"]').click();
    await page.waitForFunction(() => statisticsCache.get("all")?.data.period === "all" && document.querySelector("#statisticsContent").getAttribute("aria-busy") === "false");
    assert.ok(releaseOld, "Slow response fixture was not captured.");
    releaseOld();
    await page.waitForFunction(() => statisticsCache.has("30"));
    assert.ok((await page.locator("#collection-trend-title").innerText()).startsWith("월별"), "Stale response replaced the selected period.");
    assert.deepEqual(errors, []);
    console.log("PASS: statistics periods, publisher share, monthly totals, 1440/820/390/375/320px layout, chart pixels, error recovery, partial cache and stale-response protection");
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
