let statisticsPeriod = "7";
let statisticsRequest = 0;
let statisticsPublisher = "";
let statisticsCalendarMonth = "";
let statisticsCrossPublisher = "";
const statisticsCache = new Map();

function initializeStatistics() {
  document.querySelector("#statisticsPeriods")?.addEventListener("click", (event) => {
    const button = event.target.closest("button[data-period]");
    if (!button || button.dataset.period === statisticsPeriod) return;
    statisticsPeriod = button.dataset.period;
    document.querySelectorAll("#statisticsPeriods button").forEach((item) => {
      const active = item.dataset.period === statisticsPeriod;
      item.classList.toggle("active", active);
      item.setAttribute("aria-pressed", String(active));
    });
    loadCollectionStatistics();
  });
  document.querySelector("#statisticsRefresh")?.addEventListener("click", () => loadCollectionStatistics(true));
  document.querySelector("#statisticsContent")?.addEventListener("change", event => {
    const controls = {
      statisticsPublisher: { select: value => statisticsPublisher = value, target: "publisherTrendContent", render: renderPublisherTrend },
      statisticsCalendarMonth: { select: value => statisticsCalendarMonth = value, target: "statisticsCalendarContent", render: renderStatisticsCalendar },
      statisticsCrossPublisher: { select: value => statisticsCrossPublisher = value, target: "publisherCategoryDetail", render: renderPublisherCategoryDetail }
    };
    const control = controls[event.target.id];
    if (!control) return;
    control.select(event.target.value);
    const data = statisticsCache.get(statisticsPeriod)?.data;
    const content = document.getElementById(control.target);
    if (data && content) content.innerHTML = control.render(data);
  });
}

async function loadCollectionStatistics(force = false) {
  const content = document.querySelector("#statisticsContent");
  if (!content) return;
  const period = statisticsPeriod;
  const request = ++statisticsRequest;
  const cached = statisticsCache.get(period);
  if (!force && cached && Date.now() - cached.loadedAt < 60000) {
    content.setAttribute("aria-busy", "false");
    renderCollectionStatistics(cached.data);
    return;
  }
  content.setAttribute("aria-busy", "true");
  content.innerHTML = '<p class="statistics-empty" role="status">통계를 불러오는 중입니다.</p>';
  try {
    if (location.protocol === "file:") throw new Error("Statistics require the server API.");
    const response = await fetchWithTimeout(`/api/collection-statistics?period=${encodeURIComponent(period)}`, { cache: "no-store" }, 7000);
    if (!response.ok) throw new Error(`collection-statistics ${response.status}`);
    const data = await response.json();
    if (data.period !== period || !Array.isArray(data.trend) || !Array.isArray(data.publishers)) throw new Error("Invalid statistics response.");
    statisticsCache.set(period, { data, loadedAt: Date.now() });
    if (request !== statisticsRequest) return;
    renderCollectionStatistics(data);
  } catch (error) {
    if (request !== statisticsRequest) return;
    content.innerHTML = '<p class="statistics-empty" role="alert">통계를 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.</p>';
    console.warn(`[statistics] ${error.message}`);
  } finally {
    if (request === statisticsRequest) content.setAttribute("aria-busy", "false");
  }
}

function statisticsNumber(value, fraction = 0) {
  return Number.isFinite(value) ? value.toLocaleString("ko-KR", { maximumFractionDigits: fraction }) : "--";
}

function collectionTrendPoints(data) {
  return statisticsTrendPoints(data.trend, data.period);
}

function statisticsTrendPoints(points, period) {
  if (period !== "all") return points;
  const months = new Map();
  for (const point of points) {
    const key = point.date.slice(0, 7);
    const previous = months.get(key);
    const count = point.count == null || previous?.count === null ? null : (previous?.count || 0) + point.count;
    months.set(key, { date: key, count });
  }
  return [...months.values()];
}

function statisticsChange(value, unit, fraction = 0) {
  return Number.isFinite(value) ? `${value > 0 ? "+" : ""}${statisticsNumber(value === 0 ? 0 : value, fraction)}${unit}` : "--";
}

