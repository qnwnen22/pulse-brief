const state = {
  csrfToken: "",
  activePanel: "dashboardPanel",
  articlePage: 1,
  articlePageSize: 25,
  categories: [],
  sources: [],
  dashboard: null,
};

const $ = (selector) => document.querySelector(selector);
const loginView = $("#loginView");
const adminShell = $("#adminShell");
const statusMessage = $("#statusMessage");

function escapeHtml(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function formatNumber(value) {
  return Number(value || 0).toLocaleString("ko-KR");
}

function formatDate(value) {
  if (!value) return "-";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "-";
  return date.toLocaleString("ko-KR", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function setStatus(message, type = "") {
  statusMessage.textContent = message || "";
  statusMessage.className = `status-message ${type}`.trim();
}

function setBusy(button, isBusy) {
  if (!button) return;
  button.disabled = isBusy;
}

async function api(path, options = {}) {
  const method = options.method || "GET";
  const headers = { ...(options.headers || {}) };
  const request = {
    method,
    credentials: "include",
    headers,
  };

  if (method !== "GET" && state.csrfToken) {
    headers["X-CSRF-Token"] = state.csrfToken;
  }

  if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
    request.body = JSON.stringify(options.body);
  }

  const response = await fetch(path, request);
  const contentType = response.headers.get("content-type") || "";
  const payload = contentType.includes("application/json") ? await response.json() : await response.text();

  if (!response.ok) {
    if (response.status === 401) showLogin();
    const message = typeof payload === "string" ? payload : payload.error || "request_failed";
    throw new Error(message);
  }

  return payload;
}

function showLogin() {
  adminShell.hidden = true;
  loginView.hidden = false;
  $("#adminTokenInput")?.focus();
}

function showAdmin() {
  loginView.hidden = true;
  adminShell.hidden = false;
}

async function checkSession() {
  try {
    const session = await api("/api/admin/session");
    if (!session.authenticated) {
      showLogin();
      return;
    }

    state.csrfToken = session.csrfToken || "";
    showAdmin();
    await loadActivePanel();
  } catch {
    showLogin();
  }
}

async function login(event) {
  event.preventDefault();
  const button = event.submitter;
  const message = $("#loginMessage");
  setBusy(button, true);
  message.textContent = "";
  message.classList.remove("error");

  try {
    const token = $("#adminTokenInput").value;
    const result = await api("/api/admin/login", {
      method: "POST",
      body: { token },
    });
    state.csrfToken = result.csrfToken || "";
    $("#adminTokenInput").value = "";
    showAdmin();
    await loadActivePanel();
  } catch (error) {
    message.textContent = `로그인 실패: ${error.message}`;
    message.classList.add("error");
  } finally {
    setBusy(button, false);
  }
}

async function logout() {
  try {
    await api("/api/admin/logout", { method: "POST" });
  } finally {
    state.csrfToken = "";
    showLogin();
  }
}

function switchPanel(panelId) {
  state.activePanel = panelId;
  document.querySelectorAll(".nav-button").forEach((button) => {
    button.classList.toggle("active", button.dataset.panel === panelId);
  });
  document.querySelectorAll(".admin-panel").forEach((panel) => {
    panel.classList.toggle("active", panel.id === panelId);
  });

  const titles = {
    dashboardPanel: "운영 대시보드",
    articlesPanel: "기사 검수",
    rssPanel: "RSS 소스 관리",
    jobsPanel: "수집/요약 작업",
    logsPanel: "운영 로그",
  };
  $("#adminTitle").textContent = titles[panelId] || "관리자";
  loadActivePanel();
}

async function loadActivePanel() {
  if (state.activePanel === "dashboardPanel") return loadDashboard();
  if (state.activePanel === "articlesPanel") return loadArticles();
  if (state.activePanel === "rssPanel") return loadFeeds();
  if (state.activePanel === "logsPanel") return loadDashboard();
  return Promise.resolve();
}

async function loadDashboard() {
  const dashboard = await api("/api/admin/dashboard");
  state.dashboard = dashboard;
  renderDashboard(dashboard);
  renderLogs(dashboard.recentEvents || []);
  applyCollectorPolicy(dashboard);
}

function renderDashboard(dashboard) {
  const storage = dashboard.storage || {};
  const contentFetch = dashboard.contentFetch || {};
  const summaries = dashboard.summaries || {};
  const pipeline = dashboard.pipeline || {};
  $("#dashboardMetrics").innerHTML = [
    metricCard("전체 기사", formatNumber(storage.articleCount), `유효 ${formatNumber(storage.effectiveArticleCount)}건`),
    metricCard("이슈 그룹", formatNumber(storage.groupCount), `출처 ${formatNumber(storage.sourceCount)}곳`),
    metricCard("본문 성공", `${Math.round((contentFetch.successRate || 0) * 100)}%`, `실패 ${formatNumber(contentFetch.failed)}건`),
    metricCard("요약", `${formatNumber(summaries.dailyCount)} / ${formatNumber(summaries.weeklyCount)}`, "일간 / 주간"),
    metricCard("RSS", formatNumber(dashboard.rss?.feedCount), `${dashboard.rss?.refreshIntervalMinutes || 10}분 주기`),
    metricCard("수집 모드", dashboard.collector?.webHostedRefreshEnabled ? "웹 내장" : "분리", dashboard.collector?.webManualRefreshEnabled ? "웹 수동 수집 가능" : "Collector 전용"),
    metricCard("버전", dashboard.server?.version || "-", dashboard.server?.environment || "Production"),
    metricCard("AI", dashboard.server?.openAiConfigured ? "연결됨" : "미연결", dashboard.server?.database || "MongoDB"),
    metricCard("파이프라인", pipeline.status || "not_started", pipeline.finishedAt ? formatDate(pipeline.finishedAt) : "대기 중"),
  ].join("");

  const warnings = dashboard.warnings || [];
  $("#warningList").innerHTML = warnings.length
    ? warnings.map((warning) => `
        <div class="stack-item">
          <strong>${escapeHtml(warning.level)} · ${escapeHtml(warning.code)}</strong>
          <span>${escapeHtml(warning.message)}</span>
        </div>
      `).join("")
    : '<div class="stack-item"><strong>경고 없음</strong><span>현재 진단 기준에서 즉시 조치할 항목이 없습니다.</span></div>';

  const categories = dashboard.categories || [];
  $("#categoryList").innerHTML = categories.length
    ? categories.slice(0, 12).map((item) => `
        <div class="stack-item">
          <strong>${escapeHtml(item.category)}</strong>
          <span>이슈 ${formatNumber(item.groupCount)}건 · 기사 ${formatNumber(item.articleCount)}건</span>
        </div>
      `).join("")
    : '<div class="stack-item"><strong>카테고리 없음</strong><span>저장된 그룹 데이터가 없습니다.</span></div>';
}

function applyCollectorPolicy(dashboard) {
  const refreshButton = document.querySelector('.job-button[data-job="refresh"]');
  if (!refreshButton) return;

  const manualRefreshEnabled = Boolean(dashboard.collector?.webManualRefreshEnabled);
  refreshButton.disabled = !manualRefreshEnabled;
  refreshButton.title = manualRefreshEnabled
    ? "웹 관리자 페이지에서 RSS 수집을 실행합니다."
    : "RSS 수집은 PulseBrief.Collector에서 실행됩니다.";
}

function metricCard(label, value, detail) {
  return `
    <article class="metric-card">
      <span>${escapeHtml(label)}</span>
      <strong>${escapeHtml(value)}</strong>
      <small>${escapeHtml(detail)}</small>
    </article>
  `;
}

function renderLogs(events) {
  $("#logList").innerHTML = events.length
    ? events.map((event) => `
        <article class="log-item">
          <strong>${escapeHtml(event.level)} · ${escapeHtml(event.type)}</strong>
          <span>${formatDate(event.createdAt)}</span>
          <span>${escapeHtml(event.message)}</span>
        </article>
      `).join("")
    : '<article class="log-item"><strong>로그 없음</strong><span>현재 프로세스에 기록된 최근 이벤트가 없습니다.</span></article>';
}

function articleQueryString() {
  const params = new URLSearchParams();
  const filters = {
    query: $("#articleSearch").value.trim(),
    category: $("#articleCategoryFilter").value,
    source: $("#articleSourceFilter").value,
    contentStatus: $("#articleStatusFilter").value,
    excluded: $("#articleExcludedFilter").value,
    page: state.articlePage,
    pageSize: state.articlePageSize,
  };

  Object.entries(filters).forEach(([key, value]) => {
    if (value !== "" && value !== null && value !== undefined) params.set(key, value);
  });
  return params.toString();
}

async function loadArticles() {
  const result = await api(`/api/admin/articles?${articleQueryString()}`);
  state.categories = result.categories || [];
  state.sources = result.sources || [];
  populateSelect("#articleCategoryFilter", state.categories, "전체 카테고리");
  populateSelect("#articleSourceFilter", state.sources, "전체 출처");
  renderArticles(result);
}

function populateSelect(selector, values, defaultLabel) {
  const select = $(selector);
  const currentValue = select.value;
  select.innerHTML = `<option value="">${escapeHtml(defaultLabel)}</option>`
    + values.map((value) => `<option value="${escapeHtml(value)}">${escapeHtml(value)}</option>`).join("");
  select.value = values.includes(currentValue) ? currentValue : "";
}

function renderArticles(result) {
  const tbody = $("#articleTableBody");
  tbody.innerHTML = (result.items || []).length
    ? result.items.map(renderArticleRow).join("")
    : '<tr><td colspan="5">조건에 맞는 기사가 없습니다.</td></tr>';

  $("#articlePagination").innerHTML = `
    <span>${formatNumber(result.totalCount)}건 · ${result.page}/${result.pageCount}페이지</span>
    <button type="button" data-page="${result.page - 1}" ${result.page <= 1 ? "disabled" : ""}>이전</button>
    <button type="button" data-page="${result.page + 1}" ${result.page >= result.pageCount ? "disabled" : ""}>다음</button>
  `;
}

function renderArticleRow(article) {
  const status = article.contentFetchStatus || "pending";
  const categoryOptions = state.categories.map((category) => {
    const selected = category === article.category ? "selected" : "";
    return `<option value="${escapeHtml(category)}" ${selected}>${escapeHtml(category)}</option>`;
  }).join("");

  return `
    <tr data-article-id="${escapeHtml(article.id)}">
      <td>
        <div class="article-title">
          <a href="${escapeHtml(article.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(article.title)}</a>
          <span>${escapeHtml(article.source)}${article.author ? ` · ${escapeHtml(article.author)}` : ""}</span>
          <span>${escapeHtml(article.summaryPreview || article.contentPreview || "")}</span>
          ${article.isExcluded ? '<span class="badge excluded">제외됨</span>' : ""}
        </div>
      </td>
      <td>
        <select class="row-category-select" data-id="${escapeHtml(article.id)}">
          ${categoryOptions}
        </select>
      </td>
      <td><span class="badge ${escapeHtml(status)}">${escapeHtml(status)}</span></td>
      <td>${formatDate(article.publishedAt)}</td>
      <td>
        <div class="row-actions">
          <button type="button" data-action="detail" data-id="${escapeHtml(article.id)}">상세</button>
          <button type="button" data-action="toggle-excluded" data-id="${escapeHtml(article.id)}" data-excluded="${article.isExcluded ? "false" : "true"}">
            ${article.isExcluded ? "복원" : "제외"}
          </button>
        </div>
      </td>
    </tr>
  `;
}

async function patchArticle(id, body) {
  const result = await api(`/api/admin/articles/${encodeURIComponent(id)}`, {
    method: "PATCH",
    body,
  });
  setStatus("기사 정보가 저장되었습니다.", "success");
  await loadArticles();
  return result;
}

async function openArticleDetail(id) {
  const article = await api(`/api/admin/articles/${encodeURIComponent(id)}`);
  $("#dialogTitle").textContent = article.title || "기사 상세";
  $("#articleDetailBody").innerHTML = `
    <div class="detail-grid" data-detail-id="${escapeHtml(article.id)}">
      <div class="detail-field full">
        <label>제목</label>
        <input id="detailTitle" value="${escapeHtml(article.title)}" />
      </div>
      <div class="detail-field">
        <label>출처</label>
        <input id="detailSource" value="${escapeHtml(article.source)}" />
      </div>
      <div class="detail-field">
        <label>작성자</label>
        <input id="detailAuthor" value="${escapeHtml(article.author)}" />
      </div>
      <div class="detail-field">
        <label>카테고리</label>
        <select id="detailCategory">
          ${state.categories.map((category) => `<option value="${escapeHtml(category)}" ${category === article.category ? "selected" : ""}>${escapeHtml(category)}</option>`).join("")}
        </select>
      </div>
      <label class="checkbox-label">
        <input id="detailExcluded" type="checkbox" ${article.isExcluded ? "checked" : ""} />
        공개 화면과 요약 후보에서 제외
      </label>
      <div class="detail-field full">
        <label>RSS 대표 내용</label>
        <textarea id="detailSummary">${escapeHtml(article.summary)}</textarea>
      </div>
      <div class="detail-field full">
        <label>수집 본문</label>
        <pre>${escapeHtml(article.content || "수집된 본문이 없습니다.")}</pre>
      </div>
      <div class="detail-field full">
        <label>원문 URL</label>
        <pre>${escapeHtml(article.url)}</pre>
      </div>
      <div class="row-actions">
        <button id="detailSaveButton" type="button">저장</button>
        <button id="detailCloseButton" class="secondary-button" type="button">닫기</button>
      </div>
    </div>
  `;
  $("#articleDialog").showModal();
}

async function saveArticleDetail() {
  const wrapper = $("#articleDetailBody [data-detail-id]");
  if (!wrapper) return;

  await patchArticle(wrapper.dataset.detailId, {
    title: $("#detailTitle").value,
    source: $("#detailSource").value,
    author: $("#detailAuthor").value,
    category: $("#detailCategory").value,
    summary: $("#detailSummary").value,
    isExcluded: $("#detailExcluded").checked,
  });
  $("#articleDialog").close();
}

async function loadFeeds() {
  const result = await api("/api/admin/rss-feeds");
  $("#rssSummary").textContent = `전체 ${formatNumber(result.totalCount)}개 · 활성 ${formatNumber(result.activeCount)}개 · 비활성 ${formatNumber(result.inactiveCount)}개`;
  $("#rssTableBody").innerHTML = (result.feeds || []).map((feed) => `
    <tr>
      <td><strong>${escapeHtml(feed.publisher || "알 수 없음")}</strong></td>
      <td><a href="${escapeHtml(feed.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(feed.url)}</a></td>
      <td>
        ${feed.guideUrl
          ? `<a class="source-guide-link" href="${escapeHtml(feed.guideUrl)}" target="_blank" rel="noopener noreferrer">안내 페이지</a>`
          : '<span class="muted-text">-</span>'}
      </td>
      <td><span class="badge ${feed.isActive ? "success" : "pending"}">${feed.isActive ? "활성" : "비활성"}</span></td>
      <td>
        <div class="row-actions">
          <button type="button" data-feed-action="toggle" data-url="${escapeHtml(feed.url)}" data-active="${feed.isActive ? "false" : "true"}">
            ${feed.isActive ? "비활성화" : "활성화"}
          </button>
          <button type="button" data-feed-action="remove" data-url="${escapeHtml(feed.url)}">삭제</button>
        </div>
      </td>
    </tr>
  `).join("");
}

async function addFeed(event) {
  event.preventDefault();
  await api("/api/admin/rss-feeds", {
    method: "POST",
    body: {
      url: $("#rssUrlInput").value,
      isActive: $("#rssActiveInput").checked,
    },
  });
  $("#rssUrlInput").value = "";
  $("#rssActiveInput").checked = true;
  setStatus("RSS 피드를 추가했습니다.", "success");
  await loadFeeds();
}

async function runJob(job, button) {
  setBusy(button, true);
  setStatus("작업을 실행 중입니다.", "");
  $("#jobResult").textContent = "";

  try {
    let result;
    if (job === "refresh") {
      result = await api("/api/admin/refresh", { method: "POST" });
    } else if (job === "content") {
      const limit = Number($("#contentLimitInput").value || 200);
      result = await api(`/api/admin/fetch-missing-content?limit=${encodeURIComponent(limit)}`, { method: "POST" });
    } else if (job === "images") {
      const limit = Number($("#imageLimitInput").value || 200);
      result = await api(`/api/admin/fetch-missing-images?limit=${encodeURIComponent(limit)}`, { method: "POST" });
    } else if (job === "daily") {
      result = await api("/api/admin/summaries/daily/regenerate", {
        method: "POST",
        body: { date: $("#dailyDateInput").value || null },
      });
    } else if (job === "weekly") {
      result = await api("/api/admin/summaries/weekly/regenerate", {
        method: "POST",
        body: { endDate: $("#weeklyDateInput").value || null },
      });
    }

    $("#jobResult").textContent = JSON.stringify(result, null, 2);
    setStatus("작업이 완료되었습니다.", "success");
    await loadDashboard();
  } catch (error) {
    setStatus(`작업 실패: ${error.message}`, "error");
  } finally {
    setBusy(button, false);
  }
}

document.addEventListener("click", async (event) => {
  const navButton = event.target.closest(".nav-button[data-panel]");
  if (navButton) {
    switchPanel(navButton.dataset.panel);
    return;
  }

  const actionButton = event.target.closest("button[data-action]");
  if (actionButton) {
    const id = actionButton.dataset.id;
    if (actionButton.dataset.action === "detail") await openArticleDetail(id);
    if (actionButton.dataset.action === "toggle-excluded") {
      await patchArticle(id, { isExcluded: actionButton.dataset.excluded === "true" });
    }
    return;
  }

  const pageButton = event.target.closest("#articlePagination button[data-page]");
  if (pageButton) {
    state.articlePage = Number(pageButton.dataset.page);
    await loadArticles();
    return;
  }

  const feedButton = event.target.closest("button[data-feed-action]");
  if (feedButton) {
    const url = feedButton.dataset.url;
    if (feedButton.dataset.feedAction === "toggle") {
      await api("/api/admin/rss-feeds", {
        method: "PATCH",
        body: { url, isActive: feedButton.dataset.active === "true" },
      });
      setStatus("RSS 피드 상태를 변경했습니다.", "success");
      await loadFeeds();
    }
    if (feedButton.dataset.feedAction === "remove" && confirm("이 RSS 피드를 삭제할까요?")) {
      await api("/api/admin/rss-feeds/remove", {
        method: "POST",
        body: { url },
      });
      setStatus("RSS 피드를 삭제했습니다.", "success");
      await loadFeeds();
    }
    return;
  }

  const jobButton = event.target.closest(".job-button[data-job]");
  if (jobButton) {
    await runJob(jobButton.dataset.job, jobButton);
    return;
  }

  if (event.target.id === "reloadButton") {
    setBusy(event.target, true);
    try {
      await loadActivePanel();
      setStatus("관리자 데이터를 다시 불러왔습니다.", "success");
    } finally {
      setBusy(event.target, false);
    }
  }

  if (event.target.id === "detailSaveButton") await saveArticleDetail();
  if (event.target.id === "detailCloseButton") $("#articleDialog").close();
});

document.addEventListener("change", async (event) => {
  const categorySelect = event.target.closest(".row-category-select");
  if (!categorySelect) return;

  await patchArticle(categorySelect.dataset.id, { category: categorySelect.value });
});

$("#loginForm").addEventListener("submit", login);
$("#logoutButton").addEventListener("click", logout);
$("#articleFilters").addEventListener("submit", async (event) => {
  event.preventDefault();
  state.articlePage = 1;
  await loadArticles();
});
$("#articleFilterReset").addEventListener("click", async () => {
  $("#articleSearch").value = "";
  $("#articleCategoryFilter").value = "";
  $("#articleSourceFilter").value = "";
  $("#articleStatusFilter").value = "";
  $("#articleExcludedFilter").value = "";
  state.articlePage = 1;
  await loadArticles();
});
$("#rssAddForm").addEventListener("submit", addFeed);

checkSession();
