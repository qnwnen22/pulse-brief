const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { pathToFileURL } = require("node:url");
const { chromium } = require(path.resolve(path.dirname(process.execPath), "../node_modules/playwright"));

const root = path.resolve(__dirname, "..");
const output = path.join(root, "publish/navigation-qa");
const target = process.argv[2] || pathToFileURL(path.join(root, "wwwroot/index.html")).href;
fs.mkdirSync(output, { recursive: true });

(async () => {
  const browser = await chromium.launch({ channel: "msedge", headless: true });
  try {
    const page = await browser.newPage();
    const errors = [];
    let apiRequests = 0;
    page.on("pageerror", (error) => errors.push(error.message));
    page.on("request", (request) => {
      if (new URL(request.url()).pathname.startsWith("/api/")) apiRequests++;
    });
    await page.goto(target);
    await page.locator("#appLoading").waitFor({ state: "hidden" });
    await page.waitForLoadState("networkidle");
    assert.equal(await page.locator(".nav-item").count(), 4);
    assert.ok(await page.locator(".brand-logo").evaluate((img) => img.complete && img.naturalWidth > 0));
    for (const [width, height] of [[1440, 1000], [820, 1180], [390, 844], [390, 1200], [375, 812], [320, 740]]) {
      await page.setViewportSize({ width, height });
      const before = apiRequests;
      let headerHeight;
      for (const view of ["statistics", "feed", "notice", "briefing", "statistics"]) {
        await page.locator(`.nav-item[data-view="${view}"]`).click();
        assert.equal(await page.locator(".view-panel.active").count(), 1);
        assert.ok(await page.locator(`.view-panel[data-panel="${view}"]`).isVisible());
        assert.equal(await page.locator(".nav-item[aria-current=page]").getAttribute("data-view"), view);
        assert.equal(await page.locator("#newsMetricGrid").isVisible(), view === "feed");
        assert.equal(await page.locator("#refreshButton").isVisible(), view !== "statistics");
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${view} overflow at ${width}`);
        if (width <= 1050) {
          const measured = await page.locator(".sidebar").evaluate((el) => {
            const style = getComputedStyle(el);
            const contentHeight = Math.max(...[...el.children].map((child) => child.getBoundingClientRect().height));
            return {
              height: el.getBoundingClientRect().height,
              contentHeight: contentHeight + parseFloat(style.paddingTop) + parseFloat(style.paddingBottom),
            };
          });
          if (width <= 720) {
            assert.ok(measured.height <= measured.contentHeight + 1,
              `${view} header stretched at ${width}x${height}: ${measured.height}px for ${measured.contentHeight}px content`);
            assert.ok(measured.height <= 80, `mobile header too tall: ${measured.height}px`);
          }
          headerHeight ??= measured.height;
          assert.ok(Math.abs(measured.height - headerHeight) <= 1, `header height changes between views at ${width}x${height}`);
        }
      }
      assert.equal(apiRequests, before, "menu switches must not issue API requests");
      assert.equal(await page.locator("#menuTitle").innerText(), "\uD1B5\uACC4");
      for (const button of await page.locator(".nav-item").all()) {
        assert.ok(await button.evaluate((el) => {
          const box = el.getBoundingClientRect();
          return box.left >= 0 && box.right <= innerWidth && el.scrollWidth <= el.clientWidth;
        }), `menu text clipped at ${width}`);
      }
      await page.screenshot({ path: path.join(output, `statistics-${width}x${height}.png`), fullPage: true });
    }
    await page.locator('.footer-link[data-view="notice"]').click();
    assert.ok(await page.locator('[data-panel="notice"]').isVisible());
    await page.locator('.nav-item[data-view="statistics"]').focus();
    await page.keyboard.press("Enter");
    assert.ok(await page.locator('[data-panel="statistics"]').isVisible());
    assert.deepEqual(errors, []);
    console.log("PASS: statistics and existing menus at 1440/820/390/375/320px including tall screens, stable compact header, no overflow or extra requests, logo, footer and keyboard navigation");
  } finally {
    await browser.close();
  }
})().catch((error) => { console.error(error); process.exitCode = 1; });
