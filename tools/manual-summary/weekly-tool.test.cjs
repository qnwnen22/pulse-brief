const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const vm = require("node:vm");
const core = require("./summary-core.cjs");
const periods = require("./periods.cjs");
const weekly = require("./weekly.cjs");
const publication = require("./publication.cjs");
const launcher = require("./run.cjs");
const { runWorkflow } = require("./workflow.cjs");
const week = periods.weekEnding("2026-09-06");
function daily(date, index = 0) {
  return { Date: date, GeneratedAt: "2026-09-07T00:00:00Z", Provider: "manual", Model: "Codex", Headline: "시험", Summary: "시험 요약",
    ArticleCount: 1, IssueCount: 1, SourceCount: 1,
    Categories: [{ Category: "지역", ArticleCount: 1, IssueCount: 1, Summary: "도서관 소식" }],
    TopIssues: [{ Title: "시립 도서관 개관", Category: "지역", Summary: `자료 ${index}`, ArticleCount: 1, ArticleIds: [`article-${date}`], Score: 70, Sources: [`신문${index}`], Keywords: ["도서관"] }] };
}
const inputs = () => week.dates.map(daily);
function directory(t) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "pulsebrief-weekly-test-"));
  t.after(() => {
    assert.ok(path.resolve(dir).startsWith(path.resolve(os.tmpdir()) + path.sep));
    fs.rmSync(dir, { recursive: true, force: true });
  });
  return dir;
}
function remote(values) {
  return async (config, request, record) => {
    assert.equal(request.mode, "read-summary");
    const summary = values.find(value => value.Date === request.date);
    record({ type: summary ? "existing" : "missing", date: request.date, summary });
  };
}
test("weekly period changes at Monday 00:00 KST, not Sunday or UTC midnight", () => {
  assert.equal(periods.latestWeek(new Date("2026-09-06T14:59:59Z")).end, "2026-08-30");
  assert.equal(periods.latestWeek(new Date("2026-09-06T15:00:00Z")).key, week.key);
  assert.equal(periods.latestWeek(new Date("2026-09-11T10:00:00Z")).key, week.key);
  assert.equal(periods.latestWeek(new Date("2026-01-04T15:00:00Z")).start, "2025-12-29");
  assert.throws(() => periods.weekEnding("2026-09-05"), /일요일/);
  assert.throws(() => periods.validateSummaryKey("weekly:2026-09-01:2026-09-06"));
  assert.throws(() => periods.validateSummaryKey("weekly:2026-08-31:2026-09-06", new Date("2026-09-06T14:59:59Z")));
  assert.equal(periods.fileStem(week.key), "weekly-2026-08-31_2026-09-06");
});
test("local weekly rollup accounts for all seven days and uses the latest dated prose", () => {
  const result = weekly.buildWeekly(week, inputs());
  assert.equal(result.ArticleCount, 7);
  assert.equal(result.IssueCount, 7);
  assert.equal(result.SourceCount, 7);
  assert.equal(result.TopIssues.length, 1);
  assert.equal(result.TopIssues[0].ArticleIds.length, 7);
  assert.equal(result.TopIssues[0].Score, 90);
  assert.equal(result.TopIssues[0].Summary, "2026-09-06 일간 요약 기준: 자료 6");
  assert.equal(result.Model, "Local daily rollup");
  assert.match(result.Date, /^weekly:/);
});
test("missing, duplicate or out-of-period daily data blocks weekly publication", () => {
  assert.throws(() => weekly.buildWeekly(week, inputs().slice(1)), /7일/);
  const values = inputs(); values[0] = values[1];
  assert.throws(() => weekly.buildWeekly(week, values), /없거나 중복/);
  values[0] = daily("2026-08-30");
  assert.throws(() => weekly.buildWeekly(week, values), /없거나 중복/);
});
test("all nine categories survive and each has at most three featured issues", () => {
  const values = inputs().map((value, index) => ({ ...value, ArticleCount: 9, IssueCount: 9,
    Categories: core.categories.map(Category => ({ ...value.Categories[0], Category })),
    TopIssues: core.categories.map(Category => ({ ...value.TopIssues[0], Category, Title: `${Category} 날짜별 사건 ${index}`, ArticleIds: [Category + index] })) }));
  const result = weekly.buildWeekly(week, values);
  assert.equal(result.Categories.length, 9);
  assert.equal(result.TopIssues.length, 27);
  assert.equal(result.ArticleCount, 63);
  for (const Category of core.categories) assert.equal(result.TopIssues.filter(issue => issue.Category === Category).length, 3);
});
test("unrelated events sharing a keyword are not merged; evidence is deduplicated", () => {
  const values = inputs();
  values[1].TopIssues[0].Title = "도서관 예산 삭감";
  values[2].TopIssues[0].ArticleIds = values[0].TopIssues[0].ArticleIds;
  const result = weekly.buildWeekly(week, values);
  assert.equal(result.TopIssues.length, 2);
  assert.equal(result.TopIssues.reduce((sum, issue) => sum + issue.ArticleCount, 0), 6);
});
test("corrupt evidence or overlapping references across unrelated issues fail closed", () => {
  const values = inputs(); values[0].TopIssues[0].ArticleCount = 10;
  assert.throws(() => weekly.buildWeekly(week, values), /근거 ID/);
  const overlap = inputs(); overlap[1].TopIssues[0].Title = "다른 사건"; overlap[1].TopIssues[0].ArticleIds = overlap[0].TopIssues[0].ArticleIds;
  assert.throws(() => weekly.buildWeekly(week, overlap), /중복/);
});
test("existing seven summaries require no Codex, article export or daily generation", async t => {
  let reads = 0;
  const read = remote(inputs());
  const opts = { week, directory: directory(t), config: {}, log: () => {},
    ensureDaily: async () => { throw new Error("must not generate"); },
    readRemote: async (...args) => { reads++; return read(...args); } };
  assert.equal((await weekly.gatherDaily(opts)).length, 7);
  assert.equal(reads, 7);
  assert.equal((await weekly.gatherDaily(opts)).length, 7);
  assert.equal(reads, 7);
});
test("only missing days are generated sequentially and reread from the server", async t => {
  const values = inputs().slice(2), generated = [];
  const result = await weekly.gatherDaily({ week, directory: directory(t), config: {}, log: () => {}, readRemote: remote(values),
    ensureDaily: async date => { generated.push(date); values.push(daily(date)); return { status: "published" }; } });
  assert.deepEqual(generated, week.dates.slice(0, 2));
  assert.equal(result.length, 7);
});
test("an empty day or inaccessible server cannot become an incomplete week", async t => {
  const opts = { week, directory: directory(t), config: {}, log: () => {}, readRemote: remote([]), ensureDaily: async () => ({ status: "no-articles" }) };
  await assert.rejects(weekly.gatherDaily(opts), /주간 배포를 중단/);
  assert.equal(fs.existsSync(path.join(opts.directory, "daily-input.json")), false);
  await assert.rejects(weekly.gatherDaily({ ...opts, readRemote: async () => { throw new Error("offline"); } }), /offline/);
});
test("tampered weekly input is not silently regenerated", async t => {
  const dir = directory(t);
  core.writeJson(path.join(dir, "daily-input.json"), { key: week.key, summaries: inputs(), sha256: "bad" });
  await assert.rejects(weekly.gatherDaily({ week, directory: dir, readRemote: async () => { throw new Error("must not read"); } }), /복구 기록/);
});
function publicationOptions(t) {
  let stored, builds = 0, verification = 0;
  const summary = weekly.buildWeekly(week, inputs());
  const opts = { date: week.key, directory: directory(t), config: {}, log: () => {},
    generateSummary: async () => { builds++; return summary; },
    dependencies: {
      checkRemote: async () => Boolean(stored),
      publishSummary: async (config, value) => { stored = value; return { type: "published", date: week.key, matches: true }; },
      verifyWebsite: async () => { verification++; return { status: "verified" }; },
      checkLogin: () => { throw new Error("must not authenticate"); },
      askCodex: () => { throw new Error("must not call Codex"); },
      exportArticles: () => { throw new Error("must not export articles"); }
    } };
  return { opts, summary, counts: () => ({ builds, verification }) };
}
test("weekly publication reuses duplicate protection without any AI calls", async t => {
  const { opts, counts } = publicationOptions(t);
  assert.equal((await launcher.run(opts)).status, "published");
  assert.equal((await launcher.run(opts)).status, "existing-server");
  assert.deepEqual(counts(), { builds: 1, verification: 1 });
});
test("weekly API failure resumes verification without rebuilding or overwriting", async t => {
  const { opts, counts } = publicationOptions(t);
  const verify = opts.dependencies.verifyWebsite;
  opts.dependencies.verifyWebsite = async () => { throw new Error("API unavailable"); };
  await assert.rejects(launcher.run(opts), /API unavailable/);
  opts.dependencies.verifyWebsite = verify;
  assert.equal((await launcher.run(opts)).status, "published");
  assert.deepEqual(counts(), { builds: 1, verification: 1 });
});
test("weekly check-only never gathers daily data or builds output", async t => {
  const { opts, counts } = publicationOptions(t);
  const result = await launcher.run({ ...opts, checkOnly: true });
  assert.equal(result.status, "checked");
  assert.equal(result.serverExists, false);
  assert.deepEqual(counts(), { builds: 0, verification: 0 });
});
test("a weekly canonical file is reused even if the daily inputs are unavailable", async t => {
  const { opts, summary, counts } = publicationOptions(t);
  opts.canonicalFile = path.join(opts.directory, "saved.json");
  core.writeJson(opts.canonicalFile, summary);
  assert.equal((await launcher.run(opts)).status, "published");
  assert.equal(counts().builds, 0);
});
function camel(value) {
  if (Array.isArray(value)) return value.map(camel);
  if (value && typeof value === "object") return Object.fromEntries(Object.entries(value).map(([key, item]) => [key[0].toLowerCase() + key.slice(1), camel(item)]));
  return value;
}
test("weekly verification checks the full weekly API body and rejects stale content", async () => {
  const summary = weekly.buildWeekly(week, inputs());
  const now = new Date("2026-09-07T00:00:00Z");
  const fetchImpl = async url => { assert.equal(url.pathname, "/api/weekly-summary"); return { ok: true, json: async () => camel(summary) }; };
  assert.equal((await publication.verifyWebsite({}, week.key, summary, { now, fetchImpl })).status, "verified");
  await assert.rejects(publication.verifyWebsite({}, week.key, summary, { now, fetchImpl: async () => ({ ok: true, json: async () => ({ ...camel(summary), headline: "wrong" }) }) }), /다릅니다/);
  assert.equal((await publication.verifyWebsite({}, week.key, summary, { now: new Date("2026-09-14T00:00:00Z"), fetchImpl: () => { throw new Error("must not fetch"); } })).status, "db-only");
});
test("all mode runs yesterday and the completed week, weekly mode skips unrelated yesterday", async t => {
  const calls = [];
  const opts = { root: directory(t), config: {}, now: new Date("2026-09-11T00:00:00Z"), log: () => {}, dependencies: {
    run: async options => { calls.push(options.date); return { status: "existing-server", date: options.date }; },
    readRemote: () => { throw new Error("must not gather for existing week"); } } };
  assert.equal((await runWorkflow(opts)).status, "completed");
  assert.deepEqual(calls, ["2026-09-10", week.key]);
  calls.length = 0;
  await runWorkflow({ ...opts, mode: "weekly" });
  assert.deepEqual(calls, [week.key]);
});
test("Monday workflow reuses the Sunday result and reads seven summaries only", async t => {
  const values = inputs().slice(0, 6), calls = [];
  const result = await runWorkflow({ root: directory(t), config: {}, now: new Date("2026-09-06T15:00:00Z"), log: () => {}, dependencies: {
    readRemote: remote(values),
    run: async opts => {
      calls.push(opts.date);
      if (opts.generateSummary) return { status: "published", date: opts.date, summary: await opts.generateSummary() };
      values.push(daily(opts.date)); return { status: "published", date: opts.date };
    } } });
  assert.deepEqual(calls, ["2026-09-06", week.key]);
  assert.equal(result.results.at(-1).summary.ArticleCount, 7);
});
test("Mongo weekly checks and daily summary reads never access the articles collection", () => {
  for (const mode of ["check", "read-summary"]) {
    const calls = [], records = [];
    const summary = daily(week.end);
    summary.GeneratedAt = { DateTime: new Date(summary.GeneratedAt) };
    const target = mode === "check" ? week.key : week.end;
    const cursor = { hint: name => { assert.equal(name, "Date_1"); return cursor; }, limit: n => { assert.equal(n, 1); return cursor; }, maxTimeMS: ms => { assert.equal(ms, 3000); return cursor; }, toArray: () => [mode === "check" ? { Date: target } : summary] };
    const context = { request: { date: target, mode }, print: line => records.push(JSON.parse(line)), db: { summaries: {
      getIndexes: () => [{ name: "Date_1", key: { Date: 1 }, unique: true }],
      find: (filter, projection) => { calls.push({ filter, projection }); return cursor; }
    }, get articles() { throw new Error("No article queries allowed"); } } };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, "read-news.mongosh.js"), "utf8"), context);
    assert.equal(calls.length, 1);
    assert.equal(calls[0].filter.Date, target);
    assert.equal(records[0].type, "existing");
    if (mode === "read-summary") assert.equal(records[0].summary.GeneratedAt, "2026-09-07T00:00:00.000Z");
  }
});

