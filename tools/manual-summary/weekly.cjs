const fs = require("node:fs");
const path = require("node:path");
const core = require("./summary-core.cjs");
const publication = require("./publication.cjs");
const { requireThat, readJson, writeJson } = core;

function validateDaily(value, date) {
  requireThat(["manual", "local", "openai"].includes(value?.Provider), `${date}: 지원하지 않는 일간 요약입니다.`);
  // Reuse the same structural/evidence checks without changing the stored provider.
  return { ...publication.normalizeSummary({ ...value, Provider: "manual" }, date), Provider: value.Provider };
}
function buildWeekly(week, values, now = new Date()) {
  requireThat(values.length === 7, "7일의 일간 요약이 모두 필요합니다. 불완전한 주간 요약은 배포하지 않습니다.");
  const summaries = week.dates.map(date => {
    const matches = values.filter(value => value.Date === date);
    requireThat(matches.length === 1, `${date}: 일간 요약이 없거나 중복되었습니다.`);
    return validateDaily(matches[0], date);
  });
  const groups = new Map();
  for (const daily of summaries) for (const issue of daily.TopIssues) {
    const key = `${issue.Category}|${issue.Title.normalize("NFKC").replace(/\s+/g, " ").trim().toLowerCase()}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push({ date: daily.Date, issue });
  }
  const merged = [...groups.values()].map(group => {
    const latest = group.at(-1), days = new Set(group.map(item => item.date)).size;
    const ids = [...new Set(group.flatMap(item => item.issue.ArticleIds))];
    return { ...latest.issue, Summary: `${latest.date} 일간 요약 기준: ${latest.issue.Summary}`,
      ArticleIds: ids, ArticleCount: ids.length,
      Score: Math.min(100, Math.max(...group.map(item => item.issue.Score)) + Math.min(20, (days - 1) * 5)),
      Sources: [...new Set(group.flatMap(item => item.issue.Sources))],
      Keywords: [...new Set(group.flatMap(item => item.issue.Keywords))].slice(0, 15), days, lastDate: latest.date };
  }).sort((a, b) => b.Score - a.Score || b.days - a.days || b.Sources.length - a.Sources.length || b.ArticleCount - a.ArticleCount || b.lastDate.localeCompare(a.lastDate) || a.Title.localeCompare(b.Title, "ko"));
  const categories = [], topIssues = [];
  for (const category of core.categories) {
    const rows = summaries.flatMap(summary => summary.Categories).filter(row => row.Category === category);
    if (!rows.length) continue;
    const chosen = merged.filter(issue => issue.Category === category).slice(0, 3);
    requireThat(chosen.length, `${category}: 근거 대표 이슈가 없습니다.`);
    categories.push({ Category: category,
      ArticleCount: rows.reduce((sum, row) => sum + row.ArticleCount, 0),
      IssueCount: rows.reduce((sum, row) => sum + row.IssueCount, 0),
      Summary: chosen.map(issue => `${issue.lastDate}: ${issue.Title}`).join(" / ") });
    topIssues.push(...chosen.map(({ days, lastDate, ...issue }) => issue));
  }
  const sources = new Set(summaries.flatMap(summary => summary.TopIssues.flatMap(issue => issue.Sources)));
  return publication.normalizeSummary({ Date: week.key, GeneratedAt: now.toISOString(), Provider: "manual", Model: "Local daily rollup",
    Headline: `${week.start}~${week.end} 주간 이슈 요약`,
    Summary: `7일의 저장된 일간 요약을 합산했습니다. 기사·이슈 수는 일간 요약의 합계이며 주간 전체 기사를 재분류한 수가 아닙니다.\n\n${categories.map(row => `${row.Category}: ${row.Summary}`).join("\n\n")}`,
    ArticleCount: categories.reduce((sum, row) => sum + row.ArticleCount, 0),
    IssueCount: categories.reduce((sum, row) => sum + row.IssueCount, 0), SourceCount: sources.size,
    Categories: categories, TopIssues: topIssues }, week.key);
}
async function readDaily(config, date, readRemote) {
  const records = [];
  await readRemote(config, { date, mode: "read-summary" }, record => records.push(record));
  requireThat(records.length === 1 && records[0].date === date && ["existing", "missing"].includes(records[0].type), `${date}: 일간 요약 조회 응답 오류`);
  return records[0].type === "missing" ? null : validateDaily(records[0].summary, date);
}
async function gatherDaily({ week, directory, config, ensureDaily, readRemote, log = console.log }) {
  const file = path.join(directory, "daily-input.json");
  if (fs.existsSync(file)) {
    const saved = readJson(file);
    requireThat(saved.key === week.key && saved.sha256 === core.hash(JSON.stringify(saved.summaries)), "주간 입력 복구 기록 검증 실패");
    buildWeekly(week, saved.summaries);
    log("검증된 주간 일간 요약 자료를 재사용합니다.");
    return saved.summaries;
  }
  const summaries = [];
  for (const date of week.dates) {
    let summary = await readDaily(config, date, readRemote);
    if (!summary) {
      log(`${date}: 주간에 필요한 일간 요약이 없어 순차 생성·배포합니다.`);
      const result = await ensureDaily(date);
      requireThat(["published", "existing-server"].includes(result.status), `${date}: 일간 요약을 확보하지 못했습니다. 주간 배포를 중단합니다.`);
      summary = await readDaily(config, date, readRemote);
    }
    requireThat(summary, `${date}: 게시된 일간 요약을 읽지 못했습니다.`);
    summaries.push(summary);
  }
  buildWeekly(week, summaries);
  writeJson(file, { key: week.key, sha256: core.hash(JSON.stringify(summaries)), summaries });
  return summaries;
}
module.exports = { validateDaily, buildWeekly, readDaily, gatherDaily };
