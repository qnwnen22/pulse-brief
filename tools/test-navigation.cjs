const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const webRoot = path.join(__dirname, "../wwwroot");
const html = fs.readFileSync(path.join(webRoot, "index.html"), "utf8");
const views = [...html.matchAll(/class="nav-item[^\"]*"[^>]*data-view="([^\"]+)"/g)]
  .map((match) => match[1]);
const panels = [...html.matchAll(/class="[^\"]*view-panel[^\"]*"[^>]*data-panel="([^\"]+)"/g)]
  .map((match) => match[1]);
assert.deepEqual(views, ["briefing", "feed", "statistics", "notice"]);
assert.deepEqual(panels, views);
assert.match(html, /id="statisticsContent"/);

function element(dataset = {}) {
  const classes = new Set();
  const attributes = new Map();
  return {
    dataset, attributes, hidden: false, textContent: "",
    classList: {
      toggle(name, active) { if (active) classes.add(name); else classes.delete(name); },
      contains(name) { return classes.has(name); },
    },
    setAttribute(name, value) { attributes.set(name, value); },
    removeAttribute(name) { attributes.delete(name); },
  };
}

const navItems = views.map((view) => element({ view }));
const viewPanels = panels.map((panel) => element({ panel }));
const elements = new Map();
const document = {
  querySelector(selector) {
    if (!elements.has(selector)) elements.set(selector, element());
    return elements.get(selector);
  },
  querySelectorAll(selector) {
    if (selector === ".nav-item[data-view]") return navItems;
    if (selector === ".view-panel[data-panel]") return viewPanels;
    return [];
  },
};
let requests = 0;
const context = vm.createContext({
  document, Intl, location: { protocol: "https:" },
  fetch() { requests++; throw new Error("navigation must not request data"); },
});
for (const file of ["state.js", "navigation.js"]) {
  vm.runInContext(fs.readFileSync(path.join(webRoot, "js", file), "utf8"), context);
}

for (const view of ["statistics", "feed", "notice", "briefing", "statistics", "invalid"]) {
  vm.runInContext(`showView(${JSON.stringify(view)})`, context);
  const target = views.includes(view) ? view : "briefing";
  assert.equal(navItems.filter((item) => item.classList.contains("active")).length, 1);
  assert.equal(viewPanels.filter((panel) => panel.classList.contains("active")).length, 1);
  for (const item of navItems) {
    assert.equal(item.attributes.get("aria-current"), item.dataset.view === target ? "page" : undefined);
  }
  assert.equal(viewPanels.find((panel) => panel.dataset.panel === target).classList.contains("active"), true);
  assert.equal(elements.get("#newsMetricGrid").classList.contains("hidden"), target !== "feed");
  assert.equal(elements.get("#refreshButton").hidden, target === "statistics");
  if (target === "statistics") {
    assert.equal(elements.get("#menuTitle").textContent, "\uD1B5\uACC4");
    assert.equal(elements.get("#menuEyebrow").textContent, "Statistics");
  }
}
assert.equal(requests, 0);
console.log("PASS: four menu panels, statistics title, active navigation, metrics/refresh visibility, fallback and no extra data requests");
