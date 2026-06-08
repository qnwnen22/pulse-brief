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
const trendPoints = [18, 26, 24, 38, 52, 48, 61, 74, 70, 86, 91, 96];
let activeFilter = "전체";
let currentPage = 1;
let activeWeeklyCategory = "전체";
const pageSize = 50;

const newsList = document.querySelector("#newsList");
const searchInput = document.querySelector("#searchInput");
const topicCount = document.querySelector("#topicCount");
const impactScore = document.querySelector("#impactScore");
const updateTime = document.querySelector("#updateTime");
const keywordCloud = document.querySelector("#keywordCloud");
const trendCanvas = document.querySelector("#trendCanvas");
const categoryFilters = document.querySelector("#categoryFilters");
const paginationControls = document.querySelector("#paginationControls");
const weeklySummary = document.querySelector("#weeklySummary");
const weeklyCategoryTabs = document.querySelector("#weeklyCategoryTabs");

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
  "영상/포토",
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

function renderRelatedLinks(issue) {
  const links = (issue.relatedLinks || [])
    .map((link) => ({
      title: link.title || issue.title,
      source: link.source || issue.source,
      url: safeUrl(link.url),
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
        </a>
      `;
    })
    .join("");
}

function getVisibleIssues() {
  const query = searchInput.value.trim().toLowerCase();
  return issues.filter((issue) => {
    const matchesFilter = activeFilter === "전체" || issue.category === activeFilter;
    const text = `${issue.title} ${issue.category} ${issue.source} ${issue.summary} ${issue.keywords.join(" ")}`.toLowerCase();
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
    renderKeywords(visible);
    drawTrend();
    renderPagination(0);
    return;
  }

  pageItems.forEach((issue) => {
    const card = document.createElement("article");
    card.className = "news-card";
    card.innerHTML = `
      <details class="source-picker">
        <summary class="signal-art" aria-label="관련 출처 링크 선택">
          <span class="source-hint">출처 선택</span>
        </summary>
        <div class="source-menu">
          ${renderRelatedLinks(issue)}
        </div>
      </details>
      <div>
        <div class="card-meta">
          <span class="badge ${issue.heat === "hot" ? "hot" : ""}">${issue.category}</span>
          <span>${issue.source}</span>
          <span>${issue.minutes}분 전</span>
        </div>
        <h3>${issue.title}</h3>
        <details class="summary-expand">
          <summary>기사 내용 더보기</summary>
          <p>${escapeHtml(issue.summary)}</p>
        </details>
        <div class="card-bottom">
          <span class="impact">중요도 ${issue.impact}</span>
          <span>${issue.keywords.map((keyword) => `#${keyword}`).join(" ")}</span>
        </div>
      </div>
    `;
    newsList.appendChild(card);
  });

  renderMetrics(visible);
  renderWeeklySummary();
  renderKeywords(visible);
  drawTrend();
  renderPagination(visible.length);
}

function getIssueDate(issue) {
  const date = new Date(issue.latestPublishedAt || Date.now() - issue.minutes * 60000);
  return Number.isNaN(date.getTime()) ? new Date() : date;
}

function renderWeeklySummary() {
  const now = Date.now();
  const weekAgo = now - 7 * 24 * 60 * 60 * 1000;
  const weeklyItems = issues.filter((issue) => getIssueDate(issue).getTime() >= weekAgo);
  const baseItems = weeklyItems.length ? weeklyItems : issues;
  const categories = ["전체", ...preferredCategories.filter((category) => baseItems.some((issue) => issue.category === category))];
  const extraCategories = [...new Set(baseItems.map((issue) => issue.category))]
    .filter((category) => !categories.includes(category))
    .sort((a, b) => a.localeCompare(b, "ko"));
  const allCategories = [...categories, ...extraCategories];

  if (!allCategories.includes(activeWeeklyCategory)) activeWeeklyCategory = "전체";

  weeklyCategoryTabs.innerHTML = allCategories
    .map((category) => {
      const activeClass = category === activeWeeklyCategory ? " active" : "";
      return `<button class="weekly-tab${activeClass}" type="button" data-weekly-category="${escapeHtml(category)}">${escapeHtml(category)}</button>`;
    })
    .join("");

  const targetItems =
    activeWeeklyCategory === "전체" ? baseItems : baseItems.filter((issue) => issue.category === activeWeeklyCategory);
  const categoryCounts = targetItems.reduce((acc, issue) => {
    acc[issue.category] = (acc[issue.category] || 0) + 1;
    return acc;
  }, {});
  const topCategories = Object.entries(categoryCounts)
    .sort((a, b) => b[1] - a[1])
    .slice(0, activeWeeklyCategory === "전체" ? 4 : 1);
  const topIssues = [...targetItems]
    .sort((a, b) => {
      const impactDiff = b.impact - a.impact;
      if (impactDiff) return impactDiff;
      return getIssueDate(b) - getIssueDate(a);
    })
    .slice(0, 4);
  const sourceCount = new Set(targetItems.flatMap((issue) => (issue.source || "").split(", ").filter(Boolean))).size;
  const weeklyLabel = weeklyItems.length ? "최근 7일" : "저장 데이터 기준";

  if (!targetItems.length) {
    weeklySummary.innerHTML = '<div class="empty-state">선택한 카테고리의 주간 이슈가 없습니다.</div>';
    return;
  }

  weeklySummary.innerHTML = `
    <div class="weekly-kpis">
      <div>
        <strong>${targetItems.length.toLocaleString("ko-KR")}</strong>
        <span>${weeklyLabel} 이슈</span>
      </div>
      <div>
        <strong>${sourceCount.toLocaleString("ko-KR")}</strong>
        <span>확인 출처</span>
      </div>
    </div>
    <div class="weekly-category-list">
      ${topCategories
        .map(([category, count]) => {
          const percent = Math.max(4, Math.round((count / targetItems.length) * 100));
          return `
            <div class="weekly-category">
              <div><span>${escapeHtml(category)}</span><strong>${count.toLocaleString("ko-KR")}</strong></div>
              <i style="width:${percent}%"></i>
            </div>
          `;
        })
        .join("")}
    </div>
    <ol class="weekly-issue-list">
      ${topIssues
        .map((issue) => `<li><strong>${escapeHtml(issue.category)}</strong><span>${escapeHtml(issue.title)}</span></li>`)
        .join("")}
    </ol>
  `;
}

function renderPagination(totalItems) {
  if (!totalItems || totalItems <= pageSize) {
    paginationControls.innerHTML = "";
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

  paginationControls.innerHTML = `
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

function renderMetrics(items) {
  const totalImpact = items.reduce((sum, issue) => sum + issue.impact, 0);
  const average = items.length ? totalImpact / items.length : 0;
  const now = new Date();

  topicCount.textContent = items.length;
  impactScore.textContent = average.toFixed(1);
  updateTime.textContent = now.toLocaleTimeString("ko-KR", {
    hour: "2-digit",
    minute: "2-digit",
  });
}

function renderKeywords(items) {
  const keywords = items.flatMap((issue) => issue.keywords);
  const counts = keywords.reduce((acc, keyword) => {
    acc[keyword] = (acc[keyword] || 0) + 1;
    return acc;
  }, {});

  keywordCloud.innerHTML = "";
  Object.entries(counts)
    .sort(([, a], [, b]) => b - a)
    .slice(0, 12)
    .forEach(([keyword, count]) => {
      const chip = document.createElement("button");
      chip.type = "button";
      chip.className = "keyword-chip";
      chip.textContent = `#${keyword}${count > 1 ? ` ${count}` : ""}`;
      chip.addEventListener("click", () => {
        searchInput.value = keyword;
        currentPage = 1;
        renderNews();
      });
      keywordCloud.appendChild(chip);
    });
}

function drawTrend() {
  const canvas = trendCanvas;
  const ctx = canvas.getContext("2d");
  const width = canvas.width;
  const height = canvas.height;
  const padding = 34;

  ctx.clearRect(0, 0, width, height);
  ctx.strokeStyle = "#dde4ec";
  ctx.lineWidth = 1;

  for (let i = 0; i < 5; i += 1) {
    const y = padding + i * ((height - padding * 2) / 4);
    ctx.beginPath();
    ctx.moveTo(padding, y);
    ctx.lineTo(width - padding, y);
    ctx.stroke();
  }

  const points = trendPoints.map((value, index) => {
    const x = padding + index * ((width - padding * 2) / (trendPoints.length - 1));
    const y = height - padding - (value / 100) * (height - padding * 2);
    return { x, y };
  });

  ctx.beginPath();
  points.forEach((point, index) => {
    if (index === 0) ctx.moveTo(point.x, point.y);
    else ctx.lineTo(point.x, point.y);
  });
  ctx.strokeStyle = "#2563eb";
  ctx.lineWidth = 4;
  ctx.stroke();

  points.forEach((point, index) => {
    ctx.beginPath();
    ctx.arc(point.x, point.y, index === points.length - 1 ? 6 : 4, 0, Math.PI * 2);
    ctx.fillStyle = index === points.length - 1 ? "#c2410c" : "#13805a";
    ctx.fill();
  });

  ctx.fillStyle = "#657080";
  ctx.font = "14px Segoe UI, sans-serif";
  ctx.fillText("확산도", padding, 22);
  ctx.fillText("최근 12구간", width - 112, height - 12);
}

categoryFilters.addEventListener("click", (event) => {
  const button = event.target.closest(".segment");
  if (!button) return;
  activeFilter = button.dataset.filter;
  currentPage = 1;
  renderCategoryFilters();
  renderNews();
});

paginationControls.addEventListener("click", (event) => {
  const button = event.target.closest(".page-button");
  if (!button || button.disabled) return;
  currentPage = Number(button.dataset.page);
  renderNews();
  newsList.scrollIntoView({ block: "start", behavior: "smooth" });
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

async function refreshFromServer() {
  if (location.protocol !== "file:") {
    try {
      const response = await fetch("/api/refresh", { method: "POST" });
      if (!response.ok) throw new Error(`refresh ${response.status}`);
      await loadServerBriefs();
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

searchInput.addEventListener("input", () => {
  currentPage = 1;
  renderNews();
});
document.addEventListener("click", (event) => {
  document.querySelectorAll(".source-picker[open]").forEach((picker) => {
    if (!picker.contains(event.target)) picker.removeAttribute("open");
  });
});
loadServerBriefs().then(() => {
  renderCategoryFilters();
  renderNews();
});
