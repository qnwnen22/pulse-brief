function renderRelatedLinks(issue) {
  const links = (issue.relatedLinks || [])
    .map((link) => ({
      title: link.title || issue.title,
      source: link.source || issue.source,
      url: safeUrl(link.url),
      imageUrl: safeUrl(link.imageUrl),
    }))
    .filter((link) => link.url);

  if (!links.length) {
    return '<span class="source-empty">연결된 출처가 없습니다.</span>';
  }

  return links
    .map((link) => {
      return `
        <a class="source-link" href="${escapeHtml(link.url)}" target="_blank" rel="noopener noreferrer">
          <strong>${escapeHtml(displaySourceName(link.source, link.url))}</strong>
          <span>${escapeHtml(link.title)}</span>
        </a>
      `;
    })
    .join("");
}

function normalizeTitle(value) {
  return String(value || "")
    .replace(/\s+/g, " ")
    .replace(/[^\p{L}\p{N}\s]/gu, "")
    .trim()
    .toLowerCase();
}

function mergeTrackedIssues(summaryIssue, matchedIssues) {
  const linksByUrl = new Map();
  const articleIds = new Set((summaryIssue.articleIds || []).filter(Boolean));
  const publishers = new Set();
  const sources = new Set();

  matchedIssues.forEach((issue) => {
    (issue.articleIds || []).forEach((articleId) => {
      if (articleId) articleIds.add(articleId);
    });
    (issue.publishers || []).forEach((publisher) => {
      if (publisher) publishers.add(publisher);
    });
    getSourceNames(issue).forEach((source) => sources.add(source));
    (issue.relatedLinks || []).forEach((link) => {
      const url = safeUrl(link.url);
      if (!url || linksByUrl.has(url)) return;
      linksByUrl.set(url, link);
    });
  });

  const first = matchedIssues[0] || {};
  return {
    ...first,
    title: summaryIssue.title || first.title,
    category: summaryIssue.category || first.category,
    summary: summaryIssue.summary || first.summary,
    source: [...sources].slice(0, 2).join(", ") || first.source,
    publishers: [...publishers],
    articleCount: Number(summaryIssue.articleCount || articleIds.size || first.articleCount || linksByUrl.size || 0),
    articleIds: [...articleIds],
    relatedLinks: [...linksByUrl.values()],
  };
}

function findTrackedIssue(summaryIssue, candidates) {
  const articleIds = new Set((summaryIssue.articleIds || []).filter(Boolean));
  if (articleIds.size) {
    const byArticleId = candidates.filter((candidate) =>
      (candidate.articleIds || []).some((articleId) => articleIds.has(articleId))
    );
    if (byArticleId.length) return mergeTrackedIssues(summaryIssue, byArticleId);
  }

  const title = normalizeTitle(summaryIssue.title);
  const sameCategory = candidates.filter((candidate) => !summaryIssue.category || candidate.category === summaryIssue.category);
  return sameCategory.find((candidate) => normalizeTitle(candidate.title) === title)
    || candidates.find((candidate) => normalizeTitle(candidate.title) === title)
    || sameCategory.find((candidate) => {
      const candidateTitle = normalizeTitle(candidate.title);
      return title && (candidateTitle.includes(title) || title.includes(candidateTitle));
    })
    || null;
}

function renderTrackedIssueList(items, targetItems, options = {}) {
  const listClass = options.listClass || "weekly-issue-list";
  const pickerClass = options.pickerClass || "weekly-source-picker";
  const ariaLabel = options.ariaLabel || "관련 기사 보기";

  return `
    <ol class="${listClass}">
      ${items.map((issue) => renderTrackedIssueItem(issue, targetItems, pickerClass, ariaLabel)).join("")}
    </ol>
  `;
}

function renderTrackedIssueItem(issue, targetItems, pickerClass, ariaLabel) {
  const trackedIssue = issue.relatedLinks?.length ? issue : findTrackedIssue(issue, targetItems);
  const linkCount = trackedIssue?.relatedLinks?.length || 0;
  const articleCount = Number(issue.articleCount || trackedIssue?.articleCount || linkCount || 0);

  return `
    <li>
      <div class="weekly-issue-content">
        <strong>${escapeHtml(issue.category || trackedIssue?.category || activeWeeklyCategory)}</strong>
        <span>${escapeHtml(issue.title)}</span>
      </div>
      <details class="${pickerClass}">
        <summary class="source-button" aria-label="${escapeHtml(ariaLabel)}">
          관련 기사${articleCount ? ` ${articleCount.toLocaleString("ko-KR")}건` : ""}
        </summary>
        <div class="weekly-source-menu">
          ${trackedIssue ? renderRelatedLinks(trackedIssue) : '<span class="source-empty">연결된 관련 기사를 찾지 못했습니다.</span>'}
        </div>
      </details>
    </li>
  `;
}
