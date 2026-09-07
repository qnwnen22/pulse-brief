function getIssueDate(issue) {
  const date = new Date(issue.latestPublishedAt || Date.now() - issue.minutes * 60000);
  return Number.isNaN(date.getTime()) ? new Date() : date;
}

function getKoreaDateKey(date) {
  const parts = koreaDateFormatter.formatToParts(date)
    .reduce((result, part) => {
      if (part.type !== "literal") result[part.type] = part.value;
      return result;
    }, {});
  return `${parts.year}-${parts.month}-${parts.day}`;
}

function getIssuesForDateKey(dateKey, sourceItems = issues) {
  if (!dateKey) return sourceItems;
  return sourceItems.filter((issue) => getKoreaDateKey(getIssueDate(issue)) === dateKey);
}

function parseWeeklySummaryRange(summary) {
  const match = /^weekly:(\d{4}-\d{2}-\d{2}):(\d{4}-\d{2}-\d{2})$/.exec(summary?.date || "");
  return match ? { start: match[1], end: match[2] } : null;
}

function getIssuesForDateRange(range, sourceItems = issues) {
  if (!range) return sourceItems;
  return sourceItems.filter((issue) => {
    const dateKey = getKoreaDateKey(getIssueDate(issue));
    return dateKey >= range.start && dateKey <= range.end;
  });
}

function getSummaryProviderLabel(summary) {
  if (!summary) return "로컬 요약";
  if (summary.provider === "openai") return `AI 요약 · ${summary.model || "OpenAI"}`;
  if (summary.provider === "manual") return `수동 요약${summary.model ? ` · ${summary.model}` : ""}`;
  return "로컬 요약";
}

function getCategoryIssues(category) {
  return category === "전체" ? issues : issues.filter((issue) => issue.category === category);
}

function renderCategorySummary() {
  if (!categorySummary) return;

  const selectedCategory = activeWeeklyCategory;
  if (!selectedCategory) {
    categorySummary.innerHTML = '<div class="empty-state">요약을 확인할 카테고리를 선택해 주세요.</div>';
    return;
  }

  const dailyItems = dailyBrief?.date ? getIssuesForDateKey(dailyBrief.date) : issues;
  const selectedIssues = selectedCategory === "전체"
    ? dailyItems
    : dailyItems.filter((issue) => issue.category === selectedCategory);
  const providerLabel = getSummaryProviderLabel(dailyBrief);
  const matchedCategory = (dailyBrief?.categories || []).find((category) => category.category === selectedCategory);
  const categoryIssues = (dailyBrief?.topIssues || []).filter((issue) => issue.category === selectedCategory).slice(0, 4);
  const title = `${selectedCategory} 전날 이슈 요약`;
  const summary = matchedCategory?.summary
    || buildLocalCategorySummary(selectedCategory, selectedIssues);

  if (!selectedIssues.length && !matchedCategory && !categoryIssues.length) {
    categorySummary.innerHTML = '<div class="empty-state">선택한 카테고리의 요약 정보가 없습니다.</div>';
    return;
  }

  categorySummary.innerHTML = `
    <div class="category-summary-top">
      <div>
        <span class="daily-provider">${escapeHtml(providerLabel)}</span>
        <h3>${escapeHtml(title)}</h3>
      </div>
      <span>${dailyBrief?.date ? `${escapeHtml(dailyBrief.date)} 기준` : "저장 데이터 기준"}</span>
    </div>
    <p>${escapeHtml(summary)}</p>
    ${categoryIssues.length ? renderDailyIssueList(categoryIssues, dailyItems) : ""}
  `;
}

function buildLocalCategorySummary(category, items) {
  if (!items.length) return "해당 카테고리에서 확인된 이슈가 없습니다.";
  const topIssue = [...items].sort((a, b) => b.impact - a.impact)[0];
  return `${category}에서 ${items.length.toLocaleString("ko-KR")}개 이슈가 확인됐습니다. 현재 가장 주목도가 높은 이슈는 ${topIssue.title}입니다.`;
}

function renderDailyIssueList(topIssues, targetItems) {
  return renderTrackedIssueList(topIssues, targetItems, {
    listClass: "daily-issue-list tracked-issue-list",
    pickerClass: "daily-source-picker weekly-source-picker",
    ariaLabel: "전날 주요 이슈 관련 기사 보기",
  });
}

