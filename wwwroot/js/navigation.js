function getAvailableViewDefinitions() {
  const panelIds = new Set([...document.querySelectorAll(".view-panel[data-panel]")]
    .map((panel) => panel.dataset.panel));
  return viewDefinitions.filter((view) => panelIds.has(view.id));
}

function renderNavigation() {
  if (!navList) return;
  const definitions = getAvailableViewDefinitions();
  navList.replaceChildren(...definitions.map((view) => {
    const button = document.createElement("button");
    button.className = "nav-item";
    button.type = "button";
    button.dataset.view = view.id;
    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = view.icon || "";
    button.append(icon, document.createTextNode(view.label));
    return button;
  }));
  document.querySelectorAll(".footer-link[data-view]").forEach((item) => {
    const view = definitions.find((definition) => definition.id === item.dataset.view);
    item.hidden = !view;
    if (view) item.textContent = view.label;
  });
  showView(activeViewId);
}

function showView(viewId) {
  const definitions = getAvailableViewDefinitions();
  const view = definitions.find((definition) => definition.id === viewId) || definitions[0];
  if (!view) return;
  activeViewId = view.id;
  navList?.querySelectorAll(".nav-item[data-view]").forEach((item) => {
    const isActive = item.dataset.view === view.id;
    item.classList.toggle("active", isActive);
    if (isActive) item.setAttribute("aria-current", "page");
    else item.removeAttribute("aria-current");
  });
  document.querySelectorAll(".view-panel[data-panel]").forEach((panel) => {
    panel.classList.toggle("active", panel.dataset.panel === view.id);
  });
  newsMetricGrid?.classList.toggle("hidden", !view.showNewsMetrics);
  if (refreshButton) refreshButton.hidden = view.showRefresh === false;
  if (menuEyebrow) menuEyebrow.textContent = view.eyebrow;
  if (menuTitle) menuTitle.textContent = view.title;
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