test("weekly Mongo publication uses the existing BSON format and only inserts once", () => {
  const summary = weekly.buildWeekly(week, inputs()), records = [];
  let stored, inserts = 0;
  const cursor = { hint: name => { assert.equal(name, "Date_1"); return cursor; },
    limit: n => { assert.equal(n, 1); return cursor; }, maxTimeMS: ms => { assert.equal(ms, 3000); return cursor; }, toArray: () => stored ? [stored] : [] };
  const context = { request: { date: week.key, summary }, Long: { fromString: value => ({ $numberLong: value }) },
    print: line => records.push(JSON.parse(line)), db: { summaries: {
      getIndexes: () => [{ name: "Date_1", key: { Date: 1 }, unique: true }],
      find: filter => { assert.equal(filter.Date, week.key); return cursor; },
      insertOne: (document, options) => { inserts++; stored = document; assert.equal(options.writeConcern.j, true); return { acknowledged: true }; }
    } } };
  const script = fs.readFileSync(path.join(__dirname, "publish-summary.mongosh.js"), "utf8");
  vm.runInNewContext(script, context);
  vm.runInNewContext(script, { ...context });
  assert.equal(inserts, 1);
  assert.equal(stored.GeneratedAt.DateTime.toISOString(), summary.GeneratedAt);
  assert.deepEqual(records.map(record => [record.type, record.matches]), [["published", true], ["existing", true]]);
});

test("weekly workflow check-only cannot enter generation even when the week is missing", async t => {
  let checks = 0;
  const result = await runWorkflow({ root: directory(t), config: {}, now: new Date("2026-09-11T00:00:00Z"), checkOnly: true, log: () => {},
    dependencies: { readRemote: () => { throw new Error("must not read inputs"); },
      run: opts => launcher.run({ ...opts, dependencies: { checkRemote: async () => { checks++; return false; } } }) } });
  assert.equal(result.status, "checked");
  assert.equal(checks, 2);
  assert.ok(result.results.every(item => item.serverExists === false));
});
