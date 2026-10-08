// Application entry point; loaded last after feature and event scripts.
async function initializeApp() {
  document.body.classList.add("loading-active");

  try {
    renderNavigation();
    initializeStatistics();
    await Promise.all([loadServerBriefs(), loadNewsStats()]);
    renderPublisherFilter();
    renderCategoryFilters();
    renderNews();
    loadCollectionStatistics();
    loadAppVersion().catch((error) => {
      console.warn(`[app-version] ${error.message}`);
    });
    Promise.all([
      (async () => {
        await loadDailySummaryDates();
        return loadDailySummary(selectedDailySummaryDate);
      })(),
      loadWeeklySummary(),
    ]).catch((error) => {
      console.warn(`[summary-load] ${error.message}`);
    });
  } finally {
    hideAppLoading();
  }
}

initializeApp();
