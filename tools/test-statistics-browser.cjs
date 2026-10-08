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
    let statisticsRequests = 0;
    page.on("request", request => { if (request.url().includes("/api/collection-statistics")) statisticsRequests++; });
    page.on("pageerror", error => errors.push(error.message));
    await page.goto(target);
    await page.locator("#appLoading").waitFor({ state: "hidden" });
    await page.locator('.nav-item[data-view="statistics"]').click();
    await page.locator("#collection-trend-title").waitFor();
    const sample = await page.evaluate(() => statisticsCache.get("7").data);
    assert.equal(await page.locator(".statistics-metrics .metric").count(), 4);
    assert.equal(await page.locator(".publisher-table tbody tr").count(), sample.publishers.length);
    assert.equal(await page.locator(".category-table tbody tr").count(), sample.categories.length);
    assert.ok(sample.isReady, "Statistics cache must be complete before release QA.");
    assert.ok(sample.areCategoriesReady, "Category statistics cache must be complete before release QA.");
    for (const [width, height] of [[1440, 1000], [820, 1180], [390, 844], [375, 812], [320, 740]]) {
      await page.setViewportSize({ width, height });
      for (const period of ["7", "30", "all"]) {
        await page.locator(`#statisticsPeriods [data-period="${period}"]`).click();
        await page.waitForFunction(period => document.querySelector("#statisticsContent").getAttribute("aria-busy") === "false" && statisticsCache.has(period), period);
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `page overflows at ${width}, ${period}`);
        const panel = await page.locator('[data-panel="statistics"]').boundingBox();
        assert.ok(panel.x >= 0 && panel.x + panel.width <= width, `statistics panel is clipped at ${width}, ${period}`);
        const data = await page.evaluate(period => statisticsCache.get(period).data, period);
        assert.equal(data.categories.reduce((total, item) => total + item.count, 0), data.total, "Categories must count each article exactly once.");
        assert.equal(await page.locator(".category-table tbody tr").count(), 9);
        assert.equal(await page.locator(".category-change-table tbody tr").count(), period === "all" ? 0 : 9);
        const publisherNames = data.publisherTrends.map(item => item.publisher);
        assert.equal(await page.locator("#statisticsPublisher option").count(), publisherNames.length);
        const beforeSelection = statisticsRequests;
        for (const publisher of [publisherNames[0], publisherNames.at(-1)]) {
          await page.locator("#statisticsPublisher").selectOption(publisher);
          const series = data.publisherTrends.find(item => item.publisher === publisher);
          const rendered = await page.locator("#publisherTrendContent .statistics-details tbody td").allTextContents();
          assert.deepEqual(rendered, series.trend.map(point => `${point.count.toLocaleString("ko-KR")}건`), "Publisher selection rendered the wrong series.");
          assert.equal(await page.locator("#publisherTrendContent .statistics-bar").count(), series.trend.length);
          assert.ok(await page.locator("#publisherTrendContent .statistics-bars").evaluate(el => el.getBoundingClientRect().height > 0));
        }
        assert.equal(statisticsRequests, beforeSelection, "Publisher selection must not trigger server requests.");
        assert.ok(await page.locator("#statisticsPublisher").evaluate(el => el.getBoundingClientRect().right <= innerWidth), "Publisher select leaves the viewport.");
        assert.ok(await page.locator(".statistics-share-table").evaluateAll(tables => tables.every(table => {
          const box = table.getBoundingClientRect();
          return box.left >= 0 && box.right <= innerWidth;
        })), `share table is clipped at ${width}, ${period}`);
        assert.equal(await page.locator('#statisticsPeriods [aria-pressed="true"]').getAttribute("data-period"), period);
        assert.ok(await page.locator(".statistics-metrics .metric strong").evaluateAll(elements => elements.every(el => el.scrollWidth <= el.clientWidth)), "metric value overflows");
        assert.ok(await page.locator(".statistics-toolbar button").evaluateAll(elements => elements.every(el => el.scrollWidth <= el.clientWidth)), "period button text overflows");
        assert.ok(await page.locator(".statistics-bar").count() > 0);
        assert.ok(await page.locator(".statistics-bar").evaluateAll(elements => elements.every(el => el.getBoundingClientRect().height > 0)), "chart is blank");
        if (width <= 720) {
          assert.ok((await page.locator(".sidebar").boundingBox()).height <= 80);
          assert.equal(await page.locator(".statistics-metrics").evaluate(el => getComputedStyle(el).gridTemplateColumns.split(" ").length), 2);
          assert.ok(await page.locator(".category-change-table tbody td").evaluateAll(cells => cells.every(cell => {
            const box = cell.getBoundingClientRect();
            return box.left >= 0 && box.right <= innerWidth;
          })), "Category changes must be visible without horizontal scrolling on mobile.");
        }
        for (const help of await page.locator(".statistics-metrics .metric-help").all()) {
          await help.focus();
          const tooltip = await help.locator(".metric-tooltip").boundingBox();
          const bounds = await help.evaluate(el => ({ label: el.getAttribute("aria-label"), card: el.closest(".metric").getBoundingClientRect().toJSON(), right: getComputedStyle(el.querySelector(".metric-tooltip")).right, left: getComputedStyle(el.querySelector(".metric-tooltip")).left }));
          assert.ok(tooltip.x >= -0.5 && tooltip.x + tooltip.width <= width + 0.5, `metric tooltip leaves viewport at ${width}: ${JSON.stringify({ tooltip, bounds })}`);
        }
        await page.locator("#statisticsRefresh").focus();
        await page.screenshot({ path: path.join(output, `${period}-${width}.png`), fullPage: true });
        if (width === 375 && period === "7") {
          await page.locator('section[aria-labelledby="category-change-title"]').screenshot({ path: path.join(output, "category-change-mobile.png") });
          await page.locator(".publisher-trend-section").screenshot({ path: path.join(output, "publisher-trend-mobile.png") });
        }
      }
    }
    await page.locator(".statistics-details summary").first().click();
    assert.ok(await page.locator(".statistics-details table").first().isVisible());
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
    assert.equal(await page.locator(".category-table").count(), 0);
    assert.equal(await page.locator(".category-change-table").count(), 0);
    await page.unroute("**/api/collection-statistics?*");

    await page.route("**/api/collection-statistics?*", route => route.fulfill({ json: {
      ...sample, period: "all", areCategoriesReady: false, categoryCompletedDays: 6, categories: []
    } }));
    await page.locator("#statisticsRefresh").click();
    await page.locator("#category-share-title").waitFor();
    await page.waitForFunction(() => document.querySelector("#statisticsContent").getAttribute("aria-busy") === "false");
    assert.ok((await page.locator("#statisticsContent").innerText()).includes("카테고리 집계 중"));
    assert.ok(await page.locator(".publisher-table").isVisible(), "Category migration must not hide ready publisher statistics.");
    assert.equal(await page.locator(".category-table").count(), 0);
    assert.equal(await page.locator(".category-change-table").count(), 0);
    await page.unroute("**/api/collection-statistics?*");

    await page.route("**/api/collection-statistics?*", route => route.fulfill({ json: {
      ...sample, period: "all", publisherTrends: [], isCategoryComparisonReady: false
    } }));
    await page.locator("#statisticsRefresh").click();
    await page.waitForFunction(() => document.querySelector("#statisticsContent").getAttribute("aria-busy") === "false");
    assert.equal(await page.locator("#statisticsPublisher").count(), 0);
    assert.equal(await page.locator(".category-change-table").count(), 0);
    assert.ok((await page.locator("#publisherTrendContent").innerText()).includes("데이터가 없습니다"));
    await page.unroute("**/api/collection-statistics?*");

    await page.route("**/api/collection-statistics?*", route => route.fulfill({ json: {
      ...sample, period: "all", total: 0, dailyAverage: 0, publishers: [], categories: sample.categories.map(item => ({ ...item, count: 0, share: 0 }))
    } }));
    await page.locator("#statisticsRefresh").click();
    await page.locator(".category-table").waitFor();
    assert.ok(!(await page.locator("#statisticsContent").innerText()).includes("NaN"));
    assert.ok(await page.locator(".category-table tbody td:last-child").evaluateAll(cells => cells.every(cell => cell.textContent.trim() === "0%")));
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
    console.log("PASS: periods, publisher/category shares and changes, publisher trends without requests, monthly totals, 1440/820/390/375/320px layout, error recovery, legacy/partial/zero caches and stale responses");
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