function renderWeeklySummary() {
  const now = Date.now();
  const weekAgo = now - 7 * 24 * 60 * 60 * 1000;
  const weeklyRange = parseWeeklySummaryRange(weeklyBrief);
  const summaryRangeItems = weeklyRange ? getIssuesForDateRange(weeklyRange) : [];
  const recentItems = issues.filter((issue) => getIssueDate(issue).getTime() >= weekAgo);
  const baseItems = weeklyRange ? summaryRangeItems : (recentItems.length ? recentItems : issues);
  const dailyItems = dailyBrief?.date ? getIssuesForDateKey(dailyBrief.date) : [];
  // Saved summaries can outlive the bounded recent-news feed.
  const availableCategories = new Set([
    ...(dailyBrief?.categories || []),
    ...(dailyBrief?.topIssues || []),
    ...(weeklyBrief?.categories || []),
    ...(weeklyBrief?.topIssues || []),
    ...dailyItems,
    ...baseItems,
  ].map((item) => item.category).filter(Boolean));
  const categories = preferredCategories.filter((category) => availableCategories.has(category));
  const extraCategories = [...availableCategories]
    .filter((category) => !categories.includes(category))
    .sort((a, b) => a.localeCompare(b, "ko"));
  const allCategories = [...categories, ...extraCategories];

  if (!allCategories.length) {
    activeWeeklyCategory = "";
    weeklyCategoryTabs.innerHTML = "";
    weeklyStats.innerHTML = "";
    categorySummary.innerHTML = '<div class="empty-state">요약할 이슈 데이터가 없습니다.</div>';
    weeklySummary.innerHTML = '<div class="empty-state">주간 이슈 데이터가 없습니다.</div>';
    return;
  }

  if (!allCategories.includes(activeWeeklyCategory)) activeWeeklyCategory = allCategories[0];

  weeklyCategoryTabs.innerHTML = allCategories
    .map((category) => {
      const activeClass = category === activeWeeklyCategory ? " active" : "";
      return `<button class="weekly-tab${activeClass}" type="button" data-weekly-category="${escapeHtml(category)}">${escapeHtml(category)}</button>`;
    })
    .join("");

  const targetItems = baseItems.filter((issue) => issue.category === activeWeeklyCategory);
  const topIssues = [...targetItems]
    .sort((a, b) => {
      const impactDiff = b.impact - a.impact;
      if (impactDiff) return impactDiff;
      return getIssueDate(b) - getIssueDate(a);
    })
    .slice(0, 4);
  const weeklyLabel = weeklyRange ? `${weeklyRange.start}~${weeklyRange.end}` : (recentItems.length ? "최근 7일" : "저장 데이터 기준");
  const weeklyCategorySummary = (weeklyBrief?.categories || []).find((category) => category.category === activeWeeklyCategory);
  const savedWeeklyIssues = (weeklyBrief?.topIssues || []).filter((issue) => issue.category === activeWeeklyCategory);
  const weeklyIssueItems = savedWeeklyIssues.length ? savedWeeklyIssues.slice(0, 4) : topIssues;
  const sourceCount = new Set(savedWeeklyIssues.length
    ? savedWeeklyIssues.flatMap((issue) => issue.sources || [])
    : targetItems.flatMap(getSourceNames)).size;
  const weeklyProvider = getSummaryProviderLabel(weeklyBrief);
  const weeklyText = weeklyCategorySummary?.summary || buildWeeklyCategorySummary(activeWeeklyCategory, targetItems, weeklyLabel);

  if (!targetItems.length && !weeklyCategorySummary && !savedWeeklyIssues.length) {
    weeklyStats.innerHTML = "";
    weeklySummary.innerHTML = '<div class="empty-state">선택한 카테고리의 주간 이슈가 없습니다.</div>';
    renderCategorySummary();
    return;
  }

  renderWeeklyStats(activeWeeklyCategory, targetItems, sourceCount, weeklyLabel, {
    categorySummary: weeklyCategorySummary,
    topIssue: weeklyIssueItems[0],
    savedIssues: savedWeeklyIssues,
  });
  renderCategorySummary();
  weeklySummary.innerHTML = `
    <div class="weekly-summary-card">
      <span class="daily-provider">${escapeHtml(weeklyProvider)}</span>
      <h3>${escapeHtml(activeWeeklyCategory)} 주간 요약</h3>
      <p>${escapeHtml(weeklyText)}</p>
    </div>
    ${renderTrackedIssueList(weeklyIssueItems, baseItems, {
      listClass: "weekly-issue-list tracked-issue-list",
      pickerClass: "weekly-source-picker",
      ariaLabel: "주간 주요 이슈 관련 기사 보기",
    })}
  `;
}

