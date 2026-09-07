// Application entry point; loaded last after feature and event scripts.
async function initializeApp() {
  document.body.classList.add("loading-active");

  try {
    await Promise.all([loadServerBriefs(), loadNewsStats()]);
    showView("briefing");
    renderPublisherFilter();
    renderCategoryFilters();
    renderNews();
    loadAppVersion().catch((error) => {
      console.warn(`[app-version] ${error.message}`);
    });
    Promise.all([loadDailySummary(), loadWeeklySummary()]).catch((error) => {
      console.warn(`[summary-load] ${error.message}`);
    });
  } finally {
    hideAppLoading();
  }
}

initializeApp();
