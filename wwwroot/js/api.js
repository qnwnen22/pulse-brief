async function loadServerBriefs() {
  if (location.protocol === "file:") return false;

  try {
    const response = await fetchWithTimeout("/api/briefs", {}, 20000);
    if (!response.ok) throw new Error(`briefs ${response.status}`);
    const serverIssues = await response.json();
    if (!Array.isArray(serverIssues)) return false;
    issues = serverIssues;
    return true;
  } catch (error) {
    console.warn(`[briefs] ${error.message}`);
    return false;
  }
}

async function loadNewsStats() {
  if (location.protocol === "file:") return false;

  try {
    const response = await fetchWithTimeout("/api/news-stats", { cache: "no-store" }, 5000);
    if (!response.ok) throw new Error(`news-stats ${response.status}`);
    newsStats = await response.json();
    return true;
  } catch (error) {
    console.warn(`[news-stats] ${error.message}`);
    newsStats = null;
    return false;
  }
}

async function fetchWithTimeout(resource, options = {}, timeoutMs = 15000) {
  const controller = new AbortController();
  const timeoutId = window.setTimeout(() => controller.abort(), timeoutMs);

  try {
    return await fetch(resource, {
      ...options,
      signal: controller.signal
    });
  } finally {
    window.clearTimeout(timeoutId);
  }
}

const summaryHistoryConfigurations = {
  daily: {
    history: dailySummaryHistory,
    endpoint: "/api/daily-summary",
    dateParameter: "date",
    requestedDate: getRequestedDailySummaryDate,
    renderOptions: renderDailySummaryDateOptions,
    getDate: summary => summary?.date || "",
    setSummary: summary => { dailyBrief = summary; }
  },
  weekly: {
    history: weeklySummaryHistory,
    endpoint: "/api/weekly-summary",
    dateParameter: "endDate",
    requestedDate: getRequestedWeeklySummaryDate,
    renderOptions: renderWeeklySummaryDateOptions,
    getDate: summary => parseWeeklySummaryRange(summary)?.end || "",
    setSummary: summary => { weeklyBrief = summary; }
  }
};

async function loadSummaryDates(configuration, preferredDate = "") {
  if (location.protocol === "file:") return false;

  const { history, endpoint, renderOptions, requestedDate } = configuration;
  const requestId = ++history.requestId;
  history.datesStatus = "loading";
  history.status = "loading";
  renderOptions();
  renderWeeklySummary();
  try {
    const response = await fetchWithTimeout(`${endpoint}/dates`, { cache: "no-store" }, 5000);
    if (!response.ok) throw new Error(`${endpoint}/dates ${response.status}`);
    const values = await response.json();
    if (requestId !== history.requestId) return false;
    if (!Array.isArray(values)) throw new Error(`${endpoint}/dates invalid response`);

    history.dates = [...new Set(values.filter(isDailySummaryDateKey))].sort().reverse();
    const preferred = preferredDate || requestedDate();
    history.selectedDate = history.dates.includes(preferred) ? preferred : history.dates[0] || "";
    history.datesStatus = "ready";
    renderOptions();
    return true;
  } catch (error) {
    if (requestId !== history.requestId) return false;
    console.warn(`[summary-dates] ${error.message}`);
    history.datesStatus = "error";
    renderOptions();
    return false;
  }
}

async function loadStoredSummary(configuration, date = configuration.history.selectedDate) {
  if (location.protocol === "file:") return false;

  const { history, endpoint, dateParameter, renderOptions, getDate, setSummary } = configuration;
  const requestId = ++history.requestId;
  const targetDate = isDailySummaryDateKey(date) ? date : "";
  if (targetDate) history.selectedDate = targetDate;
  history.status = "loading";
  renderOptions();
  renderWeeklySummary();

  try {
    const query = targetDate ? `?${dateParameter}=${encodeURIComponent(targetDate)}` : "";
    const response = await fetchWithTimeout(`${endpoint}${query}`, { cache: "no-store" }, 10000);
    if (requestId !== history.requestId) return false;
    if (response.status === 404) {
      setSummary(null);
      history.status = "missing";
      return false;
    }
    if (!response.ok) throw new Error(`${endpoint} ${response.status}`);
    const summary = await response.json();
    if (requestId !== history.requestId) return false;
    const actualDate = getDate(summary);
    if (!isDailySummaryDateKey(actualDate) || (targetDate && actualDate !== targetDate)) {
      throw new Error(`${endpoint} returned a different period`);
    }

    setSummary(summary);
    history.status = "ready";
    history.selectedDate = actualDate;
    if (!history.dates.includes(actualDate)) {
      history.dates = [...history.dates, actualDate].sort().reverse();
    }
    return true;
  } catch (error) {
    if (requestId !== history.requestId) return false;
    console.warn(`[summary] ${error.message}`);
    setSummary(null);
    history.status = "error";
    return false;
  } finally {
    if (requestId === history.requestId) {
      renderOptions();
      renderWeeklySummary();
    }
  }
}

function loadDailySummary(date = dailySummaryHistory.selectedDate) {
  return loadStoredSummary(summaryHistoryConfigurations.daily, date);
}

function loadWeeklySummary(date = weeklySummaryHistory.selectedDate) {
  return loadStoredSummary(summaryHistoryConfigurations.weekly, date);
}

async function loadSummaryHistory(type, preferredDate = "") {
  const configuration = summaryHistoryConfigurations[type];
  const requestId = configuration.history.requestId + 1;
  await loadSummaryDates(configuration, preferredDate);
  // Another selection may supersede the history request while it is in flight.
  if (configuration.history.requestId === requestId) {
    return loadStoredSummary(configuration);
  }
  return false;
}

async function loadAppVersion() {
  if (!appVersion || location.protocol === "file:") return false;

  try {
    const response = await fetch("/api/health", { cache: "no-store" });
    if (!response.ok) throw new Error(`health ${response.status}`);

    const health = await response.json();
    const version = String(health.version || "").trim();
    if (!version) return false;

    appVersion.textContent = `v${version}`;
    appVersion.title = `현재 배포 버전 ${version}`;
    appVersion.hidden = false;
    return true;
  } catch (error) {
    console.warn(`[app-version] ${error.message}`);
    return false;
  }
}

async function refreshFromServer() {
  if (refreshButton?.disabled) return;

  setRefreshButtonBusy(true);
  try {
    if (location.protocol !== "file:") {
      const [loaded] = await Promise.all([loadServerBriefs(), loadNewsStats()]);
      if (!loaded) console.warn("[refresh] failed to reload briefs");
      await Promise.all([
        loadSummaryHistory("daily", dailySummaryHistory.selectedDate),
        loadSummaryHistory("weekly", weeklySummaryHistory.selectedDate)
      ]);
    } else {
      issues.unshift(issues.pop());
    }

    currentPage = 1;
    renderPublisherFilter();
    renderCategoryFilters();
    renderNews();
  } catch (error) {
    console.warn(`[refresh] ${error.message}`);
  } finally {
    setRefreshButtonBusy(false);
  }
}
