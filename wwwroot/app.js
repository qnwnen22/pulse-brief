const sampleIssues = [
  {
    title: "반도체 공급망 투자 경쟁이 다시 가속",
    category: "경제",
    source: "Market Daily",
    minutes: 8,
    impact: 92,
    heat: "hot",
    summary: "미국과 아시아 주요 기업의 설비 투자 발표가 이어지며 장비·소재 섹터까지 기대감이 확산되고 있습니다.",
    keywords: ["반도체", "공급망", "설비투자", "소재"],
  },
  {
    title: "생성형 AI 검색 서비스, 뉴스 유통 구조 흔든다",
    category: "기술",
    source: "Tech Signal",
    minutes: 13,
    impact: 88,
    heat: "hot",
    summary: "검색 결과가 링크 목록에서 답변형 요약으로 이동하면서 언론사와 플랫폼의 트래픽 배분 논의가 커지고 있습니다.",
    keywords: ["AI검색", "뉴스", "플랫폼", "저작권"],
  },
  {
    title: "폭염 대비 전력 수급 점검 이슈 부상",
    category: "사회",
    source: "Civic Wire",
    minutes: 21,
    impact: 76,
    heat: "normal",
    summary: "냉방 수요 증가가 예상되면서 지역별 전력 예비율, 취약계층 지원, 공공시설 냉방 운영이 함께 주목받고 있습니다.",
    keywords: ["폭염", "전력", "에너지", "안전"],
  },
  {
    title: "OTT 신작 공개 후 원작 IP 검색량 급등",
    category: "문화",
    source: "Culture Beat",
    minutes: 28,
    impact: 69,
    heat: "normal",
    summary: "시리즈 공개 직후 원작 웹툰과 배우 인터뷰 검색량이 동시에 뛰며 2차 콘텐츠 소비가 빠르게 늘고 있습니다.",
    keywords: ["OTT", "웹툰", "IP", "인터뷰"],
  },
  {
    title: "환율 변동성 확대에 수입 물가 우려",
    category: "경제",
    source: "Finance Now",
    minutes: 36,
    impact: 81,
    heat: "hot",
    summary: "달러 강세와 원자재 가격 흐름이 겹치며 기업 비용과 소비자 물가에 미칠 영향이 주요 관심사로 떠올랐습니다.",
    keywords: ["환율", "물가", "원자재", "수입"],
  },
  {
    title: "모바일 보안 업데이트 권고 확산",
    category: "기술",
    source: "Security Desk",
    minutes: 47,
    impact: 73,
    heat: "normal",
    summary: "주요 제조사가 긴급 패치를 배포하면서 피싱 문자, 악성 앱 권한, 업무용 단말 관리가 함께 언급되고 있습니다.",
    keywords: ["보안", "패치", "모바일", "피싱"],
  },
];

let issues = [...sampleIssues];
let dailyBrief = null;
let weeklyBrief = null;
let activeFilter = "전체";
let currentPage = 1;
let activeWeeklyCategory = "전체";
const pageSize = 10;

const newsList = document.querySelector("#newsList");
const searchInput = document.querySelector("#searchInput");
const dateFilter = document.querySelector("#dateFilter");
const sourceFilter = document.querySelector("#sourceFilter");
const articleCountFilter = document.querySelector("#articleCountFilter");
const sortSelect = document.querySelector("#sortSelect");
const hotOnlyFilter = document.querySelector("#hotOnlyFilter");
const resetFiltersButton = document.querySelector("#resetFiltersButton");
const todayKeywords = document.querySelector("#todayKeywords");
const topicCount = document.querySelector("#topicCount");
const todayCount = document.querySelector("#todayCount");
const impactScore = document.querySelector("#impactScore");
const updateTime = document.querySelector("#updateTime");
const newsMetricGrid = document.querySelector("#newsMetricGrid");
const menuEyebrow = document.querySelector("#menuEyebrow");
const menuTitle = document.querySelector("#menuTitle");
const categoryFilters = document.querySelector("#categoryFilters");
const paginationControls = document.querySelector("#paginationControls");
const topPaginationControls = document.querySelector("#topPaginationControls");
const paginationContainers = [topPaginationControls, paginationControls].filter(Boolean);
const categorySummary = document.querySelector("#categorySummary");
const weeklySummary = document.querySelector("#weeklySummary");
const weeklyCategoryTabs = document.querySelector("#weeklyCategoryTabs");
const weeklyStats = document.querySelector("#weeklyStats");
const navItems = document.querySelectorAll(".nav-item[data-view]");
const viewPanels = document.querySelectorAll(".view-panel[data-panel]");
const appLoading = document.querySelector("#appLoading");
const refreshButton = document.querySelector("#refreshButton");

