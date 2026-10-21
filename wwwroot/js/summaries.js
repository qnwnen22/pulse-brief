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
  if (!match || !isDailySummaryDateKey(match[1]) || !isDailySummaryDateKey(match[2])) return null;
  const duration = Date.parse(`${match[2]}T00:00:00Z`) - Date.parse(`${match[1]}T00:00:00Z`);
  return duration === 6 * 86400000 ? { start: match[1], end: match[2] } : null;
}

function getIssuesForDateRange(range, sourceItems = issues) {
  if (!range) return sourceItems;
  return sourceItems.filter((issue) => {
    const dateKey = getKoreaDateKey(getIssueDate(issue));
    return dateKey >= range.start && dateKey <= range.end;
  });
}

function isDailySummaryDateKey(value) {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const date = new Date(`${value}T00:00:00Z`);
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value;
}

function formatDailySummaryDate(value) {
  if (!isDailySummaryDateKey(value)) return value || "";
  const [year, month, day] = value.split("-").map(Number);
  return `${year}년 ${month}월 ${day}일`;
}

function formatWeeklySummaryDate(value) {
  if (!isDailySummaryDateKey(value)) return value || "";
  const end = new Date(`${value}T00:00:00Z`);
  const start = new Date(end.getTime() - 6 * 86400000).toISOString().slice(0, 10);
  return `${start.replaceAll("-", ".")} ~ ${value.replaceAll("-", ".")}`;
}

function renderSummaryDateOptions(select, history, formatDate, label) {
  if (!select) return;

  if (!history.dates.length) {
    let message = "저장된 요약 없음";
    if (history.datesStatus === "loading") {
      message = `${label} 불러오는 중`;
    } else if (history.datesStatus === "error") {
      message = `${label} 목록 조회 실패`;
    }
    select.innerHTML = `<option value="">${message}</option>`;
    select.disabled = true;
    return;
  }

  select.innerHTML = history.dates
    .map((date, index) => {
      const suffix = index === 0 ? " (최신)" : "";
      return `<option value="${date}">${formatDate(date)}${suffix}</option>`;
    })
    .join("");
  select.value = history.selectedDate;
  select.disabled = history.status === "loading";
}

function renderDailySummaryDateOptions() {
  renderSummaryDateOptions(dailySummaryDateSelect, dailySummaryHistory, formatDailySummaryDate, "날짜");
}

function renderWeeklySummaryDateOptions() {
  renderSummaryDateOptions(weeklySummaryDateSelect, weeklySummaryHistory, formatWeeklySummaryDate, "기간");
}

function getRequestedSummaryDate(parameter) {
  if (location.protocol === "file:") return "";
  const value = new URLSearchParams(window.location.search).get(parameter) || "";
  return isDailySummaryDateKey(value) ? value : "";
}

function updateSummaryAddress(date, parameter) {
  if (location.protocol === "file:" || !isDailySummaryDateKey(date)) return;
  const url = new URL(window.location.href);
  url.searchParams.set(parameter, date);
  window.history.replaceState({}, "", url);
}

function getRequestedDailySummaryDate() {
  return getRequestedSummaryDate("summaryDate");
}

function getRequestedWeeklySummaryDate() {
  return getRequestedSummaryDate("weeklyEndDate");
}

function updateDailySummaryAddress(date) {
  updateSummaryAddress(date, "summaryDate");
}

function updateWeeklySummaryAddress(date) {
  updateSummaryAddress(date, "weeklyEndDate");
}

function summaryHistoryStatus(history, period, formatDate) {
  if (history.status === "loading") {
    const target = period === "일간" ? "날짜" : "기간";
    return `<div class="empty-state">선택한 ${target}의 ${period} 요약을 불러오는 중입니다.</div>`;
  }
  if (history.status === "missing") {
    const date = history.selectedDate ? formatDate(history.selectedDate) : "선택한 기간";
    return `<div class="empty-state">${date}에 저장된 ${period} 요약이 없습니다.</div>`;
  }
  if (history.status === "error") {
    return `<div class="empty-state">${period} 이슈 요약을 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.</div>`;
  }
  return "";
}

function getSummaryProviderLabel(summary) {
  if (!summary) return "로컬 요약";
  if (summary.provider === "manual" && summary.model === "\uB2F7\uCE20 \uD074\uB77C\uC6B0\uB4DC") return `AI \uC694\uC57D \u00B7 ${summary.model}`;
  if (summary.provider === "openai") return `AI 요약 · ${summary.model || "OpenAI"}`;
  if (summary.provider === "manual") return `수동 요약${summary.model ? ` · ${summary.model}` : ""}`;
  return "로컬 요약";
}