function renderWeeklyStats(category, targetItems, sourceCount, weeklyLabel, summaryContext = {}) {
  if (!weeklyStats) return;

  const rawArticleCount = targetItems.reduce((sum, issue) => sum + (issue.articleCount || 1), 0);
  const rawTopIssue = [...targetItems].sort((a, b) => b.impact - a.impact)[0];
  const summaryIssueCount = Number(summaryContext.categorySummary?.issueCount || 0);
  const summaryArticleCount = Number(summaryContext.categorySummary?.articleCount || 0);
  const issueCount = summaryIssueCount || targetItems.length;
  const articleCount = summaryArticleCount || rawArticleCount;
  const topIssue = summaryContext.topIssue || rawTopIssue;
  const countBasis = summaryIssueCount ? `${weeklyLabel} 요약 기준` : "선택 카테고리 기준";
  const averageImpact = targetItems.length
    ? targetItems.reduce((sum, issue) => sum + issue.impact, 0) / targetItems.length
    : null;
  const hasSavedSources = summaryContext.savedIssues?.length > 0;

  weeklyStats.innerHTML = `
    ${renderMetricCard(
      "선택 카테고리",
      category,
      "요약 기준",
      "전날 이슈 요약과 주간 이슈 요약을 계산할 때 사용 중인 카테고리입니다.",
      "compact-value"
    )}
    ${renderMetricCard(
      "주간 이슈",
      issueCount.toLocaleString("ko-KR"),
      countBasis,
      "저장된 주간 요약이 있으면 요약에 포함된 대표 이슈 수를 보여줍니다. 뉴스 검색의 전체 이슈 수와는 집계 기준이 다를 수 있습니다."
    )}
    ${renderMetricCard(
      "확인 출처",
      hasSavedSources || targetItems.length ? sourceCount.toLocaleString("ko-KR") : "-",
      hasSavedSources ? "대표 이슈 출처 기준" : "중복 출처 제외",
      "저장된 대표 이슈의 출처를 중복 없이 표시합니다. 대표 이슈가 없으면 현재 조회된 같은 기간 기사 출처를 사용합니다."
    )}
    ${renderMetricCard(
      "관련 기사",
      articleCount.toLocaleString("ko-KR"),
      countBasis,
      "저장된 주간 요약이 있으면 요약에 포함된 대표 기사 수를 보여줍니다. 관련 기사 링크는 같은 기간의 원문 그룹에서 추적합니다."
    )}
    ${renderMetricCard(
      "중요도 평균",
      averageImpact === null ? "-" : averageImpact.toFixed(1),
      "선택 카테고리 평균",
      "관련 기사 수와 출처 수를 반영해 계산한 중요도 점수의 평균입니다. 값이 높을수록 여러 기사에서 반복적으로 확인된 흐름에 가깝습니다."
    )}
    ${renderMetricCard(
      "최상위 이슈",
      topIssue?.title || "-",
      "중요도 기준",
      "선택 카테고리에서 중요도 점수가 가장 높은 이슈입니다.",
      "long-value"
    )}
  `;
}

function renderMetricCard(label, value, description, helpText, valueClass = "") {
  const className = valueClass ? ` ${valueClass}` : "";
  return `
    <article class="metric">
      <div class="metric-label">
        <span>${escapeHtml(label)}</span>
        <span class="metric-help" tabindex="0" aria-label="${escapeHtml(label)} 도움말">
          <span class="metric-tooltip">${escapeHtml(helpText)}</span>
        </span>
      </div>
      <strong class="${className.trim()}">${escapeHtml(value)}</strong>
      <small>${escapeHtml(description)}</small>
    </article>
  `;
}

function buildWeeklyCategorySummary(category, items, weeklyLabel) {
  if (!items.length) return `${category} 카테고리의 주간 이슈가 없습니다.`;
  const topIssue = [...items].sort((a, b) => {
    const impactDiff = b.impact - a.impact;
    if (impactDiff) return impactDiff;
    return getIssueDate(b) - getIssueDate(a);
  })[0];
  const sourceCount = new Set(items.flatMap((issue) => (issue.source || "").split(", ").filter(Boolean))).size;
  return `${weeklyLabel} 동안 ${category} 카테고리에서는 ${items.length.toLocaleString("ko-KR")}개 이슈가 확인됐고, ${sourceCount.toLocaleString("ko-KR")}개 출처에서 관련 흐름이 포착됐습니다. 가장 주목도가 높은 흐름은 ${topIssue.title}입니다.`;
}
