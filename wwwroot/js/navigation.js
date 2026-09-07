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


function resetSearchFilters() {
  if (searchInput) searchInput.value = "";
  if (dateFilter) dateFilter.value = "all";
  if (publisherFilter) publisherFilter.value = "all";
  if (articleCountFilter) articleCountFilter.value = "all";
  if (sortSelect) sortSelect.value = "latest";
  currentPage = 1;
  renderNews();
}


function setRefreshButtonBusy(isBusy) {
  if (!refreshButton) return;
  refreshButton.disabled = isBusy;
  refreshButton.setAttribute("aria-busy", String(isBusy));
  refreshButton.classList.toggle("is-loading", isBusy);
}