function getCategoryIssues(category) {
  return category === "전체" ? issues : issues.filter((issue) => issue.category === category);
}

function initializeSummaryTabs() {
  if (location.protocol !== "file:") {
    const query = new URLSearchParams(window.location.search);
    const period = query.get("summaryPeriod");
    if (["daily", "weekly"].includes(period)) {
      activeSummaryPeriod = period;
    } else if (!query.has("summaryDate") && getRequestedWeeklySummaryDate()) {
      activeSummaryPeriod = "weekly";
    }
  }
  renderSummaryPeriodState();
}

function selectSummaryPeriod(period) {
  if (!["daily", "weekly"].includes(period)) return;
  activeSummaryPeriod = period;
  if (location.protocol !== "file:") {
    const url = new URL(window.location.href);
    url.searchParams.set("summaryPeriod", period);
    window.history.replaceState({}, "", url);
  }
  renderSummaries();
}

function renderSummaryPeriodState() {
  summaryPeriodTabs?.querySelectorAll("[data-summary-period]").forEach(tab => {
    const selected = tab.dataset.summaryPeriod === activeSummaryPeriod;
    tab.setAttribute("aria-selected", String(selected));
    tab.tabIndex = selected ? 0 : -1;
  });
  for (const attribute of ["summary-panel", "summary-history"]) {
    document.querySelectorAll(`[data-${attribute}]`).forEach(element => {
      element.hidden = element.getAttribute(`data-${attribute}`) !== activeSummaryPeriod;
    });
  }
}

function getSummaryContext(period) {
  if (period === "daily") {
    return {
      period,
      periodName: "일간",
      summary: dailyBrief,
      history: dailySummaryHistory,
      items: dailyBrief?.date ? getIssuesForDateKey(dailyBrief.date) : issues
    };
  }

  const range = parseWeeklySummaryRange(weeklyBrief);
  const weekAgo = Date.now() - 7 * 86400000;
  const recentItems = issues.filter(issue => getIssueDate(issue).getTime() >= weekAgo);
  return {
    period,
    periodName: "주간",
    summary: weeklyBrief,
    history: weeklySummaryHistory,
    items: range ? getIssuesForDateRange(range) : (recentItems.length ? recentItems : issues)
  };
}

function renderSummaries() {
  renderSummaryPeriodState();
  const context = getSummaryContext(activeSummaryPeriod);
  const availableCategories = new Set([
    ...(context.summary?.categories || []),
    ...(context.summary?.topIssues || []),
    ...context.items
  ].map(item => item.category).filter(Boolean));
  const categories = preferredCategories.filter(category => availableCategories.has(category));
  const extraCategories = [...availableCategories]
    .filter(category => !categories.includes(category))
    .sort((first, second) => first.localeCompare(second, "ko"));
  const allCategories = [...categories, ...extraCategories];
  if (!allCategories.includes(selectedSummaryCategories[activeSummaryPeriod])) {
    selectedSummaryCategories[activeSummaryPeriod] = allCategories[0] || "";
  }

  const selectedCategory = selectedSummaryCategories[activeSummaryPeriod];
  summaryCategoryTabs.innerHTML = allCategories.map(category => {
    const selected = category === selectedCategory;
    return `<button class="summary-category-tab${selected ? " active" : ""}" type="button" data-summary-category="${escapeHtml(category)}" aria-pressed="${selected}">${escapeHtml(category)}</button>`;
  }).join("");
  renderSummaryStats(context, selectedCategory);

  if (activeSummaryPeriod === "daily") renderCategorySummary();
  else renderWeeklySummaryContent(context.items);
}

function renderCategorySummary() {
  if (!categorySummary) return;

  const status = summaryHistoryStatus(dailySummaryHistory, "일간", formatDailySummaryDate);
  if (status) {
    categorySummary.innerHTML = status;
    return;
  }

  const selectedCategory = selectedSummaryCategories.daily;
  if (!selectedCategory) {
    categorySummary.innerHTML = '<div class="empty-state">요약할 이슈 데이터가 없습니다.</div>';
    return;
  }

  const dailyItems = dailyBrief?.date ? getIssuesForDateKey(dailyBrief.date) : issues;
  const selectedIssues = selectedCategory === "전체"
    ? dailyItems
    : dailyItems.filter((issue) => issue.category === selectedCategory);
  const providerLabel = getSummaryProviderLabel(dailyBrief);
  const matchedCategory = (dailyBrief?.categories || []).find((category) => category.category === selectedCategory);
  const categoryIssues = (dailyBrief?.topIssues || []).filter((issue) => issue.category === selectedCategory).slice(0, 4);
  const title = `${selectedCategory} 일간 이슈 요약`;
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
    ariaLabel: "일간 주요 이슈 관련 기사 보기",
  });
}

