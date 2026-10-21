
navList?.addEventListener("click", (event) => {
  const item = event.target.closest(".nav-item[data-view]");
  if (!item || !navList.contains(item)) return;
  showView(item.dataset.view);
});

document.querySelectorAll("[data-view].footer-link").forEach((item) => {
  item.addEventListener("click", () => {
    showView(item.dataset.view);
    window.scrollTo({ top: 0, behavior: "smooth" });
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

summaryCategoryTabs.addEventListener("click", (event) => {
  const button = event.target.closest("[data-summary-category]");
  if (!button) return;
  selectedSummaryCategories[activeSummaryPeriod] = button.dataset.summaryCategory;
  renderSummaries();
});

summaryPeriodTabs?.addEventListener("click", event => {
  const button = event.target.closest("[data-summary-period]");
  if (button) selectSummaryPeriod(button.dataset.summaryPeriod);
});

summaryPeriodTabs?.addEventListener("keydown", event => {
  const button = event.target.closest("[data-summary-period]");
  if (!button || !["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
  const tabs = [...summaryPeriodTabs.querySelectorAll("[data-summary-period]")];
  let index = tabs.indexOf(button);
  if (event.key === "Home") index = 0;
  else if (event.key === "End") index = tabs.length - 1;
  else index = (index + (event.key === "ArrowRight" ? 1 : -1) + tabs.length) % tabs.length;
  event.preventDefault();
  selectSummaryPeriod(tabs[index].dataset.summaryPeriod);
  tabs[index].focus();
});

function bindSummaryHistorySelect(select, history, updateAddress, loadSummary) {
  select?.addEventListener("change", () => {
    const date = select.value;
    if (!isDailySummaryDateKey(date)) return;
    history.selectedDate = date;
    updateAddress(date);
    loadSummary(date);
  });
}

bindSummaryHistorySelect(dailySummaryDateSelect, dailySummaryHistory, updateDailySummaryAddress, loadDailySummary);
bindSummaryHistorySelect(weeklySummaryDateSelect, weeklySummaryHistory, updateWeeklySummaryAddress, loadWeeklySummary);

refreshButton?.addEventListener("click", () => {
  refreshFromServer();
});


searchInput?.addEventListener("input", () => {
  currentPage = 1;
  renderNews();
});

[dateFilter, publisherFilter, articleCountFilter, sortSelect]
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
