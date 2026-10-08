const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const webRoot = path.join(__dirname, "../wwwroot");
const html = fs.readFileSync(path.join(webRoot, "index.html"), "utf8");
const panels = [...html.matchAll(/class="[^\"]*view-panel[^\"]*"[^>]*data-panel="([^\"]+)"/g)]
  .map((match) => match[1]);
assert.match(html, /id="serviceNavigation"/);
assert.doesNotMatch(html, /class="nav-item/, "menu labels must come from view definitions");
assert.match(html, /id="statisticsContent"/);

function element(dataset = {}) {
  const classes = new Set();
  const attributes = new Map();
  return {
    dataset, attributes, children: [], hidden: false, textContent: "",
    classList: {
      toggle(name, active) { if (active) classes.add(name); else classes.delete(name); },
      contains(name) { return classes.has(name); },
    },
    setAttribute(name, value) { attributes.set(name, value); },
    removeAttribute(name) { attributes.delete(name); },
    append(...children) { this.children.push(...children); },
    replaceChildren(...children) { this.children = children; },
    querySelectorAll() { return this.children.filter((child) => child.dataset?.view); },
  };
}

const viewPanels = panels.map((panel) => element({ panel }));
const footer = element({ view: "notice" });
const elements = new Map();
const document = {
  querySelector(selector) {
    if (!elements.has(selector)) elements.set(selector, element());
    return elements.get(selector);
  },
  querySelectorAll(selector) {
    if (selector === ".view-panel[data-panel]") return viewPanels;
    if (selector === ".footer-link[data-view]") return [footer];
    return [];
  },
  createElement() { return element(); },
  createTextNode(textContent) { return { textContent }; },
};
let requests = 0;
const context = vm.createContext({
  document, Intl, location: { protocol: "https:" },
  fetch() { requests++; throw new Error("navigation must not request data"); },
});
for (const file of ["state.js", "views.js", "navigation.js"]) {
  vm.runInContext(fs.readFileSync(path.join(webRoot, "js", file), "utf8"), context);
}

const definitions = JSON.parse(vm.runInContext("JSON.stringify(viewDefinitions)", context));
assert.deepEqual(definitions.map((view) => view.id), panels);
assert.equal(new Set(definitions.map((view) => view.id)).size, definitions.length);
vm.runInContext("renderNavigation()", context);
const navigation = elements.get("#serviceNavigation");
assert.equal(navigation.children.length, definitions.length);
assert.equal(footer.textContent, definitions.find((view) => view.id === "notice").label);
for (const [index, view] of definitions.entries()) {
  const button = navigation.children[index];
  assert.equal(button.dataset.view, view.id);
  assert.equal(button.children[0].textContent, view.icon);
  assert.equal(button.children[0].attributes.get("aria-hidden"), "true");
  assert.equal(button.children[1].textContent, view.label);
}

function assertView(viewId) {
  vm.runInContext(`showView(${JSON.stringify(viewId)})`, context);
  const available = JSON.parse(vm.runInContext("JSON.stringify(getAvailableViewDefinitions())", context));
  const view = available.find((item) => item.id === viewId) || available[0];
  const navItems = navigation.children;
  assert.equal(navItems.filter((item) => item.classList.contains("active")).length, 1);
  assert.equal(viewPanels.filter((panel) => panel.classList.contains("active")).length, 1);
  for (const item of navItems) {
    assert.equal(item.attributes.get("aria-current"), item.dataset.view === view.id ? "page" : undefined);
  }
  assert.equal(viewPanels.find((panel) => panel.dataset.panel === view.id).classList.contains("active"), true);
  assert.equal(elements.get("#newsMetricGrid").classList.contains("hidden"), !view.showNewsMetrics);
  assert.equal(elements.get("#refreshButton").hidden, view.showRefresh === false);
  assert.equal(elements.get("#menuTitle").textContent, view.title);
  assert.equal(elements.get("#menuEyebrow").textContent, view.eyebrow);
  assert.equal(vm.runInContext("activeViewId", context), view.id);
}

for (const view of [...panels, "statistics", "invalid"]) assertView(view);
assertView("statistics");
vm.runInContext("renderNavigation(); renderNavigation();", context);
assert.equal(navigation.children.length, definitions.length, "rerender must not duplicate menus");
assert.equal(vm.runInContext("activeViewId", context), "statistics", "rerender must preserve the active view");

context.extraDefinition = {
  id: "extra", label: "Extra", icon: "+", eyebrow: "Extra", title: "Extra report",
  showNewsMetrics: true, showRefresh: false,
};
vm.runInContext("viewDefinitions.push(extraDefinition); renderNavigation();", context);
assert.equal(navigation.children.length, definitions.length, "a missing panel must not create a dead menu");
viewPanels.push(element({ panel: "extra" }));
vm.runInContext("renderNavigation()", context);
assert.equal(navigation.children.length, definitions.length + 1);
assertView("extra");
vm.runInContext("viewDefinitions.unshift(viewDefinitions.pop()); renderNavigation();", context);
assertView("invalid");
assert.equal(vm.runInContext("activeViewId", context), "extra", "default view must follow definitions, not a fixed ID");
viewPanels.pop();
vm.runInContext("renderNavigation()", context);
assertView("extra");
assert.equal(navigation.children.length, definitions.length);
assert.equal(requests, 0);
console.log("PASS: centralized menu metadata, titles/controls, rerender, new view configuration, missing panel fallback, default order and no extra requests");
