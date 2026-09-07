function renderPagination(totalItems) {
  if (!totalItems || totalItems <= pageSize) {
    paginationContainers.forEach((container) => {
      container.innerHTML = "";
    });
    return;
  }

  const pageCount = Math.ceil(totalItems / pageSize);
  const startItem = (currentPage - 1) * pageSize + 1;
  const endItem = Math.min(currentPage * pageSize, totalItems);
  const pages = [];
  const firstPage = Math.max(1, currentPage - 2);
  const lastPage = Math.min(pageCount, currentPage + 2);

  for (let page = firstPage; page <= lastPage; page += 1) {
    pages.push(page);
  }

  const paginationHtml = `
    <div class="pagination-summary">${startItem}-${endItem} / ${totalItems}</div>
    <div class="pagination-buttons">
      <button class="page-button" type="button" data-page="1" ${currentPage === 1 ? "disabled" : ""}>처음</button>
      <button class="page-button" type="button" data-page="${currentPage - 1}" ${currentPage === 1 ? "disabled" : ""}>이전</button>
      ${pages
        .map((page) => {
          const activeClass = page === currentPage ? " active" : "";
          return `<button class="page-button${activeClass}" type="button" data-page="${page}">${page}</button>`;
        })
        .join("")}
      <button class="page-button" type="button" data-page="${currentPage + 1}" ${currentPage === pageCount ? "disabled" : ""}>다음</button>
      <button class="page-button" type="button" data-page="${pageCount}" ${currentPage === pageCount ? "disabled" : ""}>끝</button>
    </div>
  `;

  paginationContainers.forEach((container) => {
    container.innerHTML = paginationHtml;
  });
}

function renderCategoryFilters() {
  const existingCategories = [...new Set(issues.map((issue) => issue.category).filter(Boolean))];
  const orderedCategories = preferredCategories.filter((category) => existingCategories.includes(category));
  const extraCategories = existingCategories
    .filter((category) => !preferredCategories.includes(category))
    .sort((a, b) => a.localeCompare(b, "ko"));
  const categories = ["전체", ...orderedCategories, ...extraCategories];

  if (!categories.includes(activeFilter)) activeFilter = "전체";

  categoryFilters.innerHTML = categories
    .map((category) => {
      const activeClass = category === activeFilter ? " active" : "";
      return `<button class="segment${activeClass}" type="button" data-filter="${escapeHtml(category)}">${escapeHtml(category)}</button>`;
    })
    .join("");
}

function renderPublisherFilter() {
  if (!publisherFilter) return;

  const currentValue = publisherFilter.value || "all";
  const publisherNames = [...new Set(issues.flatMap(getPublisherNames))]
    .sort((a, b) => a.localeCompare(b, "ko"));

  publisherFilter.innerHTML = [
    '<option value="all">전체 언론사</option>',
    ...publisherNames.map((publisher) => `<option value="${escapeHtml(publisher)}">${escapeHtml(publisher)}</option>`),
  ].join("");
  publisherFilter.value = publisherNames.includes(currentValue) ? currentValue : "all";
}

function renderTodayKeywords() {
  if (!todayKeywords) return;

  const todayKey = getKoreaDateKey(new Date());
  const keywordStats = new Map();
  const countedIssueKeys = new Set();
  issues
    .filter((issue) => getKoreaDateKey(getIssueDate(issue)) === todayKey)
    .forEach((issue) => {
      const issueKey = `${issue.source || ""}|${normalizeTitle(issue.title)}`;
      if (countedIssueKeys.has(issueKey)) return;
      countedIssueKeys.add(issueKey);

      const issueSources = getSourceNames(issue);
      const uniqueKeywords = new Set((issue.keywords || [])
        .map((keyword) => String(keyword || "").replace(/^#/, "").trim())
        .filter(Boolean));

      uniqueKeywords.forEach((keyword) => {
        const cleaned = String(keyword || "").replace(/^#/, "").trim();
        if (!cleaned) return;

        const stats = keywordStats.get(cleaned) || { count: 0, sources: new Set() };
        stats.count += 1;
        issueSources.forEach((source) => stats.sources.add(source));
        keywordStats.set(cleaned, stats);
      });
    });

  const keywordItems = [...keywordStats.entries()]
    .map(([keyword, stats]) => [keyword, stats.count, stats.sources.size])
    .filter(([, count, sourceCount]) => count >= 5 && sourceCount >= 3)
    .sort((a, b) => b[1] - a[1] || b[2] - a[2] || a[0].localeCompare(b[0], "ko"))
    .slice(0, 16);

  if (!keywordItems.length) {
    todayKeywords.innerHTML = "";
    todayKeywords.classList.add("hidden");
    return;
  }

  todayKeywords.classList.remove("hidden");
  todayKeywords.innerHTML = `
    <div>
      <strong>금일 주요 키워드</strong>
      <span>오늘 5회 이상, 3개 이상 출처에서 확인된 키워드</span>
    </div>
    <div class="keyword-list">
      ${keywordItems
        .map(([keyword, count, sourceCount]) => `<button type="button" data-keyword="${escapeHtml(keyword)}">#${escapeHtml(keyword)} <span>${count}회 · ${sourceCount}출처</span></button>`)
        .join("")}
    </div>
  `;
}

function renderMetrics() {
  const categoryItems = getCategoryIssues(activeFilter);
  const todayKey = getKoreaDateKey(new Date());
  const todayItems = categoryItems.filter((issue) => getKoreaDateKey(getIssueDate(issue)) === todayKey);
  const statsCount = Number(newsStats?.todayArticleCount);
  const hasCurrentStats = newsStats?.isReady !== false
    && newsStats?.todayDate === todayKey
    && Number.isFinite(statsCount);
  const totalImpact = todayItems.reduce((sum, issue) => sum + issue.impact, 0);
  const average = todayItems.length ? totalImpact / todayItems.length : 0;
  const statsUpdatedAt = newsStats?.updatedAt ? new Date(newsStats.updatedAt) : null;
  const updatedAt = statsUpdatedAt && !Number.isNaN(statsUpdatedAt.getTime()) ? statsUpdatedAt : new Date();

  todayCount.textContent = hasCurrentStats
    ? statsCount.toLocaleString("ko-KR")
    : (location.protocol === "file:" ? todayItems.length.toLocaleString("ko-KR") : "-");
  impactScore.textContent = average.toFixed(1);
  updateTime.textContent = updatedAt.toLocaleTimeString("ko-KR", {
    hour: "2-digit",
    minute: "2-digit",
  });
}
