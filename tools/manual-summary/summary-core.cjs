const fs = require("node:fs");
const crypto = require("node:crypto");
const path = require("node:path");

const categories = ["정치/정책", "경제/산업", "사회", "국제", "IT/과학", "문화/연예", "스포츠", "생활/건강", "지역"];
const policyVersion = 1;
function requireThat(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

function hash(value) {
  return crypto.createHash("sha256").update(value).digest("hex");
}

function yesterday(now = new Date()) {
  const koreaOffsetMilliseconds = 9 * 3600000;
  const dayMilliseconds = 86400000;
  return new Date(now.getTime() + koreaOffsetMilliseconds - dayMilliseconds).toISOString().slice(0, 10);
}

function validateDate(date, now = new Date()) {
  requireThat(/^\d{4}-\d{2}-\d{2}$/.test(date), "날짜는 YYYY-MM-DD 형식이어야 합니다.");
  const parsed = new Date(date + "T00:00:00Z");
  requireThat(!Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === date, "존재하지 않는 날짜입니다.");
  requireThat(date <= yesterday(now), "완료된 날짜만 요약할 수 있습니다. 오늘과 미래 날짜는 제외합니다.");
  return date;
}
function readJson(file) {
  const content = fs.readFileSync(file, "utf8").replace(/^\uFEFF/, "");
  return JSON.parse(content);
}

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
function clean(value) {
  return String(value || "")
    .replace(/<[^>]*>/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function prepareArticles(raw, date) {
  const periodStart = Date.parse(date + "T00:00:00+09:00");
  const periodEnd = periodStart + 86400000;
  const articlesById = new Map();
  for (const article of raw) {
    const timestamp = Date.parse(article.PublishedAt);
    requireThat(timestamp >= periodStart && timestamp < periodEnd, "대상 날짜 밖의 기사가 포함되어 중단했습니다.");
    requireThat(typeof article.Id === "string" && article.Id.length > 0 && !articlesById.has(article.Id), "기사 ID가 없거나 중복되었습니다.");
    requireThat(typeof article.Title === "string" && article.Title.trim(), "제목이 없는 기사입니다.");
    articlesById.set(article.Id, article);
  }
  // Keep the existing source/title/author deduplication policy, without merging different publishers.
  const ordered = [...articlesById.values()].sort((firstArticle, secondArticle) => {
    const publishedAtDifference = Date.parse(secondArticle.PublishedAt) - Date.parse(firstArticle.PublishedAt);
    if (publishedAtDifference !== 0) {
      return publishedAtDifference;
    }
    return (Date.parse(secondArticle.FirstSeenAt) || 0) - (Date.parse(firstArticle.FirstSeenAt) || 0);
  });
  const deduplicationKeys = new Set();
  const articles = [];
  for (const article of ordered) {
    const key = [
      clean(article.Source).toLowerCase(),
      clean(article.Title).toLowerCase().replace(/[^\p{L}\p{N}]/gu, ""),
      clean(article.Author).toLowerCase() || "unknown"
    ].join("|");
    if (deduplicationKeys.has(key)) {
      continue;
    }
    deduplicationKeys.add(key);
    articles.push({ ...article, Key: `a${articles.length + 1}` });
  }
  return articles;
}
function makeBatches(articles, maxArticles = 150, maxCharacters = 140000) {
  const batches = [];
  let currentBatch = [];
  let currentCharacters = 0;
  for (const article of articles) {
    const content = clean(article.Content);
    const summary = clean(article.Summary);
    const item = {
      key: article.Key,
      title: article.Title,
      source: article.Source,
      rssSummary: summary.slice(0, 350),
      bodyExcerpt: content.slice(0, 1200),
      excerpted: content.length > 1200 || summary.length > 350
    };
    const itemCharacters = JSON.stringify(item).length;
    requireThat(itemCharacters <= maxCharacters, "기사 하나의 입력 크기가 상한을 초과했습니다.");

    const exceedsArticleLimit = currentBatch.length >= maxArticles;
    const exceedsCharacterLimit = currentCharacters + itemCharacters > maxCharacters;
    if (currentBatch.length && (exceedsArticleLimit || exceedsCharacterLimit)) {
      batches.push(currentBatch);
      currentBatch = [];
      currentCharacters = 0;
    }
    currentBatch.push(item);
    currentCharacters += itemCharacters;
  }
  if (currentBatch.length) {
    batches.push(currentBatch);
  }
  return batches;
}
const stringSchema = { type: "string" };
const stringArray = { type: "array", items: stringSchema };
const strictObject = properties => ({
  type: "object",
  properties,
  required: Object.keys(properties),
  additionalProperties: false
});
const topicSchema = strictObject({
  title: stringSchema,
  category: { type: "string", enum: categories },
  summary: stringSchema,
  articleKeys: stringArray,
  keywords: stringArray
});
const mapSchema = strictObject({ topics: { type: "array", items: topicSchema } });
const reduceSchema = strictObject({
  summary: stringSchema,
  issues: {
    type: "array",
    items: strictObject({
      title: stringSchema,
      summary: stringSchema,
      topicKeys: stringArray,
      keywords: stringArray,
      score: { type: "integer" },
      featured: { type: "boolean" }
    })
  }
});

function validateEvidenceCoverage(items, keyName, allowedKeys, label, { normalizeUnfeaturedEmptySummary = false } = {}) {
  const foundKeys = new Set();
  for (const item of items) {
    text(item.title, label + " title", 300);
    const isUnfeaturedEmptySummary = item.featured === false
      && typeof item.summary === "string"
      && !item.summary.trim();
    if (normalizeUnfeaturedEmptySummary && isUnfeaturedEmptySummary) {
      item.summary = "기사 본문에서 별도로 요약할 사실을 확인하지 못했습니다.";
    }
    text(item.summary, label + " summary");
    requireThat(
      Array.isArray(item.keywords)
        && item.keywords.length <= 15
        && item.keywords.every(word => typeof word === "string" && word.length <= 100),
      "키워드 형식 오류"
    );
    requireThat(Array.isArray(item[keyName]) && item[keyName].length > 0, `${label}: 근거가 없습니다.`);
    for (const key of item[keyName]) {
      requireThat(allowedKeys.has(key) && !foundKeys.has(key), `${label}: 알 수 없거나 중복된 근거 ${key}`);
      foundKeys.add(key);
    }
  }
  requireThat(
    foundKeys.size === allowedKeys.size,
    `${label}: 일부 입력이 누락되어 결과를 저장하지 않습니다. (${foundKeys.size}/${allowedKeys.size})`
  );
}
function validateMap(result, batch) {
  requireThat(Array.isArray(result.topics), "이슈 분류 결과 형식 오류");
  const articleKeys = new Set(batch.map(article => article.key));
  validateEvidenceCoverage(result.topics, "articleKeys", articleKeys, "기사 분류");
  requireThat(result.topics.every(topic => categories.includes(topic.category)), "지원하지 않는 카테고리입니다.");
  return result;
}
function validateReduction(result, topics) {
  text(result.summary, "카테고리 요약");
  requireThat(Array.isArray(result.issues), "카테고리 결과 형식 오류");
  const topicKeys = new Set(topics.map(topic => topic.key));
  validateEvidenceCoverage(result.issues, "topicKeys", topicKeys, "이슈 통합", {
    normalizeUnfeaturedEmptySummary: true
  });
  requireThat(
    result.issues.every(issue => Number.isInteger(issue.score)
      && issue.score >= 0
      && issue.score <= 100
      && typeof issue.featured === "boolean"),
    "중요도 또는 대표 이슈 형식 오류"
  );
  const featuredCount = result.issues.filter(issue => issue.featured).length;
  requireThat(featuredCount >= 1 && featuredCount <= 3, "카테고리별 대표 이슈는 1~3개여야 합니다.");
  return result;
}
function buildDraft(date, articles, topics, reductions, now = new Date()) {
  const articlesByKey = new Map(articles.map(article => [article.Key, article]));
  const topicsByKey = new Map(topics.map(topic => [topic.key, topic]));
  const categoryRows = [];
  const topIssues = [];
  for (const category of categories) {
    const categoryTopics = topics.filter(topic => topic.category === category);
    if (!categoryTopics.length) {
      continue;
    }
    const result = validateReduction(reductions[category], categoryTopics);
    const articleKeys = categoryTopics.flatMap(topic => topic.articleKeys);
    categoryRows.push({
      Category: category,
      IssueCount: result.issues.length,
      ArticleCount: articleKeys.length,
      Summary: result.summary
    });

    const featuredIssues = result.issues
      .filter(issue => issue.featured)
      .sort((firstIssue, secondIssue) => secondIssue.score - firstIssue.score);
    for (const issue of featuredIssues) {
      const evidence = issue.topicKeys
        .flatMap(key => topicsByKey.get(key).articleKeys)
        .map(key => articlesByKey.get(key));
      topIssues.push({
        Title: issue.title,
        Category: category,
        Summary: issue.summary,
        ArticleCount: evidence.length,
        ArticleIds: evidence.map(article => article.Id),
        Score: issue.score,
        Sources: [...new Set(evidence.map(article => clean(article.Source)).filter(Boolean))],
        Keywords: issue.keywords
      });
    }
  }
  requireThat(categoryRows.reduce((sum, row) => sum + row.ArticleCount, 0) === articles.length, "최종 기사 수가 일치하지 않습니다.");
  return {
    Date: date,
    GeneratedAt: now.toISOString(),
    Provider: "manual",
    Model: "Codex CLI",
    Headline: `${date} 전날 이슈 요약`,
    Summary: categoryRows.map(row => `${row.Category}: ${row.Summary}`).join("\n\n"),
    IssueCount: categoryRows.reduce((sum, row) => sum + row.IssueCount, 0),
    ArticleCount: articles.length,
    SourceCount: new Set(articles.map(article => clean(article.Source).toLowerCase()).filter(Boolean)).size,
    Categories: categoryRows,
    TopIssues: topIssues
  };
}
module.exports = {
  categories,
  policyVersion,
  requireThat,
  hash,
  yesterday,
  validateDate,
  readJson,
  writeJson,
  prepareArticles,
  makeBatches,
  mapSchema,
  reduceSchema,
  validateMap,
  validateReduction,
  buildDraft
};