const preferredCategories = [
  "정치/정책",
  "경제/산업",
  "사회",
  "국제",
  "IT/과학",
  "문화/연예",
  "스포츠",
  "생활/건강",
  "지역",
];

const viewTitles = {
  briefing: {
    eyebrow: "Briefing",
    title: "카테고리별 이슈 흐름 요약",
  },
  feed: {
    eyebrow: "News Search",
    title: "뉴스 검색과 원문 출처 확인",
  },
  notice: {
    eyebrow: "Service Notice",
    title: "서비스 고지와 운영 기준",
  },
};

function escapeHtml(value) {
  return String(value || "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function safeUrl(value) {
  try {
    const url = new URL(value);
    return ["http:", "https:"].includes(url.protocol) ? url.href : "";
  } catch {
    return "";
  }
}

function sourceNameFromUrl(value) {
  try {
    const host = new URL(value).hostname.replace(/^www\./, "").toLowerCase();
    if (host.includes("gonggam.korea.kr")) return "공감";
    if (host.includes("newsis.com")) return "뉴시스";
    if (host.includes("news.sbs.co.kr")) return "SBS 뉴스";
    if (host.includes("imbc.com")) return "MBC 뉴스";
    if (host.includes("news.jtbc.co.kr")) return "JTBC 뉴스";
    if (host.includes("korea.kr")) return "정책브리핑";
    if (host.includes("etnews.com")) return "전자신문";
    if (host.includes("yna.co.kr")) return "연합뉴스";
    if (host.includes("hani.co.kr")) return "한겨레";
    if (host.includes("bbc.com")) return "BBC";
    return host.split(".")[0].toUpperCase();
  } catch {
    return "";
  }
}

function displaySourceName(source, url) {
  const sourceName = String(source || "").trim();
  const publisher = sourceNameFromUrl(url);
  const genericSources = [
    "포토",
    "속보",
    "전체",
    "뉴스",
    "사회",
    "경제",
    "정치",
    "국제",
    "문화",
    "스포츠",
    "산업",
    "금융",
    "광장",
    "IT·바이오",
  ];

  if (!publisher || !sourceName) return sourceName || publisher || "출처";
  if (sourceName.includes(publisher) || publisher.includes(sourceName)) return sourceName;
  if (genericSources.includes(sourceName)) return `${publisher} · ${sourceName}`;
  return sourceName;
}

function compactText(value, maxLength = 260) {
  const text = String(value || "").replace(/\s+/g, " ").trim();
  if (text.length <= maxLength) return text;
  return `${text.slice(0, maxLength).replace(/[,\s.·-]+$/g, "")}...`;
}

function formatIssueTime(issue) {
  const date = getIssueDate(issue);
  const minutes = Math.max(1, Math.round((Date.now() - date.getTime()) / 60000));
  if (minutes < 60) return `${minutes}분 전`;
  if (minutes < 1440) return `${Math.floor(minutes / 60)}시간 전`;
  return date.toLocaleDateString("ko-KR", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  });
}

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

