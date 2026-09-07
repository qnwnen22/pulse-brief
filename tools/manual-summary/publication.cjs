const { requireThat, categories, yesterday, validateDate, hash } = require("./summary-core.cjs");

function normalizeSummary(value, date) {
  validateDate(date);
  requireThat(value?.Date === date && value.Provider === "manual", "배포할 수동 요약의 날짜 또는 생성 방식이 올바르지 않습니다.");
  function text(value, name, max = 3000) {
    requireThat(typeof value === "string" && value.trim() && value.length <= max, `${name} 형식 오류`);
    return value.trim();
  }
  function count(value, name, min = 1, max = 50000) {
    requireThat(Number.isInteger(value) && value >= min && value <= max, `${name} 범위 오류`);
    return value;
  }
  function strings(value, name, max) {
    requireThat(Array.isArray(value) && value.length <= max, `${name} 배열 오류`);
    const result = value.map(item => text(item, name, 300));
    requireThat(new Set(result).size === result.length, `${name} 중복 오류`);
    return result;
  }
  const generatedAt = new Date(text(value.GeneratedAt, "생성 시각", 50));
  requireThat(Number.isFinite(generatedAt.getTime()), "생성 시각 형식 오류");
  requireThat(Array.isArray(value.Categories) && value.Categories.length > 0 && value.Categories.length <= categories.length, "카테고리 형식 오류");
  const rows = value.Categories.map(row => {
    requireThat(categories.includes(row.Category), "지원하지 않는 카테고리입니다.");
    return { Category: row.Category, IssueCount: count(row.IssueCount, "카테고리 이슈 수"), ArticleCount: count(row.ArticleCount, "카테고리 기사 수"), Summary: text(row.Summary, "카테고리 요약") };
  });
  requireThat(new Set(rows.map(row => row.Category)).size === rows.length, "카테고리 중복 오류");
  requireThat(Array.isArray(value.TopIssues) && value.TopIssues.length > 0 && value.TopIssues.length <= 27, "대표 이슈 형식 오류");
  const allIds = new Set();
  const issues = value.TopIssues.map(issue => {
    requireThat(rows.some(row => row.Category === issue.Category), "대표 이슈의 카테고리가 없습니다.");
    const ids = strings(issue.ArticleIds, "기사 ID", 50000);
    requireThat(ids.length === issue.ArticleCount, "대표 이슈의 기사 수와 근거 ID 수가 다릅니다.");
    for (const id of ids) { requireThat(!allIds.has(id), "대표 이슈 사이에 기사 ID가 중복되었습니다."); allIds.add(id); }
    return { Title: text(issue.Title, "이슈 제목", 300), Category: issue.Category, Summary: text(issue.Summary, "이슈 요약"),
      ArticleCount: count(issue.ArticleCount, "관련 기사 수"), ArticleIds: ids, Score: count(issue.Score, "중요도", 0, 100),
      Sources: strings(issue.Sources, "언론사", 1000), Keywords: strings(issue.Keywords, "키워드", 15) };
  });
  const summary = { Date: date, GeneratedAt: generatedAt.toISOString(), Provider: "manual", Model: text(value.Model, "모델", 100),
    Headline: text(value.Headline, "전체 제목", 300), Summary: text(value.Summary, "전체 요약", 30000),
    IssueCount: count(value.IssueCount, "전체 이슈 수"), ArticleCount: count(value.ArticleCount, "전체 기사 수"), SourceCount: count(value.SourceCount, "전체 언론사 수", 1, 1000), Categories: rows, TopIssues: issues };
  requireThat(rows.reduce((sum, row) => sum + row.ArticleCount, 0) === summary.ArticleCount, "카테고리별 기사 수 합계가 전체와 다릅니다.");
  requireThat(rows.reduce((sum, row) => sum + row.IssueCount, 0) === summary.IssueCount, "카테고리별 이슈 수 합계가 전체와 다릅니다.");
  for (const row of rows) {
    const own = issues.filter(issue => issue.Category === row.Category);
    requireThat(own.length <= 3 && own.length <= row.IssueCount && row.IssueCount <= row.ArticleCount, "카테고리별 이슈 수 범위 오류");
    requireThat(own.reduce((sum, issue) => sum + issue.ArticleCount, 0) <= row.ArticleCount, "대표 이슈의 기사 수가 카테고리 기사 수를 초과했습니다.");
  }
  requireThat(summary.SourceCount <= summary.ArticleCount, "언론사 수가 기사 수를 초과했습니다.");
  requireThat(Buffer.byteLength(JSON.stringify(summary)) <= 2 * 1024 * 1024, "요약 배포 크기 상한 2MB 초과");
  return summary;
}
function siteUrl(config) {
  const url = new URL(config.SiteUrl || "https://news.pulse-brief.co.kr");
  requireThat(url.protocol === "https:" && !url.username && !url.password && url.pathname === "/" && !url.search && !url.hash, "공개 사이트는 HTTPS 기본 주소여야 합니다.");
  return url;
}
function fingerprint(summary) { return hash(JSON.stringify(summary)); }
async function verifyWebsite(config, date, expected, { fetchImpl = fetch, now = new Date() } = {}) {
  if (date !== yesterday(now)) return { status: "db-only", reason: "공개 전날 API의 대상 날짜가 아니므로 DB 반영만 확인했습니다." };
  const url = new URL("/api/daily-summary", siteUrl(config));
  const response = await fetchImpl(url, { headers: { "Cache-Control": "no-cache" }, cache: "no-store", redirect: "error", signal: AbortSignal.timeout(15000) });
  requireThat(response.ok, `운영 DB 반영 후 사이트 확인 실패 (HTTP ${response.status}). 다시 실행하면 재생성 없이 배포 상태부터 확인합니다.`);
  const actual = await response.json();
  requireThat(actual?.date === date && actual.provider === "manual" && actual.articleCount > 0 && actual.categories?.length > 0 && actual.topIssues?.length > 0, "사이트 응답에서 대상 날짜의 수동 요약을 확인하지 못했습니다.");
  if (expected) {
    function pascal(value) {
      if (Array.isArray(value)) return value.map(pascal);
      if (value && typeof value === "object") return Object.fromEntries(Object.entries(value).map(([key, item]) => [key[0].toUpperCase() + key.slice(1), pascal(item)]));
      return value;
    }
    requireThat(fingerprint(normalizeSummary(pascal(actual), date)) === fingerprint(expected), "사이트 응답이 배포한 요약과 다릅니다. 재실행 시 DB를 덮어쓰지 않고 다시 확인합니다.");
  }
  return { status: "verified", url: url.href };
}
module.exports = { normalizeSummary, siteUrl, fingerprint, verifyWebsite };