function renderWeeklySummaryContent(baseItems) {
  const weeklyRange = parseWeeklySummaryRange(weeklyBrief);
  const selectedCategory = selectedSummaryCategories.weekly;
  const weeklyStatus = summaryHistoryStatus(weeklySummaryHistory, "주간", formatWeeklySummaryDate);
  if (weeklyStatus || !selectedCategory) {
    weeklySummary.innerHTML = weeklyStatus || '<div class="empty-state">주간 이슈 데이터가 없습니다.</div>';
    return;
  }

  const targetItems = baseItems.filter((issue) => issue.category === selectedCategory);
  const topIssues = [...targetItems]
    .sort((a, b) => {
      const impactDiff = b.impact - a.impact;
      if (impactDiff) return impactDiff;
      return getIssueDate(b) - getIssueDate(a);
    })
    .slice(0, 4);
  const weeklyLabel = weeklyRange ? `${weeklyRange.start}~${weeklyRange.end}` : "저장 데이터 기준";
  const weeklyCategorySummary = (weeklyBrief?.categories || []).find((category) => category.category === selectedCategory);
  const savedWeeklyIssues = (weeklyBrief?.topIssues || []).filter((issue) => issue.category === selectedCategory);
  const weeklyIssueItems = savedWeeklyIssues.length ? savedWeeklyIssues.slice(0, 4) : topIssues;
  const weeklyProvider = getSummaryProviderLabel(weeklyBrief);
  const weeklyText = weeklyCategorySummary?.summary || buildWeeklyCategorySummary(selectedCategory, targetItems, weeklyLabel);

  if (!targetItems.length && !weeklyCategorySummary && !savedWeeklyIssues.length) {
    weeklySummary.innerHTML = '<div class="empty-state">선택한 카테고리의 주간 이슈가 없습니다.</div>';
    return;
  }

  weeklySummary.innerHTML = `
    <div class="weekly-summary-card">
      <span class="daily-provider">${escapeHtml(weeklyProvider)}</span>
      <h3>${escapeHtml(selectedCategory)} 주간 요약</h3>
      <p>${escapeHtml(weeklyText)}</p>
    </div>
    ${renderTrackedIssueList(weeklyIssueItems, baseItems, {
      listClass: "weekly-issue-list tracked-issue-list",
      pickerClass: "weekly-source-picker",
      ariaLabel: "주간 주요 이슈 관련 기사 보기",
    })}
  `;
}

function renderSummaryStats(context, category) {
  const categorySummary = (context.summary?.categories || []).find(item => item.category === category);
  const savedIssues = (context.summary?.topIssues || []).filter(issue => issue.category === category);
  const targetItems = context.items.filter(issue => issue.category === category);
  const hasData = category && (categorySummary || savedIssues.length || targetItems.length);
  summaryStats.hidden = !hasData || ["loading", "missing", "error"].includes(context.history.status);
  if (summaryStats.hidden) {
    summaryStats.innerHTML = "";
    return;
  }

  const issueCount = categorySummary?.issueCount ?? targetItems.length;
  const articleCount = categorySummary?.articleCount
    ?? targetItems.reduce((total, issue) => total + (issue.articleCount || 1), 0);
  const sources = savedIssues.length
    ? savedIssues.flatMap(issue => issue.sources || [])
    : targetItems.flatMap(getSourceNames);
  const sourceCount = new Set(sources).size;
  summaryStats.innerHTML = `
    ${renderMetricCard(
      `${context.periodName} 이슈`,
      issueCount.toLocaleString("ko-KR"),
      category,
      `선택한 ${context.periodName} 요약에 기록된 카테고리별 이슈 수입니다. 뉴스 검색의 현재 이슈 수와 집계 기준이 다릅니다.`
    )}
    ${renderMetricCard(
      "관련 기사",
      articleCount.toLocaleString("ko-KR"),
      category,
      `선택한 ${context.periodName} 요약에 기록된 카테고리별 기사 수입니다.`
    )}
    ${renderMetricCard(
      "확인 언론사",
      sourceCount.toLocaleString("ko-KR"),
      "대표 이슈 기준",
      "대표 이슈에 포함된 언론사의 중복을 제외한 수입니다. 카테고리 전체 기사에 포함된 언론사 수와는 다를 수 있습니다."
    )}
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
