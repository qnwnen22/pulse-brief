const test = require("node:test");
const assert = require("node:assert/strict");
const scheduled = require("./scheduled.cjs");

const date = "2026-10-05";
const observedAt = "2026-10-05T15:19:00Z";
const checkNow = new Date("2026-10-05T15:00:00Z");
const usage = (remainingPercent = 20, at = new Date().toISOString(), source = "codex_get_usage_limits", windows = { weekly: { remainingPercent } }) => ({ source, observedAt: at, windows });

test("scheduled usage gate checks only the Codex weekly limit and stops below 20 percent", () => {
  assert.throws(() => scheduled.requireUsage(usage(19.99), new Date()), /20% 미만/);
  assert.throws(() => scheduled.requireUsage(usage(20, new Date().toISOString(), "codex_get_usage_limits", {}), new Date()), /주간 한도 잔여량을 확인할 수 없습니다/);
  assert.throws(() => scheduled.requireUsage(null));
  assert.equal(scheduled.requireUsage(usage(), new Date()), true);
});

test("scheduled runner queries required limits first and selects the preceding KST day", async () => {
  const events = [];
  const dependencies = Object.fromEntries(["checkRemote", "exportArticles", "askCodex", "publishSummary", "verifyWebsite"]
    .map(name => [name, async () => { events.push(name); }]));
    dependencies.waitForCollectionGrace = async (_date, notBefore) => { events.push("grace"); return notBefore; };
  dependencies.checkRemote = async (_config, target) => {
    events.push(`check:${target}`);
    return true;
  };
  const result = await scheduled.runScheduledDaily({
    now: new Date("2026-10-05T15:00:00Z"),
    config: { SiteUrl: "https://news.pulse-brief.co.kr", MaxArticles: 10000 },
    storageRoot: "C:\\scheduled-pulse",
    readUsage: async () => {
      events.push("usage");
      return usage(20, observedAt);
    },
    dependencies,
    log: () => {}
  });
  assert.deepEqual(events, ["grace", "usage", "check:" + date]);
  assert.deepEqual(result, { status: "existing-server", date });
});

test("missing or low usage data prevents any remote read or publication", async () => {
  for (const snapshot of [null, usage(19, observedAt), usage(20, "2026-10-05T14:54:59Z"), usage(20, observedAt, "unknown")]) {
    const events = [];
    const dependencies = Object.fromEntries(["checkRemote", "exportArticles", "askCodex", "publishSummary", "verifyWebsite"]
      .map(name => [name, async () => { events.push(name); }]));
      dependencies.waitForCollectionGrace = async (_date, notBefore) => notBefore;
    await assert.rejects(scheduled.runScheduledDaily({
      now: new Date("2026-10-05T15:00:00Z"),
      config: { SiteUrl: "https://news.pulse-brief.co.kr", MaxArticles: 10000 },
      storageRoot: "C:\\scheduled-pulse",
      readUsage: async () => snapshot,
      dependencies,
      log: () => {}
    }));
    assert.deepEqual(events, []);
  }
});


test("scheduler rejects missing, ambiguous, stale, future, or non-official usage snapshots", () => {
  const bad = [
    { observedAt, windows: { weekly: { remainingPercent: 90 } } },
    { source: "codex_get_usage_limits", windows: { weekly: { remainingPercent: 90 } } },
    usage(90, "2026-10-05T14:59:00"),
    usage(90, "2026-10-05T14:54:59Z"),
    usage(90, "2026-10-05T15:00:01Z"),
    usage(90, observedAt, "unknown"),
    usage(101, observedAt)
  ];
  for (const snapshot of bad) assert.throws(() => scheduled.requireUsage(snapshot, checkNow));
  assert.equal(scheduled.requireUsage(usage(20, "2026-10-05T14:55:00Z"), checkNow), true);
});
