const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const root = path.resolve(__dirname, "..");
const webRoot = path.join(root, "wwwroot");
const version = fs.readFileSync(path.join(root, "VERSION"), "utf8").trim();
let count = 0;

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
