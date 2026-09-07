function startOfDay(date) {
  const result = new Date(date);
  result.setHours(0, 0, 0, 0);
  return result;
}

function getSourceNames(issue) {
  const values = [issue.source, ...(issue.relatedLinks || []).map((link) => link.source)]
    .flatMap((source) => String(source || "").split(","))
    .map((source) => source.trim())
    .filter(Boolean);
  return [...new Set(values)];
}

function getPublisherNames(issue) {
  const values = [
    ...(issue.publishers || []),
    ...(issue.relatedLinks || []).map((link) => link.publisher),
  ]
    .map((publisher) => String(publisher || "").trim())
    .filter(Boolean);
  return [...new Set(values)];
}

function getSourceCount(issue) {
  return getSourceNames(issue).length || (issue.source ? 1 : 0);
}

function getIssueImageUrl(issue) {
  return safeUrl(issue.imageUrl) || safeUrl((issue.relatedLinks || []).find((link) => link.imageUrl)?.imageUrl);
}

function matchesDateFilter(issue) {
  const value = dateFilter?.value || "all";
  if (value === "all") return true;

  const issueDay = getKoreaDateKey(getIssueDate(issue));
  const today = getKoreaDateKey(new Date());
  const yesterday = getKoreaDateKey(new Date(Date.now() - 24 * 60 * 60 * 1000));
  const oneDay = 24 * 60 * 60 * 1000;

  if (value === "today") return issueDay === today;
  if (value === "yesterday") return issueDay === yesterday;
  if (value === "week") return getIssueDate(issue).getTime() >= Date.now() - 7 * oneDay;
  return true;
}

function matchesPublisherFilter(issue) {
  const selectedPublisher = publisherFilter?.value || "all";
  if (selectedPublisher === "all") return true;
  return getPublisherNames(issue).includes(selectedPublisher);
}

function matchesArticleCountFilter(issue) {
  const minCount = Number(articleCountFilter?.value || 0);
  if (!minCount) return true;
  return Number(issue.articleCount || 1) >= minCount;
}

function compareIssues(a, b) {
  const sortValue = sortSelect?.value || "latest";
  const dateDiff = getIssueDate(b) - getIssueDate(a);
  const impactDiff = Number(b.impact || 0) - Number(a.impact || 0);
  const articleDiff = Number(b.articleCount || 1) - Number(a.articleCount || 1);

  if (sortValue === "oldest") return getIssueDate(a) - getIssueDate(b);
  if (sortValue === "impactDesc") return impactDiff || dateDiff;
  if (sortValue === "impactAsc") return -impactDiff || dateDiff;
  if (sortValue === "articleCountDesc") return articleDiff || impactDiff || dateDiff;
  if (sortValue === "articleCountAsc") return -articleDiff || dateDiff;
  if (sortValue === "titleAsc") return String(a.title || "").localeCompare(String(b.title || ""), "ko") || dateDiff;
  return dateDiff || impactDiff;
}

function getVisibleIssues() {
  const query = (searchInput?.value || "").trim().toLowerCase();
  return issues.filter((issue) => {
    const matchesFilter = activeFilter === "전체" || issue.category === activeFilter;
    const publisherText = getPublisherNames(issue).join(" ");
    const relatedText = (issue.relatedLinks || []).map((link) => `${link.title} ${link.source} ${link.publisher || ""}`).join(" ");
    const text = `${issue.title} ${issue.category} ${issue.source} ${publisherText} ${issue.summary} ${relatedText} ${issue.keywords.join(" ")}`.toLowerCase();
    return matchesFilter
      && (!query || text.includes(query))
      && matchesDateFilter(issue)
      && matchesPublisherFilter(issue)
      && matchesArticleCountFilter(issue)
  }).sort(compareIssues);
}

function renderNews() {
  const visible = getVisibleIssues();
  const pageCount = Math.max(1, Math.ceil(visible.length / pageSize));
  if (currentPage > pageCount) currentPage = pageCount;
  const startIndex = (currentPage - 1) * pageSize;
  const pageItems = visible.slice(startIndex, startIndex + pageSize);

  newsList.innerHTML = "";

  if (!visible.length) {
    newsList.innerHTML = '<div class="empty-state">검색 조건에 맞는 이슈가 없습니다.</div>';
    renderMetrics(visible);
    renderWeeklySummary();
    renderTodayKeywords();
    renderPagination(0);
    return;
  }

  pageItems.forEach((issue) => {
    const card = document.createElement("article");
    card.className = "news-card";
    const imageUrl = getIssueImageUrl(issue);
    const keywords = issue.keywords.map((keyword) => `#${escapeHtml(keyword)}`).join(" ");
    card.innerHTML = `
      <div class="signal-art" aria-hidden="true"></div>
      <div>
        <div class="card-meta">
          <span class="badge">${escapeHtml(issue.category)}</span>
          <span>${escapeHtml(issue.source)}</span>
          <span>${formatIssueTime(issue)}</span>
        </div>
        <h3>${escapeHtml(issue.title)}</h3>
        <p class="safe-summary">${escapeHtml(compactText(issue.summary || "관련 기사가 묶인 이슈입니다. 자세한 내용은 원문에서 확인해 주세요.", 220))}</p>
        <details class="source-picker">
          <summary class="source-button" aria-label="관련 본문 링크 선택">본문 보기</summary>
          <div class="source-menu">
            ${renderRelatedLinks(issue)}
          </div>
        </details>
        <div class="card-bottom">
          <span class="impact">중요도 ${issue.impact}</span>
          <span>${keywords}</span>
        </div>
      </div>
    `;
    if (imageUrl) {
      const thumbnail = card.querySelector(".signal-art");
      thumbnail?.classList.add("has-image");
      thumbnail?.style.setProperty("--thumb-image", `url("${imageUrl}")`);
    }
    newsList.appendChild(card);
  });

  renderMetrics(visible);
  renderWeeklySummary();
  renderTodayKeywords();
  renderPagination(visible.length);
}
