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
