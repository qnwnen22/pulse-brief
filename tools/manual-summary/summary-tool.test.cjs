const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const vm = require("node:vm");
const core = require("./summary-core.cjs");
const launcher = require("./run.cjs");
const publication = require("./publication.cjs");
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
  const calls = { check: 0, export: 0, login: 0, codex: 0, publish: 0, verify: 0 };
  let serverSummary;
  const dependencies = {
    checkRemote: async () => { calls.check++; return Boolean(serverSummary); },
    exportArticles: async () => { calls.export++; return { articles: news, manifest: { complete: true } }; },
    checkLogin: () => { calls.login++; },
    askCodex: async (config, directory, label) => {
      calls.codex++;
      return label.startsWith("기사 분류")
        ? { topics: [{ title: "시험 도서관 개관", category: "지역", summary: "자료에 따르면 도서관이 개관했다.", articleKeys: ["a1", "a2"], keywords: ["도서관"] }] }
        : { summary: "시험 지역의 도서관 개관 소식이다.", issues: [{ title: "시험 도서관 개관", summary: "도서관이 운영을 시작했다.", topicKeys: ["t1"], keywords: ["도서관"], score: 50, featured: true }] };
    },
    publishSummary: async (config, summary) => { calls.publish++; serverSummary = summary; return { type: "published", date, matches: true }; },
    verifyWebsite: async () => { calls.verify++; return { status: "verified", url: "https://example.com/api/daily-summary" }; }
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
  assert.deepEqual(calls, { check: 1, export: 0, login: 0, codex: 0, publish: 0, verify: 0 });
  assert.equal(fs.existsSync(path.join(options.directory, "draft.json")), false);
});
test("an unknown remote status fails closed without generating", async t => {
  const { options, calls } = fixture(t);
  options.dependencies.checkRemote = async () => { throw new Error("network unavailable"); };
  await assert.rejects(launcher.run(options), /network unavailable/);
  assert.equal(calls.codex, 0);
  assert.equal(calls.export, 0);
});
test("generation publishes immediately without a draft or review and does not repeat", async t => {
  const { options, calls } = fixture(t);
  const first = await launcher.run(options);
  assert.equal(first.status, "published");
  const receipt = core.readJson(path.join(options.directory, "publication.json"));
  assert.equal(receipt.complete, true);
  assert.equal(receipt.dbConfirmed, true);
  assert.equal(receipt.summary.ArticleCount, 2);
  assert.equal(calls.publish, 1);
  assert.equal(calls.verify, 1);
  for (const file of ["draft.json", "review.html", "manifest.json"]) assert.equal(fs.existsSync(path.join(options.directory, file)), false);
  const previousCalls = { ...calls };
  assert.equal((await launcher.run(options)).status, "existing-server");
  assert.deepEqual(calls, { ...previousCalls, check: previousCalls.check + 1 });
});
test("a saved summary is published without article queries or model access", async t => {
  const { options, calls } = fixture(t);
  core.writeJson(options.canonicalFile, sampleSummary());
  const before = fs.readFileSync(options.canonicalFile, "utf8");
  assert.equal((await launcher.run(options)).status, "published");
  assert.deepEqual(calls, { check: 1, export: 0, login: 0, codex: 0, publish: 1, verify: 1 });
  assert.equal(fs.readFileSync(options.canonicalFile, "utf8"), before);
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
test("another publisher winning the race is never overwritten", async t => {
  const { options, calls } = fixture(t);
  options.dependencies.publishSummary = async () => { calls.publish++; return { type: "existing", date, matches: false }; };
  assert.equal((await launcher.run(options)).status, "existing-server");
  assert.equal(calls.verify, 0);
  assert.equal(fs.existsSync(path.join(options.directory, "draft.json")), false);
});
test("publication failures retain recovery state and rerun only publication", async t => {
  const { options, calls } = fixture(t);
  const realPublish = options.dependencies.publishSummary;
  options.dependencies.publishSummary = async () => { calls.publish++; throw new Error("SSH failed"); };
  await assert.rejects(launcher.run(options), /SSH failed/);
  const before = { ...calls };
  assert.equal(core.readJson(path.join(options.directory, "publication.json")).complete, false);
  options.dependencies.publishSummary = realPublish;
  assert.equal((await launcher.run(options)).status, "published");
  assert.equal(calls.codex, before.codex);
  assert.equal(calls.export, before.export);
});
test("a lost write acknowledgement is recovered without another model call", async t => {
  const { options, calls } = fixture(t);
  const realPublish = options.dependencies.publishSummary;
  options.dependencies.publishSummary = async (...args) => { await realPublish(...args); throw new Error("ack lost"); };
  await assert.rejects(launcher.run(options), /ack lost/);
  const previousCodex = calls.codex;
  options.dependencies.publishSummary = async () => { calls.publish++; return { type: "existing", date, matches: true }; };
  assert.equal((await launcher.run(options)).status, "published");
  assert.equal(calls.codex, previousCodex);
  assert.equal(calls.verify, 1);
});
test("a failed website check is not reported as success and resumes from stored summary", async t => {
  const { options, calls } = fixture(t);
  options.dependencies.verifyWebsite = async () => { throw new Error("website unavailable"); };
  await assert.rejects(launcher.run(options), /website unavailable/);
  const receipt = core.readJson(path.join(options.directory, "publication.json"));
  assert.equal(receipt.complete, false);
  assert.equal(receipt.dbConfirmed, true);
  options.dependencies.publishSummary = async () => ({ type: "existing", date, matches: true });
  options.dependencies.verifyWebsite = async () => ({ status: "verified" });
  const before = calls.codex;
  assert.equal((await launcher.run(options)).status, "published");
  assert.equal(calls.codex, before);
});
test("check-only mode never generates or publishes a saved draft", async t => {
  const { options, calls } = fixture(t);
  core.writeJson(path.join(options.directory, "draft.json"), sampleSummary());
  assert.deepEqual(await launcher.run({ ...options, checkOnly: true }), { status: "checked", date, localExists: true, serverExists: false });
  assert.deepEqual(calls, { check: 1, export: 0, login: 0, codex: 0, publish: 0, verify: 0 });
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
    summaries: { getIndexes: () => summaryIndex ? [{ name: "Date_1", key: { Date: 1 }, unique: true }] : [], find: (filter, projection) => { calls.push(["summaries", filter, projection]); return cursor(exists ? [{ Date: date }] : []); } },
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

function sampleSummary() {
  return { Date: date, GeneratedAt: "2026-09-07T01:00:00.000Z", Provider: "manual", Model: "Codex CLI", Headline: "시험 요약", Summary: "시험 지역 도서관 개관 소식", IssueCount: 1, ArticleCount: 2, SourceCount: 2,
    Categories: [{ Category: "지역", IssueCount: 1, ArticleCount: 2, Summary: "시험 요약" }],
    TopIssues: [{ Title: "시험 도서관 개관", Category: "지역", Summary: "도서관이 개관했다.", ArticleCount: 2, ArticleIds: ["id-1", "id-2"], Score: 50, Sources: ["시험신문", "시험방송"], Keywords: ["도서관"] }] };
}
test("publication validation rejects bad counts, references and dates", () => {
  const summary = sampleSummary();
  assert.deepEqual(publication.normalizeSummary(summary, date), summary);
  assert.throws(() => publication.normalizeSummary({ ...summary, Date: "2026-09-05" }, date), /날짜/);
  assert.throws(() => publication.normalizeSummary({ ...summary, ArticleCount: 3 }, date), /합계/);
  assert.throws(() => publication.normalizeSummary({ ...summary, TopIssues: [{ ...summary.TopIssues[0], ArticleIds: ["id-1", "id-1"] }] }, date), /중복/);
  assert.throws(() => publication.normalizeSummary({ ...summary, Provider: "openai" }, date), /생성 방식/);
  assert.throws(() => publication.normalizeSummary({ ...summary, GeneratedAt: "invalid" }, date), /시각/);
});
test("a legacy draft is reused for publication without regeneration or a browser", async t => {
  const { options, calls } = fixture(t);
  core.writeJson(path.join(options.directory, "draft.json"), sampleSummary());
  assert.equal((await launcher.run(options)).status, "published");
  assert.equal(calls.codex, 0);
  assert.equal(fs.existsSync(path.join(options.directory, "review.html")), false);
});
test("a tampered publication receipt is never published", async t => {
  const { options, calls } = fixture(t);
  core.writeJson(path.join(options.directory, "publication.json"), { date, complete: false, sha256: "wrong", summary: sampleSummary() });
  await assert.rejects(launcher.run(options), /복구 기록 검증 실패/);
  assert.equal(calls.publish, 0);
  assert.equal(calls.codex, 0);
});
function publishFixture({ existing = false, unique = true, race = false, acknowledged = true, corrupt = false } = {}) {
  let stored = existing ? { ...sampleSummary(), Headline: "기존 요약 유지" } : null;
  const calls = [], records = [];
  const collection = {
    getIndexes: () => [{ name: "Date_1", key: { Date: 1 }, unique }],
    find: (filter, projection) => {
      calls.push(["find", filter, projection]);
      const cursor = { hint: name => { calls.push(["hint", name]); return cursor; }, limit: number => { assert.equal(number, 1); return cursor; }, maxTimeMS: number => { assert.equal(number, 3000); return cursor; }, toArray: () => stored ? [stored] : [] };
      return cursor;
    },
    insertOne: (document, options) => {
      calls.push(["insertOne", document, options]);
      if (race) { stored = { ...document, Headline: "다른 작업이 먼저 배포" }; const error = new Error("duplicate key"); error.code = 11000; throw error; }
      stored = corrupt ? { ...document, ArticleCount: 9 } : document;
      return { acknowledged };
    }
  };
  const context = { request: { date, summary: sampleSummary() }, db: { summaries: collection }, Long: { fromString: value => ({ $numberLong: value }) }, print: line => records.push(JSON.parse(line)) };
  return { calls, records, stored: () => stored, execute: () => vm.runInNewContext(fs.readFileSync(path.join(__dirname, "publish-summary.mongosh.js"), "utf8"), context) };
}
test("Mongo publication inserts one document, converts timestamp and verifies it", () => {
  const fixture = publishFixture(); fixture.execute();
  assert.equal(fixture.calls.filter(call => call[0] === "insertOne").length, 1);
  assert.equal(fixture.stored().GeneratedAt.DateTime.toISOString(), sampleSummary().GeneratedAt);
  assert.equal(fixture.stored().GeneratedAt.Ticks.$numberLong, (BigInt(Date.parse(sampleSummary().GeneratedAt)) * 10000n + 621355968000000000n).toString());
  const options = fixture.calls.find(call => call[0] === "insertOne")[2];
  assert.equal(options.writeConcern.j, true);
  assert.equal(options.writeConcern.wtimeout, 5000);
  assert.deepEqual(fixture.records, [{ type: "published", date, matches: true }]);
});
test("Mongo publication preserves existing dates and concurrent winners", () => {
  const existing = publishFixture({ existing: true }); existing.execute();
  assert.equal(existing.calls.filter(call => call[0] === "insertOne").length, 0);
  assert.equal(existing.stored().Headline, "기존 요약 유지");
  const race = publishFixture({ race: true }); race.execute();
  assert.equal(race.stored().Headline, "다른 작업이 먼저 배포");
  assert.deepEqual(race.records, [{ type: "existing", date, matches: false }]);
});
test("Mongo publication fails on a nonunique index, unacknowledged write or bad read-back", () => {
  const missing = publishFixture({ unique: false });
  assert.throws(missing.execute, /Unique Date index missing/);
  assert.equal(missing.calls.length, 0);
  assert.throws(publishFixture({ acknowledged: false }).execute, /not acknowledged/);
  assert.throws(publishFixture({ corrupt: true }).execute, /mismatch/);
});
function camel(value) {
  if (Array.isArray(value)) return value.map(camel);
  if (value && typeof value === "object") return Object.fromEntries(Object.entries(value).map(([key, item]) => [key[0].toLowerCase() + key.slice(1), camel(item)]));
  return value;
}
test("website verification checks the actual body and does not call generation endpoints", async () => {
  const summary = sampleSummary(), now = new Date("2026-09-07T01:00:00Z");
  const fetchImpl = async (url, options) => {
    assert.equal(url.href, "https://example.com/api/daily-summary");
    assert.equal(url.search, "");
    assert.equal(options.redirect, "error");
    return { ok: true, json: async () => camel(summary) };
  };
  assert.equal((await publication.verifyWebsite({ SiteUrl: "https://example.com" }, date, summary, { fetchImpl, now })).status, "verified");
  const bad = async () => ({ ok: true, json: async () => ({ ...camel(summary), headline: "stale content" }) });
  await assert.rejects(publication.verifyWebsite({}, date, summary, { fetchImpl: bad, now }), /배포한 요약과 다릅니다/);
  const failure = async () => ({ ok: false, status: 503 });
  await assert.rejects(publication.verifyWebsite({}, date, summary, { fetchImpl: failure, now }), /HTTP 503/);
});
test("historical publication reports DB-only verification and never bypasses API authentication", async () => {
  const fetchImpl = async () => { throw new Error("must not fetch historical data"); };
  const result = await publication.verifyWebsite({}, "2026-09-05", null, { fetchImpl, now: new Date("2026-09-07T01:00:00Z") });
  assert.equal(result.status, "db-only");
  assert.throws(() => publication.siteUrl({ SiteUrl: "http://example.com" }));
  assert.throws(() => publication.siteUrl({ SiteUrl: "https://user:password@example.com" }));
});