function findTrackedIssue(summaryIssue, candidates) {
  const articleIds = new Set((summaryIssue.articleIds || []).filter(Boolean));
  if (articleIds.size) {
    const byArticleId = candidates.find((candidate) =>
      (candidate.articleIds || []).some((articleId) => articleIds.has(articleId))
    );
    if (byArticleId) return byArticleId;
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
  const trackedIssue = findTrackedIssue(issue, targetItems);
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

function getSourceCount(issue) {
  return getSourceNames(issue).length || (issue.source ? 1 : 0);
}

function getIssueImageUrl(issue) {
  return safeUrl(issue.imageUrl) || safeUrl((issue.relatedLinks || []).find((link) => link.imageUrl)?.imageUrl);
}

function matchesDateFilter(issue) {
  const value = dateFilter?.value || "all";
  if (value === "all") return true;

  const issueDay = startOfDay(getIssueDate(issue)).getTime();
  const today = startOfDay(new Date()).getTime();
  const oneDay = 24 * 60 * 60 * 1000;

  if (value === "today") return issueDay === today;
  if (value === "yesterday") return issueDay === today - oneDay;
  if (value === "week") return getIssueDate(issue).getTime() >= Date.now() - 7 * oneDay;
  return true;
}

function matchesSourceFilter(issue) {
  const selectedSource = sourceFilter?.value || "all";
  if (selectedSource === "all") return true;
  return getSourceNames(issue).includes(selectedSource);
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
  const sourceDiff = getSourceCount(b) - getSourceCount(a);

  if (sortValue === "oldest") return getIssueDate(a) - getIssueDate(b);
  if (sortValue === "impactDesc") return impactDiff || dateDiff;
  if (sortValue === "impactAsc") return -impactDiff || dateDiff;
  if (sortValue === "articleCountDesc") return articleDiff || impactDiff || dateDiff;
  if (sortValue === "articleCountAsc") return -articleDiff || dateDiff;
  if (sortValue === "sourceCountDesc") return sourceDiff || articleDiff || dateDiff;
  if (sortValue === "sourceCountAsc") return -sourceDiff || dateDiff;
  if (sortValue === "titleAsc") return String(a.title || "").localeCompare(String(b.title || ""), "ko") || dateDiff;
  return dateDiff || impactDiff;
}

function getVisibleIssues() {
  const query = (searchInput?.value || "").trim().toLowerCase();
  return issues.filter((issue) => {
    const matchesFilter = activeFilter === "전체" || issue.category === activeFilter;
    const relatedText = (issue.relatedLinks || []).map((link) => `${link.title} ${link.source}`).join(" ");
    const text = `${issue.title} ${issue.category} ${issue.source} ${issue.summary} ${relatedText} ${issue.keywords.join(" ")}`.toLowerCase();
    return matchesFilter
      && (!query || text.includes(query))
      && matchesDateFilter(issue)
      && matchesSourceFilter(issue)
      && matchesArticleCountFilter(issue)
      && (!hotOnlyFilter?.checked || issue.heat === "hot");
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
          ${issue.heat === "hot" ? '<span class="hot-marker">HOT</span>' : ""}
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

function getIssueDate(issue) {
  const date = new Date(issue.latestPublishedAt || Date.now() - issue.minutes * 60000);
  return Number.isNaN(date.getTime()) ? new Date() : date;
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

  const selectedIssues = getCategoryIssues(selectedCategory);
  const providerLabel = dailyBrief?.provider === "openai" ? `AI 요약 · ${dailyBrief.model || "OpenAI"}` : "로컬 요약";
  const matchedCategory = (dailyBrief?.categories || []).find((category) => category.category === selectedCategory);
  const categoryIssues = (dailyBrief?.topIssues || []).filter((issue) => issue.category === selectedCategory).slice(0, 4);
  const title = `${selectedCategory} 전날 이슈 요약`;
  const summary = matchedCategory?.summary
    || buildLocalCategorySummary(selectedCategory, selectedIssues);

  if (!selectedIssues.length && !matchedCategory) {
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
    ${categoryIssues.length ? renderDailyIssueList(categoryIssues, selectedIssues) : ""}
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
  const weeklyItems = issues.filter((issue) => getIssueDate(issue).getTime() >= weekAgo);
  const baseItems = weeklyItems.length ? weeklyItems : issues;
  const categories = preferredCategories.filter((category) => baseItems.some((issue) => issue.category === category));
  const extraCategories = [...new Set(baseItems.map((issue) => issue.category))]
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
  const sourceCount = new Set(targetItems.flatMap((issue) => (issue.source || "").split(", ").filter(Boolean))).size;
  const weeklyLabel = weeklyItems.length ? "최근 7일" : "저장 데이터 기준";
  const weeklyCategorySummary = (weeklyBrief?.categories || []).find((category) => category.category === activeWeeklyCategory);
  const aiWeeklyIssues = (weeklyBrief?.topIssues || []).filter((issue) => issue.category === activeWeeklyCategory).slice(0, 4);
  const weeklyIssueItems = aiWeeklyIssues.length ? aiWeeklyIssues : topIssues;
  const weeklyProvider = weeklyBrief?.provider === "openai" ? `AI 요약 · ${weeklyBrief.model || "OpenAI"}` : "로컬 요약";
  const weeklyText = weeklyCategorySummary?.summary || buildWeeklyCategorySummary(activeWeeklyCategory, targetItems, weeklyLabel);

  if (!targetItems.length) {
    weeklyStats.innerHTML = "";
    weeklySummary.innerHTML = '<div class="empty-state">선택한 카테고리의 주간 이슈가 없습니다.</div>';
    renderCategorySummary();
    return;
  }

  renderWeeklyStats(activeWeeklyCategory, targetItems, sourceCount, weeklyLabel);
  renderCategorySummary();
  weeklySummary.innerHTML = `
    <div class="weekly-summary-card">
      <span class="daily-provider">${escapeHtml(weeklyProvider)}</span>
      <h3>${escapeHtml(activeWeeklyCategory)} 주간 요약</h3>
      <p>${escapeHtml(weeklyText)}</p>
    </div>
    ${renderTrackedIssueList(weeklyIssueItems, targetItems, {
      listClass: "weekly-issue-list tracked-issue-list",
      pickerClass: "weekly-source-picker",
      ariaLabel: "주간 주요 이슈 관련 기사 보기",
    })}
  `;
}

function renderWeeklyStats(category, targetItems, sourceCount, weeklyLabel) {
  if (!weeklyStats) return;

  const articleCount = targetItems.reduce((sum, issue) => sum + (issue.articleCount || 1), 0);
  const topIssue = [...targetItems].sort((a, b) => b.impact - a.impact)[0];
  const averageImpact = targetItems.length
    ? targetItems.reduce((sum, issue) => sum + issue.impact, 0) / targetItems.length
    : 0;

  weeklyStats.innerHTML = `
    ${renderMetricCard(
      "선택 카테고리",
      category,
      "요약 기준",
      "전날 이슈 요약과 주간 이슈 요약을 계산할 때 사용 중인 카테고리입니다.",
      "compact-value"
    )}
    ${renderMetricCard(
      `${weeklyLabel} 이슈`,
      targetItems.length.toLocaleString("ko-KR"),
      "선택 카테고리 기준",
      "최근 7일 데이터가 있으면 최근 7일 이슈 묶음 수를, 없으면 저장된 데이터 전체 기준 이슈 묶음 수를 보여줍니다."
    )}
    ${renderMetricCard(
      "확인 출처",
      sourceCount.toLocaleString("ko-KR"),
      "중복 출처 제외",
      "선택 카테고리의 이슈를 구성하는 기사 출처를 중복 없이 계산한 값입니다."
    )}
    ${renderMetricCard(
      "관련 기사",
      articleCount.toLocaleString("ko-KR"),
      "그룹에 포함된 기사",
      "선택 카테고리의 이슈 묶음 안에 포함된 개별 기사 수의 합계입니다."
    )}
    ${renderMetricCard(
      "중요도 평균",
      averageImpact.toFixed(1),
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

function renderSourceFilter() {
  if (!sourceFilter) return;

  const currentValue = sourceFilter.value || "all";
  const sourceNames = [...new Set(issues.flatMap(getSourceNames))]
    .sort((a, b) => a.localeCompare(b, "ko"));

  sourceFilter.innerHTML = [
    '<option value="all">전체 출처</option>',
    ...sourceNames.map((source) => `<option value="${escapeHtml(source)}">${escapeHtml(source)}</option>`),
  ].join("");
  sourceFilter.value = sourceNames.includes(currentValue) ? currentValue : "all";
}

function renderTodayKeywords() {
  if (!todayKeywords) return;

  const todayKey = startOfDay(new Date()).getTime();
  const keywordStats = new Map();
  const countedIssueKeys = new Set();
  issues
    .filter((issue) => getIssueDate(issue).toDateString() === new Date(todayKey).toDateString())
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
  const todayKey = new Date().toDateString();
  const todayItems = categoryItems.filter((issue) => getIssueDate(issue).toDateString() === todayKey);
  const totalImpact = todayItems.reduce((sum, issue) => sum + issue.impact, 0);
  const average = todayItems.length ? totalImpact / todayItems.length : 0;
  const now = new Date();

  topicCount.textContent = categoryItems.length.toLocaleString("ko-KR");
  todayCount.textContent = todayItems.length.toLocaleString("ko-KR");
  impactScore.textContent = average.toFixed(1);
  updateTime.textContent = now.toLocaleTimeString("ko-KR", {
    hour: "2-digit",
    minute: "2-digit",
  });
}

function showView(view) {
  const targetView = [...viewPanels].some((panel) => panel.dataset.panel === view) ? view : "briefing";
  const title = viewTitles[targetView] || viewTitles.briefing;
  navItems.forEach((item) => {
    item.classList.toggle("active", item.dataset.view === targetView);
  });
  viewPanels.forEach((panel) => {
    panel.classList.toggle("active", panel.dataset.panel === targetView);
  });
  newsMetricGrid?.classList.toggle("hidden", targetView !== "feed");
  if (menuEyebrow) menuEyebrow.textContent = title.eyebrow;
  if (menuTitle) menuTitle.textContent = title.title;
}

navItems.forEach((item) => {
  item.addEventListener("click", () => {
    showView(item.dataset.view);
  });
});

document.querySelectorAll("[data-view].footer-link").forEach((item) => {
  item.addEventListener("click", () => {
    showView(item.dataset.view);
    window.scrollTo({ top: 0, behavior: "smooth" });
  });
});

function resetSearchFilters() {
  if (searchInput) searchInput.value = "";
  if (dateFilter) dateFilter.value = "all";
  if (sourceFilter) sourceFilter.value = "all";
  if (articleCountFilter) articleCountFilter.value = "all";
  if (sortSelect) sortSelect.value = "latest";
  if (hotOnlyFilter) hotOnlyFilter.checked = false;
  currentPage = 1;
  renderNews();
}

categoryFilters.addEventListener("click", (event) => {
  const button = event.target.closest(".segment");
  if (!button) return;
  activeFilter = button.dataset.filter;
  currentPage = 1;
  renderCategoryFilters();
  renderNews();
});

paginationContainers.forEach((container) => {
  container.addEventListener("click", (event) => {
    const button = event.target.closest(".page-button");
    if (!button || button.disabled) return;
    currentPage = Number(button.dataset.page);
    renderNews();
    newsList.scrollIntoView({ block: "start", behavior: "smooth" });
  });
});

weeklyCategoryTabs.addEventListener("click", (event) => {
  const button = event.target.closest(".weekly-tab");
  if (!button) return;
  activeWeeklyCategory = button.dataset.weeklyCategory;
  renderWeeklySummary();
});

refreshButton?.addEventListener("click", () => {
  refreshFromServer();
});

function setRefreshButtonBusy(isBusy) {
  if (!refreshButton) return;
  refreshButton.disabled = isBusy;
  refreshButton.setAttribute("aria-busy", String(isBusy));
  refreshButton.classList.toggle("is-loading", isBusy);
}

async function loadServerBriefs() {
  if (location.protocol === "file:") return false;

  try {
    const response = await fetch("/api/briefs");
    if (!response.ok) throw new Error(`briefs ${response.status}`);
    const serverIssues = await response.json();
    if (!Array.isArray(serverIssues) || !serverIssues.length) return false;
    issues = serverIssues;
    return true;
  } catch (error) {
    console.warn(`[briefs] ${error.message}`);
    return false;
  }
}

async function loadDailySummary() {
  if (location.protocol === "file:") return false;

  try {
    const response = await fetch("/api/daily-summary");
    if (!response.ok) throw new Error(`daily-summary ${response.status}`);
    dailyBrief = await response.json();
    renderWeeklySummary();
    return true;
  } catch (error) {
    console.warn(`[daily-summary] ${error.message}`);
    renderWeeklySummary();
    return false;
  }
}

async function loadWeeklySummary() {
  if (location.protocol === "file:") return false;

  try {
    const response = await fetch("/api/weekly-summary");
    if (!response.ok) throw new Error(`weekly-summary ${response.status}`);
    weeklyBrief = await response.json();
    renderWeeklySummary();
    return true;
  } catch (error) {
    console.warn(`[weekly-summary] ${error.message}`);
    renderWeeklySummary();
    return false;
  }
}

async function refreshFromServer() {
  if (refreshButton?.disabled) return;

  setRefreshButtonBusy(true);
  try {
    if (location.protocol !== "file:") {
      const loaded = await loadServerBriefs();
      if (!loaded) console.warn("[refresh] failed to reload briefs");
      await Promise.all([loadDailySummary(), loadWeeklySummary()]);
    } else {
      issues.unshift(issues.pop());
    }

    currentPage = 1;
    renderSourceFilter();
    renderCategoryFilters();
    renderNews();
  } catch (error) {
    console.warn(`[refresh] ${error.message}`);
  } finally {
    setRefreshButtonBusy(false);
  }
}

searchInput?.addEventListener("input", () => {
  currentPage = 1;
  renderNews();
});

[dateFilter, sourceFilter, articleCountFilter, sortSelect, hotOnlyFilter]
  .filter(Boolean)
  .forEach((control) => {
    control.addEventListener("change", () => {
      currentPage = 1;
      renderNews();
    });
  });

resetFiltersButton?.addEventListener("click", resetSearchFilters);

todayKeywords?.addEventListener("click", (event) => {
  const button = event.target.closest("button[data-keyword]");
  if (!button || !searchInput) return;
  searchInput.value = button.dataset.keyword || "";
  currentPage = 1;
  renderNews();
});
document.addEventListener("click", (event) => {
  document.querySelectorAll(".source-picker[open]").forEach((picker) => {
    if (!picker.contains(event.target)) picker.removeAttribute("open");
  });
  document.querySelectorAll(".weekly-source-picker[open]").forEach((picker) => {
    if (!picker.contains(event.target)) picker.removeAttribute("open");
  });
});

function hideAppLoading() {
  document.body.classList.remove("loading-active");
  if (!appLoading) return;
  appLoading.classList.add("done");
  window.setTimeout(() => {
    appLoading.setAttribute("hidden", "");
  }, 280);
}

async function initializeApp() {
  document.body.classList.add("loading-active");

  try {
    await loadServerBriefs();
    showView("briefing");
    renderSourceFilter();
    renderCategoryFilters();
    renderNews();
    Promise.all([loadDailySummary(), loadWeeklySummary()]).catch((error) => {
      console.warn(`[summary-load] ${error.message}`);
    });
  } finally {
    hideAppLoading();
  }
}

initializeApp();