function renderStatisticsTrendChart(points, period, label, { formatDate = date => period === "all" ? date : date.slice(5).replace("-", "."), fraction = 0, showDetails = true } = {}) {
  const maximum = Math.max(1, ...points.map(point => point.count || 0));
  const countLabel = point => point.count == null ? (point.status || "집계 대기") : `${statisticsNumber(point.count, fraction)}건`;
  return `<div class="statistics-chart-scroll" tabindex="0" aria-label="${escapeHtml(label)}">
    <div class="statistics-bars" style="min-width:${points.length * (period === "all" ? 52 : 32)}px;grid-template-columns:repeat(${Math.max(1, points.length)},minmax(0,1fr))">
      ${points.map(point => `<div class="statistics-bar-column">
        <div class="statistics-bar-track"><div class="statistics-bar${point.count == null ? " missing" : ""}" style="height:${point.count == null ? 100 : Math.max(1, point.count / maximum * 100)}%" tabindex="0" title="${escapeHtml(point.date)}: ${escapeHtml(countLabel(point))}" aria-label="${escapeHtml(point.date)} ${escapeHtml(countLabel(point))}"></div></div>
        <span>${escapeHtml(formatDate(point.date))}</span></div>`).join("")}
    </div>
  </div>
  ${showDetails ? `<details class="statistics-details"><summary>수집 추이 상세</summary><div class="statistics-table-scroll"><table class="statistics-table"><thead><tr><th scope="col">날짜</th><th scope="col">수집 기사</th></tr></thead><tbody>${points.map(point => `<tr><th scope="row">${escapeHtml(point.date)}</th><td>${escapeHtml(countLabel(point))}</td></tr>`).join("")}</tbody></table></div></details>` : ""}`;
}

function renderStatisticsSelect(id, label, value, options, extraClass = "") {
  return `<label class="statistics-select-control ${extraClass}" for="${id}"><span>${escapeHtml(label)}</span><select id="${id}">${options.map(option => `<option value="${escapeHtml(option.value)}"${option.value === value ? " selected" : ""}>${escapeHtml(option.label)}</option>`).join("")}</select></label>`;
}

function renderPublisherTrend(data) {
  const series = (data.publisherTrends || []).find(item => item.publisher === statisticsPublisher);
  return series ? renderStatisticsTrendChart(statisticsTrendPoints(series.trend, data.period), data.period, `${statisticsPublisher} 수집 추이`)
    : '<p class="statistics-status">해당 기간에 수집된 언론사 데이터가 없습니다.</p>';
}

function renderPublisherTrendSection(data) {
  const series = data.publisherTrends || [];
  if (!series.some(item => item.publisher === statisticsPublisher)) statisticsPublisher = series[0]?.publisher || "";
  return `<section class="statistics-section publisher-trend-section" aria-labelledby="publisher-trend-title">
    <div class="section-head compact"><h2 id="publisher-trend-title">언론사별 ${data.period === "all" ? "월별" : "일별"} 수집 추이</h2>
      ${series.length ? renderStatisticsSelect("statisticsPublisher", "언론사", statisticsPublisher, series.map(item => ({ value: item.publisher, label: item.publisher }))) : ""}
    </div>
    <div id="publisherTrendContent">${renderPublisherTrend(data)}</div>
  </section>`;
}

function renderCategoryComparison(data) {
  const ready = data.period !== "all" && data.isReady && data.areCategoriesReady && data.isCategoryComparisonReady;
  let status = "직전 기간의 카테고리 집계가 부족합니다.";
  if (data.period === "all") status = "전체 기간은 직전 기간 비교 대상이 없습니다.";
  else if (!data.isReady || !data.areCategoriesReady) status = "현재 기간의 카테고리 집계 완료 후 표시됩니다.";
  return `<section class="statistics-section" aria-labelledby="category-change-title">
    <div class="section-head compact"><h2 id="category-change-title">카테고리별 증감</h2><span class="statistics-unit">동일 기간 비교</span></div>
    ${!ready ? `<p class="statistics-status" role="status">${status}</p>`
      : `<p class="statistics-meta">직전 ${escapeHtml(data.previousFromDate)} ~ ${escapeHtml(data.previousToDate)} 대비</p>
        <div class="statistics-table-scroll" tabindex="0" aria-label="카테고리별 증감 표"><table class="statistics-table category-change-table"><thead><tr><th scope="col">카테고리</th><th scope="col">현재 기사</th><th scope="col">직전 기사</th><th scope="col">기사 증감</th><th scope="col">증감률</th><th scope="col">비중 변화</th></tr></thead><tbody>${data.categories.map(item => `<tr><th scope="row">${escapeHtml(item.category)}</th><td data-label="현재 기사">${statisticsNumber(item.count)}건</td><td data-label="직전 기사">${statisticsNumber(item.previousCount)}건</td><td data-label="기사 증감">${statisticsChange(item.count - item.previousCount, "건")}</td><td data-label="증감률">${statisticsChange(item.changePercent, "%", 1)}</td><td data-label="비중 변화">${statisticsChange(item.shareChangePoints, "%p", 1)}</td></tr>`).join("")}</tbody></table></div>
        <p class="statistics-meta">직전 기사 수가 0건이면 증감률은 제외합니다. 어느 기간이든 전체 기사 수가 0건이면 비중 변화는 제외합니다.</p>`}
  </section>`;
}

