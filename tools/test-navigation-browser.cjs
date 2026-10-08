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
    const definitions = await page.evaluate(() => getAvailableViewDefinitions());
    assert.equal(await page.locator(".nav-item").count(), definitions.length);
    assert.ok(await page.locator(".brand-logo").evaluate((img) => img.complete && img.naturalWidth > 0));
    for (const [width, height] of [[1440, 1000], [820, 1180], [390, 844], [390, 1200], [375, 812], [320, 740]]) {
      await page.setViewportSize({ width, height });
      const before = apiRequests;
      let headerHeight;
      const statistics = definitions.find((view) => view.id === "statistics");
      for (const view of [statistics, ...definitions, statistics]) {
        await page.locator(`.nav-item[data-view="${view.id}"]`).click();
        assert.equal(await page.locator(".view-panel.active").count(), 1);
        assert.ok(await page.locator(`.view-panel[data-panel="${view.id}"]`).isVisible());
        assert.equal(await page.locator(".nav-item[aria-current=page]").getAttribute("data-view"), view.id);
        assert.equal(await page.locator("#newsMetricGrid").isVisible(), Boolean(view.showNewsMetrics));
        assert.equal(await page.locator("#refreshButton").isVisible(), view.showRefresh !== false);
        assert.equal(await page.locator("#menuTitle").innerText(), view.title);
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${view.id} overflow at ${width}`);
        const panel = await page.locator(`.view-panel[data-panel="${view.id}"]`).boundingBox();
        assert.ok(panel.x >= 0 && panel.x + panel.width <= width, `${view.id} panel is clipped at ${width}`);
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
              `${view.id} header stretched at ${width}x${height}: ${measured.height}px for ${measured.contentHeight}px content`);
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

    await page.setViewportSize({ width: 320, height: 740 });
    const beforeExtension = apiRequests;
    const headerHeight = (await page.locator(".sidebar").boundingBox()).height;
    for (const extraCount of [1, 4]) {
      await page.evaluate((count) => {
        for (let index = 1; index <= count; index++) {
          const id = `qa-view-${index}`;
          if (viewDefinitions.some((view) => view.id === id)) continue;
          viewDefinitions.push({
            id, label: `\uCD94\uAC00${index}`, icon: "+", eyebrow: "QA", title: `QA report ${index}`,
            showNewsMetrics: index === 1, showRefresh: false,
          });
          const panel = document.createElement("section");
          panel.className = "view-panel";
          panel.dataset.panel = id;
          panel.textContent = "Browser-only extension fixture";
          document.querySelector(".content-grid").append(panel);
        }
        renderNavigation();
      }, extraCount);
      assert.equal(await page.locator(".nav-item").count(), definitions.length + extraCount);
      await page.locator(`.nav-item[data-view="qa-view-${extraCount}"]`).click();
      assert.equal(await page.locator("#menuTitle").innerText(), `QA report ${extraCount}`);
      assert.equal(await page.locator("#newsMetricGrid").isVisible(), extraCount === 1);
      assert.equal(await page.locator("#refreshButton").isVisible(), false);
      assert.equal(await page.locator(".view-panel.active").count(), 1);
      assert.ok(await page.locator(`[data-panel="qa-view-${extraCount}"]`).isVisible());
      assert.ok(Math.abs((await page.locator(".sidebar").boundingBox()).height - headerHeight) <= 1);
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
      assert.ok(await page.locator("#serviceNavigation").evaluate((el) => el.scrollWidth > el.clientWidth), "extra menus must scroll inside navigation");
      await page.screenshot({ path: path.join(output, `extra-${extraCount}-320.png`), fullPage: true });
    }
    await page.evaluate((count) => {
      viewDefinitions.splice(count);
      document.querySelectorAll('[data-panel^="qa-view-"]').forEach((panel) => panel.remove());
      renderNavigation();
    }, definitions.length);
    assert.equal(await page.locator(".nav-item").count(), definitions.length);
    assert.equal(await page.locator(".nav-item[aria-current=page]").getAttribute("data-view"), definitions[0].id);
    assert.equal(apiRequests, beforeExtension, "extending and rerendering navigation must not request data");
    assert.deepEqual(errors, []);
    console.log("PASS: configured menus at 1440/820/390/375/320px and tall screens, browser-only 5/8-menu extensions, compact header, contained scrolling, no extra requests, footer and keyboard navigation");
  } finally {
    await browser.close();
  }
})().catch((error) => { console.error(error); process.exitCode = 1; });
