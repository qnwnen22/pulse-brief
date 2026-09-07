const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const vm = require("node:vm");
const core = require("./summary-core.cjs");
const launcher = require("./run.cjs");
const date = "2026-09-06";
const news = [
  { Id: "id-1", Title: "시험 지역 도서관 개관", Source: "시험신문", Author: "기자", PublishedAt: "2026-09-06T01:00:00Z", Url: "https://example.com/1", Summary: "도서관이 문을 열었다.", Content: "확인용 본문" },
  { Id: "id-2", Title: "시험 지역 도서관 운영 시작", Source: "시험방송", Author: "기자", PublishedAt: "2026-09-06T02:00:00Z", Url: "https://example.com/2", Summary: "도서관 운영 소식.", Content: "확인용 본문" }
];
function fixture(t) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "pulsebrief-launcher-test-"));
  t.after(() => {
    assert.ok(path.resolve(directory).startsWith(path.resolve(os.tmpdir()) + path.sep));
    fs.rmSync(directory, { recursive: true, force: true });
  });
  const calls = { check: 0, export: 0, login: 0, codex: 0 };
  const dependencies = {
    checkRemote: async () => { calls.check++; return false; },
    exportArticles: async () => { calls.export++; return { articles: news, manifest: { complete: true } }; },
    checkLogin: () => { calls.login++; },
    askCodex: async (config, directory, label) => {
      calls.codex++;
      return label.startsWith("기사 분류")
        ? { topics: [{ title: "시험 도서관 개관", category: "지역", summary: "자료에 따르면 도서관이 개관했다.", articleKeys: ["a1", "a2"], keywords: ["도서관"] }] }
        : { summary: "시험 지역의 도서관 개관 소식이다.", issues: [{ title: "시험 도서관 개관", summary: "도서관이 운영을 시작했다.", topicKeys: ["t1"], keywords: ["도서관"], score: 50, featured: true }] };
    }
  };
  return { options: { date, directory, canonicalFile: path.join(directory, "canonical.json"), config: {}, dependencies, log: () => {} }, calls };
}
test("Korean yesterday crosses the UTC date boundary", () => {
  assert.equal(core.yesterday(new Date("2026-09-06T15:00:00Z")), date);
  assert.equal(core.yesterday(new Date("2026-09-06T14:59:59Z")), "2026-09-05");
  assert.throws(() => core.validateDate("2026-02-30"));
  assert.throws(() => core.validateDate("2026-09-07", new Date("2026-09-07T12:00:00Z")));
});
test("a published summary prevents export, authentication and all Codex calls", async t => {
  const { options, calls } = fixture(t);
  options.dependencies.checkRemote = async () => { calls.check++; return true; };
  assert.equal((await launcher.run(options)).status, "existing-server");
  assert.deepEqual(calls, { check: 1, export: 0, login: 0, codex: 0 });
  assert.equal(fs.existsSync(path.join(options.directory, "draft.json")), false);
});
test("an unknown remote status fails closed without generating", async t => {
  const { options, calls } = fixture(t);
  options.dependencies.checkRemote = async () => { throw new Error("network unavailable"); };
  await assert.rejects(launcher.run(options), /network unavailable/);
  assert.equal(calls.codex, 0);
  assert.equal(calls.export, 0);
});
test("a completed draft makes a second invocation a no-op", async t => {
  const { options, calls } = fixture(t);
  const first = await launcher.run(options);
  assert.equal(first.status, "created");
  const before = fs.readFileSync(first.path, "utf8"), previousCalls = { ...calls };
  assert.equal((await launcher.run(options)).status, "existing-local");
  assert.deepEqual(calls, previousCalls);
  assert.equal(fs.readFileSync(first.path, "utf8"), before);
  const draft = JSON.parse(before);
  assert.equal(draft.ArticleCount, 2);
  assert.equal(draft.TopIssues[0].ArticleCount, draft.TopIssues[0].ArticleIds.length);
  assert.equal(draft.Categories[0].ArticleCount, 2);
});
test("a canonical summary prevents all network and model access", async t => {
  const { options, calls } = fixture(t);
  fs.copyFileSync(path.join(launcher.root, "manual-summaries/2026-09-06.json"), options.canonicalFile);
  assert.equal((await launcher.run(options)).status, "existing-local");
  assert.deepEqual(calls, { check: 0, export: 0, login: 0, codex: 0 });
});
test("an invalid existing local summary is never overwritten", async t => {
  const { options, calls } = fixture(t);
  fs.writeFileSync(options.canonicalFile, "broken-json");
  await assert.rejects(launcher.run(options));
  assert.equal(fs.readFileSync(options.canonicalFile, "utf8"), "broken-json");
  assert.equal(calls.codex, 0);
});
test("a summary appearing during export cancels generation", async t => {
  const { options, calls } = fixture(t);
  options.dependencies.checkRemote = async () => ++calls.check >= 2;
  assert.equal((await launcher.run(options)).status, "existing-server");
  assert.equal(calls.codex, 0);
});
test("a summary appearing before final save prevents a new draft", async t => {
  const { options, calls } = fixture(t);
  options.dependencies.checkRemote = async () => ++calls.check >= 3;
  assert.equal((await launcher.run(options)).status, "existing-server");
  assert.equal(fs.existsSync(path.join(options.directory, "draft.json")), false);
});
test("export failures and empty days never call Codex", async t => {
  const { options, calls } = fixture(t);
  options.dependencies.exportArticles = async () => { throw new Error("incomplete export"); };
  await assert.rejects(launcher.run(options), /incomplete/);
  assert.equal(calls.codex, 0);
  options.dependencies.exportArticles = async () => ({ articles: [] });
  assert.equal((await launcher.run(options)).status, "no-articles");
  assert.equal(calls.codex, 0);
});
test("data outside the Korean day is rejected and deduplication keeps separate publishers", () => {
  assert.throws(() => core.prepareArticles([{ ...news[0], PublishedAt: "2026-09-05T14:59:59Z" }], date));
  const prepared = core.prepareArticles([...news, { ...news[0], Id: "repeat", PublishedAt: "2026-09-06T03:00:00Z" }], date);
  assert.equal(prepared.length, 2);
  assert.equal(prepared[0].Id, "repeat");
});
test("batch limits split input rather than silently dropping it", () => {
  const input = Array.from({ length: 501 }, (_, index) => ({ ...news[0], Key: "a" + index }));
  const batches = core.makeBatches(input, 200);
  assert.deepEqual(batches.map(batch => batch.length), [200, 200, 101]);
  assert.equal(new Set(batches.flat().map(article => article.key)).size, 501);
});
test("missing, fabricated or repeated article references fail validation", () => {
  const topic = { title: "제목", category: "사회", summary: "요약", articleKeys: ["a1"], keywords: [] };
  const batch = [{ key: "a1" }, { key: "a2" }];
  assert.throws(() => core.validateMap({ topics: [topic] }, batch), /누락/);
  assert.throws(() => core.validateMap({ topics: [{ ...topic, articleKeys: ["a1", "made-up"] }] }, batch), /알 수 없거나/);
  assert.throws(() => core.validateMap({ topics: [{ ...topic, articleKeys: ["a1", "a1", "a2"] }] }, batch), /중복/);
});
test("HTML output escapes news text and rejects unsafe link schemes", () => {
  const draft = { Date: date, ArticleCount: 1, IssueCount: 1, SourceCount: 1, Categories: [{ Category: "사회", ArticleCount: 1, IssueCount: 1, Summary: "<script>alert(1)</script>" }], TopIssues: [{ Category: "사회", Title: "<b>제목</b>", Summary: "본문", ArticleCount: 1, ArticleIds: ["id-1"] }] };
  const html = core.reviewHtml(draft, [{ ...news[0], Url: "javascript:alert(1)" }]);
  assert.ok(!html.includes("<script>"));
  assert.ok(!html.includes("javascript:"));
  assert.ok(html.includes("&lt;script&gt;"));
});
test("model invocations disable shell, plugins, hooks and web search", () => {
  const args = launcher.codexArgs("schema.json", "response.json").join(" ");
  for (const flag of ["--sandbox read-only", "--ignore-user-config", "--ignore-rules", "--disable shell_tool", "--disable plugins", "--disable hooks", 'web_search="disabled"']) assert.ok(args.includes(flag));
  assert.ok(!args.includes("dangerously"));
});
function mongoFixture({ exists = false, count = 2, maxArticles = 10, summaryIndex = true, dateIndex = true } = {}) {
  const calls = [], records = [];
  function cursor(items) {
    let offset = 0;
    const value = { hint: name => { calls.push(["hint", name]); return value; }, limit: number => { calls.push(["limit", number]); return value; }, maxTimeMS: number => { calls.push(["maxTimeMS", number]); return value; }, sort: keys => { calls.push(["sort", keys]); return value; }, batchSize: number => { calls.push(["batchSize", number]); return value; }, toArray: () => items, hasNext: () => offset < items.length, next: () => items[offset++], close: () => calls.push(["close"]) };
    return value;
  }
  const context = { request: { date, mode: "export", maxArticles }, print: line => records.push(JSON.parse(line)), db: {
    summaries: { getIndexes: () => summaryIndex ? [{ name: "Date_1", key: { Date: 1 } }] : [], find: (filter, projection) => { calls.push(["summaries", filter, projection]); return cursor(exists ? [{ Date: date }] : []); } },
    articles: { getIndexes: () => dateIndex ? [{ name: "PublishedAt_DateTime_-1", key: { "PublishedAt.DateTime": -1 } }] : [], find: (filter, projection) => {
      calls.push(["articles", filter, projection]);
      return cursor(Array.from({ length: count }, (_, index) => ({ _id: index + "", PublishedAt: { DateTime: news[0].PublishedAt }, Title: "fixture" })));
    } }
  } };
  return { calls, records, execute: () => vm.runInNewContext(fs.readFileSync(path.join(__dirname, "read-news.mongosh.js"), "utf8"), context) };
}
test("Mongo duplicate check never reads articles when the date already exists", () => {
  const fixture = mongoFixture({ exists: true }); fixture.execute();
  assert.equal(fixture.records[0].type, "existing");
  assert.ok(!fixture.calls.some(call => call[0] === "articles"));
});
test("Mongo export uses indexed dates, narrow projection, batch size and time limits", () => {
  const fixture = mongoFixture(); fixture.execute();
  const [, filter, projection] = fixture.calls.find(call => call[0] === "articles");
  assert.equal(filter["PublishedAt.DateTime"].$gte.toISOString(), "2026-09-05T15:00:00.000Z");
  assert.equal(filter["PublishedAt.DateTime"].$lt.toISOString(), "2026-09-06T15:00:00.000Z");
  assert.ok(!("Embedding" in projection));
  assert.ok(fixture.calls.some(call => call[0] === "hint" && call[1] === "PublishedAt_DateTime_-1"));
  assert.ok(fixture.calls.some(call => call[0] === "batchSize" && call[1] === 200));
  assert.ok(fixture.calls.some(call => call[0] === "maxTimeMS" && call[1] === 15000));
  assert.equal(fixture.records.at(-1).complete, true);
});
test("missing Mongo indexes and article ceilings fail closed", () => {
  assert.throws(() => mongoFixture({ summaryIndex: false }).execute(), /refusing/);
  assert.throws(() => mongoFixture({ dateIndex: false }).execute(), /refusing/);
  const fixture = mongoFixture({ count: 3, maxArticles: 2 });
  assert.throws(() => fixture.execute(), /ceiling exceeded/);
  assert.ok(!fixture.records.some(record => record.type === "done"));
});
test("completed Codex cache is reused without starting another process", async t => {
  const { options } = fixture(t), prompt = "test", schema = core.mapSchema;
  const fingerprint = core.hash(JSON.stringify({ policy: core.policyVersion, prompt, schema }));
  const saved = path.join(options.directory, "cache", fingerprint + ".json");
  core.writeJson(saved, { ok: true });
  const response = await launcher.askCodex({ CodexPath: "must-not-run" }, options.directory, "cached", prompt, schema, value => value, () => {});
  assert.deepEqual(response, { ok: true });
});
test("an interrupted model request is not blindly retried", async t => {
  const { options } = fixture(t), prompt = "interrupted", schema = core.mapSchema;
  const fingerprint = core.hash(JSON.stringify({ policy: core.policyVersion, prompt, schema }));
  core.writeJson(path.join(options.directory, "cache", fingerprint + ".started.json"), { started: true });
  await assert.rejects(launcher.askCodex({ CodexPath: "must-not-run" }, options.directory, "interrupted", prompt, schema, value => value, () => {}), /자동 재시도하지 않습니다/);
});
