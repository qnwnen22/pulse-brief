const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const root = path.resolve(__dirname, "..");
const webRoot = path.join(root, "wwwroot");
const version = fs.readFileSync(path.join(root, "VERSION"), "utf8").trim();
let count = 0;

const publicState = fs.readFileSync(path.join(webRoot, "js/state.js"), "utf8");
const publicApi = fs.readFileSync(path.join(webRoot, "js/api.js"), "utf8");
const publicEvents = fs.readFileSync(path.join(webRoot, "js/events.js"), "utf8");
const publicHtml = fs.readFileSync(path.join(webRoot, "index.html"), "utf8");
assert.match(publicState, /location\.protocol === "file:" \? \[\.\.\.sampleIssues\] : \[\]/, "production must not start with sample news");
assert.doesNotMatch(publicApi, /!serverIssues\.length/, "an empty server response must not preserve sample news");
assert.match(publicHtml, /id="dailySummaryDateSelect"/, "daily summary history selector is missing");
assert.match(publicApi, /\/api\/daily-summary\/dates/, "daily summary history API is not loaded");
assert.match(publicApi, /\/api\/daily-summary\$\{query\}/, "selected daily summary date is not requested");
assert.match(publicEvents, /dailySummaryDateSelect\?\.addEventListener\("change"/, "daily summary selector has no change handler");
const publicStylesheet = publicHtml.match(/<link\b[^>]*\brel="stylesheet"[^>]*\bhref="([^"]+)"/)?.[1];
assert.ok(publicStylesheet, "public stylesheet link is missing");
assert.equal(new URL(publicStylesheet, "http://localhost/").searchParams.get("v"), version, "public stylesheet cache version is stale");
count += 7;

for (const [file, routes, prefix] of [
  ["index.html", ["/"], "/js/"],
  ["admin/index.html", ["/admin", "/admin/", "/admin/index.html"], "/admin/js/"],
]) {
  const html = fs.readFileSync(path.join(webRoot, file), "utf8");
  const scripts = [...html.matchAll(/<script\b([^>]*?)\bsrc="([^"]+)"[^>]*><\/script>/g)];
  assert.equal(scripts.length, 10, `${file}: expected ten feature scripts`);
  assert.ok(scripts[0][2].includes("/state.js"));
  assert.ok(scripts.at(-1)[2].includes("/main.js"));
  for (const route of routes) {
    for (const [, href] of html.matchAll(/<link\b[^>]*\bhref="([^"]+)"/g)) {
      const url = new URL(href, `http://localhost${route}`);
      if (url.origin === "http://localhost") {
        assert.ok(fs.existsSync(path.join(webRoot, url.pathname)), `${route}: missing linked asset ${href}`);
      }
    }
    for (const [, attributes, source] of scripts) {
      assert.match(attributes, /\bdefer\b/, `${file}: execution order requires defer`);
      const url = new URL(source, `http://localhost${route}`);
      assert.ok(url.pathname.startsWith(prefix), `${route}: wrong script directory ${source}`);
      assert.equal(url.searchParams.get("v"), version, `${source}: stale release version`);
      const sourcePath = path.join(webRoot, url.pathname);
      assert.ok(fs.existsSync(sourcePath), `${route}: missing ${sourcePath}`);
      new vm.Script(fs.readFileSync(sourcePath, "utf8"), { filename: sourcePath });
      count++;
    }
  }
}

console.log(`PASS: ${count} static asset paths, script syntax, load order and release versions`);