function renderStatisticsShareTable({ id, label, labelKey, items, isReady, pendingText }) {
  return !isReady ? `<p class="statistics-status" role="status">${escapeHtml(pendingText)}</p>`
    : !items.length ? '<p class="statistics-status">해당 기간에 수집된 기사가 없습니다.</p>'
    : `<div class="statistics-table-scroll"><table class="statistics-table statistics-share-table ${id}-table"><thead><tr><th scope="col">${escapeHtml(label)}</th><th scope="col">수집 기사</th><th scope="col">비중</th></tr></thead><tbody>${items.map(item => `<tr><th scope="row">${escapeHtml(item[labelKey])}</th><td>${statisticsNumber(item.count)}건</td><td><div class="statistics-share"><span class="statistics-share-track" aria-hidden="true"><span style="width:${Math.max(0, Math.min(100, item.share))}%"></span></span><span>${statisticsNumber(item.share, 1)}%</span></div></td></tr>`).join("")}</tbody></table></div>`;
}

function renderStatisticsShareSection({ title, note, ...table }) {
  return `<section class="statistics-section" aria-labelledby="${table.id}-share-title">
    <div class="section-head compact"><h2 id="${table.id}-share-title">${escapeHtml(title)}</h2><span class="statistics-unit">저장 기사 기준</span></div>
    ${renderStatisticsShareTable(table)}
    <p class="statistics-meta">${escapeHtml(note)}</p>
  </section>`;
}

function renderStatisticsWeekdays(data) {
  const weekdays = data.weekdays || [];
  const points = weekdays.map(item => ({ date: item.label, count: item.average, status: item.expectedDays ? "미집계" : "해당 요일 없음" }));
  return `<section class="statistics-section weekday-section" aria-labelledby="weekday-title">
    <div class="section-head compact"><h2 id="weekday-title">요일별 평균 수집량</h2><span class="statistics-unit">단위: 건/일</span></div>
    ${renderStatisticsTrendChart(points, "7", "요일별 평균 수집량", { formatDate: date => date[0], fraction: 1, showDetails: false })}
    <p class="statistics-meta">완료된 날짜만 평균에 반영하며 수집 0건인 날짜도 포함합니다. 오늘은 제외합니다.</p>
    <details class="statistics-details"><summary>요일별 집계 상세</summary><div class="statistics-table-scroll"><table class="statistics-table weekday-table"><thead><tr><th scope="col">요일</th><th scope="col">집계/대상 일수</th><th scope="col">수집 기사</th><th scope="col">일평균</th></tr></thead><tbody>${weekdays.map(item => `<tr><th scope="row">${escapeHtml(item.label)}</th><td>${item.sampleDays}/${item.expectedDays}일</td><td>${statisticsNumber(item.total)}건</td><td>${statisticsNumber(item.average, 1)}건</td></tr>`).join("")}</tbody></table></div></details>
  </section>`;
}

function statisticsCalendarMonths(data) {
  return [...new Set(data.trend.map(point => point.date.slice(0, 7)))];
}

function renderStatisticsCalendar(data) {
  if (!statisticsCalendarMonth) return '<p class="statistics-status">해당 기간에 달력 데이터가 없습니다.</p>';
  const [year, month] = statisticsCalendarMonth.split("-").map(Number);
  const first = (new Date(Date.UTC(year, month - 1, 1)).getUTCDay() + 6) % 7;
  const count = new Date(Date.UTC(year, month, 0)).getUTCDate();
  const points = new Map(data.trend.map(point => [point.date, point]));
  const maximum = Math.max(1, ...data.trend.map(point => point.count || 0));
  const cells = Array.from({ length: Math.ceil((first + count) / 7) * 7 }, (_, index) => {
    const day = index - first + 1;
    if (day < 1 || day > count) return '<div class="statistics-calendar-gap" aria-hidden="true"></div>';
    const date = `${statisticsCalendarMonth}-${String(day).padStart(2, "0")}`;
    const point = points.get(date);
    if (!point) return `<div class="statistics-calendar-day outside" aria-label="${date} 기간 외"><span>${day}</span></div>`;
    const label = point.count == null ? "미집계" : `${statisticsNumber(point.count)}건`;
    const level = point.count == null ? "missing" : `level-${point.count === 0 ? 0 : Math.min(4, Math.ceil(point.count / maximum * 4))}`;
    return `<div class="statistics-calendar-day ${level}" tabindex="0" role="img" title="${date}: ${label}" aria-label="${date} ${label}"><span>${day}</span><small>${point.count == null ? "--" : statisticsNumber(point.count)}</small></div>`;
  });
  return `<div class="statistics-calendar" aria-label="${year}년 ${month}월 수집량 달력">
    ${["월", "화", "수", "목", "금", "토", "일"].map(day => `<span class="statistics-calendar-weekday">${day}</span>`).join("")}${cells.join("")}
  </div>`;
}

