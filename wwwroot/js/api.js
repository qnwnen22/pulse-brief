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

async function loadDailySummaryDates(preferredDate = "") {
  if (location.protocol === "file:") return false;

  dailySummaryDatesStatus = "loading";
  dailySummaryStatus = "loading";
  renderDailySummaryDateOptions();
  renderCategorySummary();
  try {
    const response = await fetchWithTimeout("/api/daily-summary/dates", { cache: "no-store" }, 5000);
    if (!response.ok) throw new Error(`daily-summary-dates ${response.status}`);
    const values = await response.json();
    if (!Array.isArray(values)) throw new Error("daily-summary-dates invalid response");

    dailySummaryDates = [...new Set(values.filter(isDailySummaryDateKey))].sort().reverse();
    const requestedDate = preferredDate || getRequestedDailySummaryDate();
    selectedDailySummaryDate = dailySummaryDates.includes(requestedDate)
      ? requestedDate
      : dailySummaryDates[0] || "";
    dailySummaryDatesStatus = "ready";
    renderDailySummaryDateOptions();
    return true;
  } catch (error) {
    console.warn(`[daily-summary-dates] ${error.message}`);
    dailySummaryDatesStatus = "error";
    renderDailySummaryDateOptions();
    return false;
  }
}

async function loadDailySummary(date = selectedDailySummaryDate) {
  if (location.protocol === "file:") return false;

  const targetDate = isDailySummaryDateKey(date) ? date : "";
  if (targetDate) selectedDailySummaryDate = targetDate;
  dailySummaryStatus = "loading";
  renderDailySummaryDateOptions();
  renderCategorySummary();

  try {
    const query = targetDate ? `?date=${encodeURIComponent(targetDate)}` : "";
    const response = await fetchWithTimeout(`/api/daily-summary${query}`, { cache: "no-store" }, 10000);
    if (response.status === 404) {
      dailyBrief = null;
      dailySummaryStatus = "missing";
      renderDailySummaryDateOptions();
      renderWeeklySummary();
      return false;
    }
    if (!response.ok) throw new Error(`daily-summary ${response.status}`);
    dailyBrief = await response.json();
    dailySummaryStatus = "ready";
    if (isDailySummaryDateKey(dailyBrief?.date)) {
      selectedDailySummaryDate = dailyBrief.date;
      if (!dailySummaryDates.includes(dailyBrief.date)) {
        dailySummaryDates = [...dailySummaryDates, dailyBrief.date].sort().reverse();
      }
    }
    renderDailySummaryDateOptions();
    renderWeeklySummary();
    return true;
  } catch (error) {
    console.warn(`[daily-summary] ${error.message}`);
    dailyBrief = null;
    dailySummaryStatus = "error";
    renderDailySummaryDateOptions();
    renderWeeklySummary();
    return false;
  }
}

async function loadWeeklySummary() {
  if (location.protocol === "file:") return false;

  try {
    const response = await fetch("/api/weekly-summary");
    if (!response.ok) throw new Error(`weekly-summary ${response.status}`);
    weeklyBrief = await response.json();
    renderWeeklySummary();
    return true;
  } catch (error) {
    console.warn(`[weekly-summary] ${error.message}`);
    renderWeeklySummary();
    return false;
  }
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
      const preferredDate = selectedDailySummaryDate;
      await loadDailySummaryDates(preferredDate);
      await Promise.all([loadDailySummary(selectedDailySummaryDate), loadWeeklySummary()]);
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
