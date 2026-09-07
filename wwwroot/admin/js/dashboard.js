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
