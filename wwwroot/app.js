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
const topicCount = document.querySelector("#topicCount");
const todayCount = document.querySelector("#todayCount");
const impactScore = document.querySelector("#impactScore");
const updateTime = document.querySelector("#updateTime");
const metricGrid = document.querySelector(".metric-grid");
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

function compactText(value, maxLength = 260) {
  const text = String(value || "").replace(/\s+/g, " ").trim();
  if (text.length <= maxLength) return text;
  return `${text.slice(0, maxLength).replace(/[,\s.·-]+$/g, "")}...`;
}

function renderRelatedLinks(issue) {
  const links = (issue.relatedLinks || [])
    .map((link) => ({
      title: link.title || issue.title,
      source: link.source || issue.source,
      url: safeUrl(link.url),
      imageUrl: safeUrl(link.imageUrl),
      contentFetchStatus: link.contentFetchStatus || "",
      contentFetchError: link.contentFetchError || "",
    }))
    .filter((link) => link.url);

  if (!links.length) {
    return '<span class="source-empty">연결된 출처가 없습니다.</span>';
  }

  return links
    .map((link) => {
      return `
        <a class="source-link" href="${escapeHtml(link.url)}" target="_blank" rel="noopener noreferrer">
          <strong>${escapeHtml(link.source)}</strong>
          <span>${escapeHtml(link.title)}</span>
          ${link.contentFetchStatus === "failed" && link.contentFetchError ? `<em>${escapeHtml(link.contentFetchError)}</em>` : ""}
        </a>
      `;
    })
    .join("");
}

function getVisibleIssues() {
  const query = (searchInput?.value || "").trim().toLowerCase();
  return issues.filter((issue) => {
    const matchesFilter = activeFilter === "전체" || issue.category === activeFilter;
    const relatedText = (issue.relatedLinks || []).map((link) => `${link.title} ${link.source} ${link.contentPreview}`).join(" ");
    const text = `${issue.title} ${issue.category} ${issue.source} ${issue.summary} ${issue.contentPreview || ""} ${relatedText} ${issue.keywords.join(" ")}`.toLowerCase();
    return matchesFilter && (!query || text.includes(query));
  });
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
    renderPagination(0);
    return;
  }

  pageItems.forEach((issue) => {
    const card = document.createElement("article");
    card.className = "news-card";
    const detailText = compactText(issue.contentPreview || issue.summary, 900);
    const previewLabel = issue.contentPreviewLabel || (issue.contentPreviewSource === "rss" ? "RSS 요약 대체" : "원문 본문");
    const previewSource = issue.contentPreviewSource || "article";
    const imageUrl = safeUrl(issue.imageUrl) || safeUrl((issue.relatedLinks || []).find((link) => link.imageUrl)?.imageUrl);
    const keywords = issue.keywords.map((keyword) => `#${escapeHtml(keyword)}`).join(" ");
    card.innerHTML = `
      <details class="source-picker">
        <summary class="signal-art" aria-label="관련 본문 링크 선택">
          <span class="source-hint">본문 보기</span>
        </summary>
        <div class="source-menu">
          ${renderRelatedLinks(issue)}
        </div>
      </details>
      <div>
        <div class="card-meta">
          <span class="badge">${escapeHtml(issue.category)}</span>
          ${issue.heat === "hot" ? '<span class="hot-marker">HOT</span>' : ""}
          <span>${escapeHtml(issue.source)}</span>
          <span>${issue.minutes}분 전</span>
        </div>
        <h3>${escapeHtml(issue.title)}</h3>
        <details class="summary-expand">
          <summary>내용 보기</summary>
          <div class="summary-detail">
            <section>
              <span>대표 내용</span>
              <p>${escapeHtml(issue.summary || "요약 정보가 없습니다.")}</p>
            </section>
            ${detailText ? `
              <section>
                <span>${escapeHtml(previewLabel)}</span>
                <p>${escapeHtml(detailText)}</p>
              </section>
            ` : ""}
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
    ${categoryIssues.length ? renderDailyIssueList(categoryIssues) : ""}
  `;
}

function buildLocalCategorySummary(category, items) {
  if (!items.length) return "해당 카테고리에서 확인된 이슈가 없습니다.";
  const topIssue = [...items].sort((a, b) => b.impact - a.impact)[0];
  return `${category}에서 ${items.length.toLocaleString("ko-KR")}개 이슈가 확인됐습니다. 현재 가장 주목도가 높은 이슈는 ${topIssue.title}입니다.`;
}

function renderDailyIssueList(topIssues) {
  return `
    <ol class="daily-issue-list">
      ${topIssues
        .map((issue) => {
          return `
            <li>
              <strong>${escapeHtml(issue.title)}</strong>
              <span>${escapeHtml(issue.category)} · ${escapeHtml(issue.summary)}</span>
            </li>
          `;
        })
        .join("")}
    </ol>
  `;
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
    <ol class="weekly-issue-list">
      ${weeklyIssueItems
        .map((issue) => `<li><strong>${escapeHtml(issue.category)}</strong><span>${escapeHtml(issue.title)}</span></li>`)
        .join("")}
    </ol>
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
    <div>
      <span>선택 카테고리</span>
      <strong>${escapeHtml(category)}</strong>
    </div>
    <div>
      <span>${escapeHtml(weeklyLabel)} 이슈</span>
      <strong>${targetItems.length.toLocaleString("ko-KR")}</strong>
    </div>
    <div>
      <span>확인 출처</span>
      <strong>${sourceCount.toLocaleString("ko-KR")}</strong>
    </div>
    <div>
      <span>관련 기사</span>
      <strong>${articleCount.toLocaleString("ko-KR")}</strong>
    </div>
    <div>
      <span>중요도 평균</span>
      <strong>${averageImpact.toFixed(1)}</strong>
    </div>
    <div>
      <span>최상위 이슈</span>
      <strong>${escapeHtml(topIssue?.title || "-")}</strong>
    </div>
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
  navItems.forEach((item) => {
    item.classList.toggle("active", item.dataset.view === targetView);
  });
  viewPanels.forEach((panel) => {
    panel.classList.toggle("active", panel.dataset.panel === targetView);
  });
  metricGrid?.classList.toggle("hidden", targetView === "briefing");
}

navItems.forEach((item) => {
  item.addEventListener("click", () => {
    showView(item.dataset.view);
  });
});

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

document.querySelector("#refreshButton").addEventListener("click", () => {
  refreshFromServer();
});

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
  if (location.protocol !== "file:") {
    try {
      const response = await fetch("/api/refresh", { method: "POST" });
      if (!response.ok) throw new Error(`refresh ${response.status}`);
      await loadServerBriefs();
      await loadDailySummary();
      await loadWeeklySummary();
      currentPage = 1;
      renderCategoryFilters();
      renderNews();
      return;
    } catch (error) {
      console.warn(`[refresh] ${error.message}`);
    }
  }

  issues.unshift(issues.pop());
  currentPage = 1;
  renderCategoryFilters();
  renderNews();
}

searchInput?.addEventListener("input", () => {
  currentPage = 1;
  renderNews();
});
document.addEventListener("click", (event) => {
  document.querySelectorAll(".source-picker[open]").forEach((picker) => {
    if (!picker.contains(event.target)) picker.removeAttribute("open");
  });
});
loadServerBriefs().then(() => {
  showView("briefing");
  loadDailySummary();
  loadWeeklySummary();
  renderCategoryFilters();
  renderNews();
});