function renderStatisticsCalendarSection(data) {
  const months = statisticsCalendarMonths(data);
  if (!months.includes(statisticsCalendarMonth)) statisticsCalendarMonth = months.at(-1) || "";
  return `<section class="statistics-section" aria-labelledby="calendar-title">
    <div class="section-head compact"><h2 id="calendar-title">수집량 달력</h2>${renderStatisticsSelect("statisticsCalendarMonth", "월", statisticsCalendarMonth, months.map(value => ({ value, label: value })))}</div>
    <div id="statisticsCalendarContent">${renderStatisticsCalendar(data)}</div>
    <div class="statistics-calendar-legend"><span class="level-0">0건</span><span class="level-1">적음</span><span class="level-4">많음</span><span class="missing">미집계</span><span class="outside">기간 외</span></div>
  </section>`;
}

function renderPublisherCategoryDetail(data) {
  const row = (data.publisherCategories || []).find(item => item.publisher === statisticsCrossPublisher);
  return row ? `<p class="statistics-meta">${escapeHtml(row.publisher)} · ${statisticsNumber(row.total)}건</p>${renderStatisticsShareTable({
    id: "publisher-category", label: "카테고리", labelKey: "category", items: row.categories, isReady: true
  })}` : '<p class="statistics-status">해당 기간에 수집된 언론사 데이터가 없습니다.</p>';
}

function renderPublisherCategories(data) {
  const rows = data.publisherCategories || [];
  if (!rows.some(row => row.publisher === statisticsCrossPublisher)) statisticsCrossPublisher = rows[0]?.publisher || "";
  const ready = data.isReady && data.areCategoriesReady && data.arePublisherCategoriesReady;
  return `<section class="statistics-section" aria-labelledby="publisher-category-title">
    <div class="section-head compact"><h2 id="publisher-category-title">언론사 × 카테고리 분포</h2>${ready && rows.length ? renderStatisticsSelect("statisticsCrossPublisher", "언론사", statisticsCrossPublisher, rows.map(row => ({ value: row.publisher, label: row.publisher })), "publisher-category-control") : ""}</div>
    ${!ready ? `<p class="statistics-status" role="status">교차 분포 집계 중 · ${statisticsNumber(data.publisherCategoryCompletedDays || 0)} / ${statisticsNumber(data.expectedDays)}일 완료</p>`
      : !rows.length ? '<p class="statistics-status">해당 기간에 수집된 기사가 없습니다.</p>'
      : `<div class="statistics-table-scroll publisher-category-matrix" tabindex="0" aria-label="언론사별 카테고리 교차 분포"><table class="statistics-table publisher-category-matrix-table"><thead><tr><th scope="col">언론사</th><th scope="col">전체 기사</th>${data.categories.map(category => `<th scope="col">${escapeHtml(category.category)}</th>`).join("")}</tr></thead><tbody>${rows.map(row => `<tr><th scope="row">${escapeHtml(row.publisher)}</th><td>${statisticsNumber(row.total)}</td>${data.categories.map(category => {
        const cell = row.categories.find(item => item.category === category.category);
        return `<td style="background:rgba(22,133,113,${cell.share / 100 * 0.5})"><span>${statisticsNumber(cell.count)}</span><small>${statisticsNumber(cell.share, 1)}%</small></td>`;
      }).join("")}</tr>`).join("")}</tbody></table></div>
      <div id="publisherCategoryDetail" class="publisher-category-detail">${renderPublisherCategoryDetail(data)}</div>`}
    <p class="statistics-meta">비중은 각 언론사에서 수집된 기사 내 분야별 비율입니다. 제목·RSS 요약 기준의 자동 분류이며 언론사의 공식 분야 구분과 다를 수 있습니다.</p>
  </section>`;
}

