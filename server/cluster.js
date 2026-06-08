import { config } from "./config.js";

export function cosineSimilarity(a, b) {
  if (!a || !b || a.length !== b.length) return 0;
  return a.reduce((sum, value, index) => sum + value * b[index], 0);
}

function centroid(articles) {
  const size = articles[0]?.embedding?.length || 0;
  if (!size) return [];
  const vector = Array.from({ length: size }, () => 0);

  articles.forEach((article) => {
    article.embedding.forEach((value, index) => {
      vector[index] += value;
    });
  });

  return vector.map((value) => value / articles.length);
}

function categoryFor(text) {
  const rules = [
    ["경제", ["환율", "물가", "금리", "증시", "투자", "반도체", "기업", "시장"]],
    ["기술", ["AI", "인공지능", "보안", "플랫폼", "데이터", "검색", "모바일"]],
    ["사회", ["폭염", "안전", "교육", "의료", "전력", "정책", "사건"]],
    ["문화", ["OTT", "영화", "웹툰", "음악", "스포츠", "콘텐츠", "공연"]],
  ];
  const found = rules.find(([, words]) => words.some((word) => text.includes(word)));
  return found?.[0] || "이슈";
}

export function groupSimilarArticles(articles) {
  const groups = [];

  articles.forEach((article) => {
    let bestGroup = null;
    let bestScore = 0;

    groups.forEach((group) => {
      const score = cosineSimilarity(article.embedding, group.centroid);
      if (score > bestScore) {
        bestGroup = group;
        bestScore = score;
      }
    });

    if (bestGroup && bestScore >= config.groupSimilarityThreshold) {
      bestGroup.articles.push(article);
      bestGroup.centroid = centroid(bestGroup.articles);
    } else {
      groups.push({
        id: `group-${groups.length + 1}`,
        centroid: article.embedding,
        articles: [article],
      });
    }
  });

  return groups.map((group) => {
    const articlesByImpact = [...group.articles].sort((a, b) => {
      return new Date(b.publishedAt || b.firstSeenAt) - new Date(a.publishedAt || a.firstSeenAt);
    });
    const keywordText = articlesByImpact.map((article) => `${article.title} ${article.summary || ""}`).join(" ");

    const sourceBonus = new Set(articlesByImpact.map((article) => article.source)).size * 4;

    return {
      id: group.id,
      category: categoryFor(keywordText),
      articleIds: articlesByImpact.map((article) => article.id),
      articleCount: articlesByImpact.length,
      sources: [...new Set(articlesByImpact.map((article) => article.source))],
      latestPublishedAt: articlesByImpact[0]?.publishedAt || new Date().toISOString(),
      score: Math.min(100, 55 + articlesByImpact.length * 8 + sourceBonus),
      seedTitle: articlesByImpact[0]?.title || "새 이슈",
      seedSummary: articlesByImpact[0]?.summary || "",
      representativeTitle: "",
      summary: "",
    };
  });
}
