const fs = require("node:fs");
const crypto = require("node:crypto");
const path = require("node:path");

const categories = ["정치/정책", "경제/산업", "사회", "국제", "IT/과학", "문화/연예", "스포츠", "생활/건강", "지역"];
const policyVersion = 1;
function requireThat(condition, message) { if (!condition) throw new Error(message); }
function hash(value) { return crypto.createHash("sha256").update(value).digest("hex"); }
function yesterday(now = new Date()) { return new Date(now.getTime() + 9 * 3600000 - 86400000).toISOString().slice(0, 10); }
function validateDate(date, now = new Date()) {
  requireThat(/^\d{4}-\d{2}-\d{2}$/.test(date), "날짜는 YYYY-MM-DD 형식이어야 합니다.");
  const parsed = new Date(date + "T00:00:00Z");
  requireThat(!Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === date, "존재하지 않는 날짜입니다.");
  requireThat(date <= yesterday(now), "완료된 날짜만 요약할 수 있습니다. 오늘과 미래 날짜는 제외합니다.");
  return date;
}
function readJson(file) { return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, "")); }
function writeJson(file, value) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const temporary = file + ".tmp";
  fs.writeFileSync(temporary, JSON.stringify(value, null, 2), "utf8");
  fs.renameSync(temporary, file);
}
function text(value, name, max = 3000) {
  requireThat(typeof value === "string" && value.trim().length > 0 && value.length <= max, `${name}: 비어 있거나 너무 긴 문자열입니다.`);
  return value.trim();
}
function clean(value) { return String(value || "").replace(/<[^>]*>/g, " ").replace(/\s+/g, " ").trim(); }
function prepareArticles(raw, date) {
  const start = Date.parse(date + "T00:00:00+09:00");
  const byId = new Map();
  for (const article of raw) {
    const timestamp = Date.parse(article.PublishedAt);
    requireThat(timestamp >= start && timestamp < start + 86400000, "대상 날짜 밖의 기사가 포함되어 중단했습니다.");
    requireThat(typeof article.Id === "string" && article.Id.length > 0 && !byId.has(article.Id), "기사 ID가 없거나 중복되었습니다.");
    requireThat(typeof article.Title === "string" && article.Title.trim(), "제목이 없는 기사입니다.");
    byId.set(article.Id, article);
  }
  // Keep the existing source/title/author deduplication policy, without merging different publishers.
  const ordered = [...byId.values()].sort((a, b) => Date.parse(b.PublishedAt) - Date.parse(a.PublishedAt) || (Date.parse(b.FirstSeenAt) || 0) - (Date.parse(a.FirstSeenAt) || 0));
  const keys = new Set();
  const articles = [];
  for (const article of ordered) {
    const key = [clean(article.Source).toLowerCase(), clean(article.Title).toLowerCase().replace(/[^\p{L}\p{N}]/gu, ""), clean(article.Author).toLowerCase() || "unknown"].join("|");
    if (keys.has(key)) continue;
    keys.add(key);
    articles.push({ ...article, Key: `a${articles.length + 1}` });
  }
  return articles;
}
function makeBatches(articles, maxArticles = 150, maxCharacters = 140000) {
  const batches = [];
  let current = [], size = 0;
  for (const article of articles) {
    const content = clean(article.Content);
    const summary = clean(article.Summary);
    const item = { key: article.Key, title: article.Title, source: article.Source, rssSummary: summary.slice(0, 350), bodyExcerpt: content.slice(0, 1200), excerpted: content.length > 1200 || summary.length > 350 };
    const length = JSON.stringify(item).length;
    requireThat(length <= maxCharacters, "기사 하나의 입력 크기가 상한을 초과했습니다.");
    if (current.length && (current.length >= maxArticles || size + length > maxCharacters)) {
      batches.push(current); current = []; size = 0;
    }
    current.push(item); size += length;
  }
  if (current.length) batches.push(current);
  return batches;
}
const stringSchema = { type: "string" };
const stringArray = { type: "array", items: stringSchema };
const strictObject = properties => ({ type: "object", properties, required: Object.keys(properties), additionalProperties: false });
const topicSchema = strictObject({ title: stringSchema, category: { type: "string", enum: categories }, summary: stringSchema, articleKeys: stringArray, keywords: stringArray });
const mapSchema = strictObject({ topics: { type: "array", items: topicSchema } });
const reduceSchema = strictObject({ summary: stringSchema, issues: { type: "array", items: strictObject({ title: stringSchema, summary: stringSchema, topicKeys: stringArray, keywords: stringArray, score: { type: "integer" }, featured: { type: "boolean" } }) } });
function coverage(items, keyName, allowed, label) {
  const found = new Set();
  for (const item of items) {
    text(item.title, label + " title", 300);
    text(item.summary, label + " summary");
    requireThat(Array.isArray(item.keywords) && item.keywords.length <= 15 && item.keywords.every(word => typeof word === "string" && word.length <= 100), "키워드 형식 오류");
    requireThat(Array.isArray(item[keyName]) && item[keyName].length > 0, `${label}: 근거가 없습니다.`);
    for (const key of item[keyName]) {
      requireThat(allowed.has(key) && !found.has(key), `${label}: 알 수 없거나 중복된 근거 ${key}`);
      found.add(key);
    }
  }
  requireThat(found.size === allowed.size, `${label}: 일부 입력이 누락되어 결과를 저장하지 않습니다. (${found.size}/${allowed.size})`);
}
function validateMap(result, batch) {
  requireThat(Array.isArray(result.topics), "이슈 분류 결과 형식 오류");
  coverage(result.topics, "articleKeys", new Set(batch.map(article => article.key)), "기사 분류");
  requireThat(result.topics.every(topic => categories.includes(topic.category)), "지원하지 않는 카테고리입니다.");
  return result;
}
function validateReduction(result, topics) {
  text(result.summary, "카테고리 요약");
  requireThat(Array.isArray(result.issues), "카테고리 결과 형식 오류");
  coverage(result.issues, "topicKeys", new Set(topics.map(topic => topic.key)), "이슈 통합");
  requireThat(result.issues.every(issue => Number.isInteger(issue.score) && issue.score >= 0 && issue.score <= 100 && typeof issue.featured === "boolean"), "중요도 또는 대표 이슈 형식 오류");
  const featured = result.issues.filter(issue => issue.featured).length;
  requireThat(featured >= 1 && featured <= 3, "카테고리별 대표 이슈는 1~3개여야 합니다.");
  return result;
}
function buildDraft(date, articles, topics, reductions, now = new Date()) {
  const byKey = new Map(articles.map(article => [article.Key, article]));
  const byTopic = new Map(topics.map(topic => [topic.key, topic]));
  const categoryRows = [], topIssues = [];
  for (const category of categories) {
    const ownTopics = topics.filter(topic => topic.category === category);
    if (!ownTopics.length) continue;
    const result = validateReduction(reductions[category], ownTopics);
    const articleKeys = ownTopics.flatMap(topic => topic.articleKeys);
    categoryRows.push({ Category: category, IssueCount: result.issues.length, ArticleCount: articleKeys.length, Summary: result.summary });
    for (const issue of result.issues.filter(issue => issue.featured).sort((a, b) => b.score - a.score)) {
      const evidence = issue.topicKeys.flatMap(key => byTopic.get(key).articleKeys).map(key => byKey.get(key));
      topIssues.push({ Title: issue.title, Category: category, Summary: issue.summary, ArticleCount: evidence.length,
        ArticleIds: evidence.map(article => article.Id), Score: issue.score,
        Sources: [...new Set(evidence.map(article => clean(article.Source)).filter(Boolean))], Keywords: issue.keywords });
    }
  }
  requireThat(categoryRows.reduce((sum, row) => sum + row.ArticleCount, 0) === articles.length, "최종 기사 수가 일치하지 않습니다.");
  return { Date: date, GeneratedAt: now.toISOString(), Provider: "manual", Model: "Codex CLI",
    Headline: `${date} 전날 이슈 요약`, Summary: categoryRows.map(row => `${row.Category}: ${row.Summary}`).join("\n\n"),
    IssueCount: categoryRows.reduce((sum, row) => sum + row.IssueCount, 0), ArticleCount: articles.length,
    SourceCount: new Set(articles.map(article => clean(article.Source).toLowerCase()).filter(Boolean)).size,
    Categories: categoryRows, TopIssues: topIssues };
}
function escapeHtml(value) { return String(value ?? "").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[char])); }
function safeUrl(value) { try { const url = new URL(value); return ["https:", "http:"].includes(url.protocol) ? url.href : ""; } catch { return ""; } }
function reviewHtml(draft, articles = [], logo = "", status = "미배포 검토본") {
  const byId = new Map(articles.map(article => [article.Id, article]));
  function issueHtml(issue) {
    const evidence = issue.ArticleIds.map(id => byId.get(id)).filter(Boolean);
    const links = evidence.map(article => `<li>${safeUrl(article.Url) ? `<a href="${escapeHtml(safeUrl(article.Url))}" target="_blank" rel="noopener noreferrer">${escapeHtml(article.Title)}</a>` : escapeHtml(article.Title)} <small>${escapeHtml(article.Source)}</small></li>`).join("");
    const related = links ? `<details><summary>관련 기사 ${issue.ArticleCount}건</summary><ul>${links}</ul></details>` : `<p class="counts">관련 기사 ${issue.ArticleCount}건 · ${escapeHtml((issue.Sources || []).join(", "))}</p>`;
    return `<article><h3>${escapeHtml(issue.Title)}</h3><p>${escapeHtml(issue.Summary)}</p>${related}</article>`;
  }
  const sections = draft.Categories.map(category => `<section><h2>${escapeHtml(category.Category)}</h2><p class="counts">기사 ${category.ArticleCount}건 · 이슈 ${category.IssueCount}건</p><p>${escapeHtml(category.Summary)}</p>${draft.TopIssues.filter(issue => issue.Category === category.Category).map(issueHtml).join("")}</section>`).join("");
  return `<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>${escapeHtml(draft.Date)} 전날 뉴스 요약 초안</title><style>body{margin:0;color:#20272a;background:#f5f7f8;font:16px/1.7 "Malgun Gothic",sans-serif;letter-spacing:0}header,main,footer{max-width:960px;margin:auto;padding:24px}header{border-bottom:2px solid #15856b}header img{width:112px;height:80px;object-fit:cover;float:right;margin:0 0 16px 16px}h1{font-size:28px;line-height:1.35;clear:both}h2{font-size:22px}h3{font-size:18px}section{padding:20px 0;border-bottom:1px solid #cbd5d7}article{padding:12px 0}p,li,h1,h2,h3{word-break:keep-all;overflow-wrap:anywhere}a{color:#165caa}small,.counts,footer{color:#526267}.status{color:#076951;font-weight:700}summary{cursor:pointer;color:#165caa}li{margin:8px 0}footer{font-size:14px}@media(max-width:600px){header,main,footer{padding:18px}h1{font-size:24px}}</style><header>${logo ? `<img src="${escapeHtml(logo)}" alt="Pulse Brief">` : ""}<p class="status">${escapeHtml(status)}</p><h1>${escapeHtml(draft.Date)} 전날 뉴스 요약</h1><p>기사 ${draft.ArticleCount}건 · 이슈 ${draft.IssueCount}건 · 출처 ${draft.SourceCount}곳</p></header><main>${sections}</main><footer>제목·RSS 요약·저장된 본문 발췌 기반. 기사와 관련 링크를 확인한 뒤 배포를 승인해 주세요. 이 실행 도구는 사이트에 쓰지 않습니다.</footer></html>`;
}
module.exports = { categories, policyVersion, requireThat, hash, yesterday, validateDate, readJson, writeJson, prepareArticles, makeBatches, mapSchema, reduceSchema, validateMap, validateReduction, buildDraft, reviewHtml };
