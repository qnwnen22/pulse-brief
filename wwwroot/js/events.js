
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