function renderCollectionStatistics(data) {
  const content = document.querySelector("#statisticsContent");
  if (!content) return;
  if (!data.fromDate) {
    content.innerHTML = '<p class="statistics-empty" role="status">수집 통계를 준비 중입니다.</p>';
    return;
  }
  const ready = data.isReady;
  const change = Number.isFinite(data.changePercent) ? `${data.changePercent > 0 ? "+" : ""}${statisticsNumber(data.changePercent, 1)}%` : "--";
  const comparison = data.period === "all" ? "전체 기간은 비교 대상 없음"
    : data.previousTotal === 0 ? "직전 기간 0건, 증감률 계산 제외"
    : data.previousTotal == null ? "직전 기간 집계 부족" : `직전 ${data.expectedDays}일 ${statisticsNumber(data.previousTotal)}건`;
  const points = collectionTrendPoints(data);
  const updated = data.updatedAt ? new Date(data.updatedAt).toLocaleString("ko-KR", { timeZone: "Asia/Seoul" }) : "--";
  content.innerHTML = `
    <p class="statistics-meta">${escapeHtml(data.fromDate)} ~ ${escapeHtml(data.toDate)} · 최초 저장일 기준 · 한국 시간 · 오늘 제외</p>
    ${!ready ? `<p class="statistics-status" role="status">과거 통계 집계 중 · ${statisticsNumber(data.completedDays)} / ${statisticsNumber(data.expectedDays)}일 완료</p>` : ""}
    <div class="metric-grid statistics-metrics">
      ${renderMetricCard("일평균 수집", `${statisticsNumber(data.dailyAverage, 1)}건`, `${data.expectedDays}일 기준`, "완료된 날짜의 신규 저장 기사 수를 날짜 수로 나눈 값입니다. 수집 0건인 날짜도 포함합니다.", "compact-value")}
      ${renderMetricCard("기간 수집 기사", `${statisticsNumber(data.total)}건`, "신규 저장 기사 기준", "선택한 기간에 처음 저장된 기사 수입니다. 같은 기사의 RSS 재수집은 다시 세지 않습니다.", "compact-value")}
      ${renderMetricCard("직전 기간 증감", change, comparison, "동일한 길이의 직전 기간과 수집 기사 수를 비교합니다. 비교 집계가 없거나 직전 기간이 0건이면 증감률을 표시하지 않습니다.", "compact-value")}
      ${renderMetricCard("오늘 수집", `${statisticsNumber(data.todayCount)}건`, `${data.todayDate} · 수집 중`, "오늘 처음 저장된 기사 수입니다. 뉴스 검색의 오늘 발행 기사 수와 기준이 다릅니다.", "compact-value")}
    </div>
    <section class="statistics-section" aria-labelledby="collection-trend-title">
      <div class="section-head compact"><h2 id="collection-trend-title">${data.period === "all" ? "월별" : "일별"} 수집 추이</h2><span class="statistics-unit">단위: 건</span></div>
      ${renderStatisticsTrendChart(points, data.period, "수집 추이 차트")}
    </section>
    <div class="statistics-breakdowns">
      ${renderStatisticsShareSection({
        id: "publisher", title: "언론사 비중", label: "언론사", labelKey: "publisher", items: data.publishers, isReady: ready,
        pendingText: "기간 집계 완료 후 표시됩니다.", note: "Pulse Brief에 저장된 기사 내 비중이며 언론사 시장 점유율이 아닙니다."
      })}
      ${renderStatisticsShareSection({
        id: "category", title: "카테고리 비중", label: "카테고리", labelKey: "category", items: data.categories || [], isReady: ready && data.areCategoriesReady,
        pendingText: `카테고리 집계 중 · ${statisticsNumber(data.categoryCompletedDays || 0)} / ${statisticsNumber(data.expectedDays)}일 완료`,
        note: "제목·RSS 요약 자동 분류. 기사당 카테고리 하나를 반영하며 뉴스 검색의 이슈 단위 분류와 다를 수 있습니다."
      })}
    </div>
    ${renderCategoryComparison(data)}
    ${renderPublisherTrendSection(data)}
    <div class="statistics-breakdowns">
      ${renderStatisticsWeekdays(data)}
      ${renderStatisticsCalendarSection(data)}
    </div>
    ${renderPublisherCategories(data)}
    <p class="statistics-meta statistics-updated">집계 갱신: ${escapeHtml(updated)}</p>`;
}
