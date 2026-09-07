async function loadServerBriefs() {
  if (location.protocol === "file:") return false;

  try {
    const response = await fetchWithTimeout("/api/briefs", {}, 20000);
    if (!response.ok) throw new Error(`briefs ${response.status}`);
    const serverIssues = await response.json();
    if (!Array.isArray(serverIssues) || !serverIssues.length) return false;
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

async function loadDailySummary() {
  if (location.protocol === "file:") return false;

  try {
    const response = await fetch("/api/daily-summary");
    if (!response.ok) throw new Error(`daily-summary ${response.status}`);
    dailyBrief = await response.json();
    renderWeeklySummary();
    return true;
  } catch (error) {
    console.warn(`[daily-summary] ${error.message}`);
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
      await Promise.all([loadDailySummary(), loadWeeklySummary()]);
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
