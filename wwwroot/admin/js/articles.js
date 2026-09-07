function articleQueryString() {
  const params = new URLSearchParams();
  const filters = {
    query: $("#articleSearch").value.trim(),
    category: $("#articleCategoryFilter").value,
    source: $("#articleSourceFilter").value,
    contentStatus: $("#articleStatusFilter").value,
    excluded: $("#articleExcludedFilter").value,
    page: state.articlePage,
    pageSize: state.articlePageSize,
  };

  Object.entries(filters).forEach(([key, value]) => {
    if (value !== "" && value !== null && value !== undefined) params.set(key, value);
  });
  return params.toString();
}

async function loadArticles() {
  const result = await api(`/api/admin/articles?${articleQueryString()}`);
  state.categories = result.categories || [];
  state.sources = result.sources || [];
  populateSelect("#articleCategoryFilter", state.categories, "전체 카테고리");
  populateSelect("#articleSourceFilter", state.sources, "전체 출처");
  renderArticles(result);
}

function populateSelect(selector, values, defaultLabel) {
  const select = $(selector);
  const currentValue = select.value;
  select.innerHTML = `<option value="">${escapeHtml(defaultLabel)}</option>`
    + values.map((value) => `<option value="${escapeHtml(value)}">${escapeHtml(value)}</option>`).join("");
  select.value = values.includes(currentValue) ? currentValue : "";
}

function renderArticles(result) {
  const tbody = $("#articleTableBody");
  tbody.innerHTML = (result.items || []).length
    ? result.items.map(renderArticleRow).join("")
    : '<tr><td colspan="5">조건에 맞는 기사가 없습니다.</td></tr>';

  $("#articlePagination").innerHTML = `
    <span>${formatNumber(result.totalCount)}건 · ${result.page}/${result.pageCount}페이지</span>
    <button type="button" data-page="${result.page - 1}" ${result.page <= 1 ? "disabled" : ""}>이전</button>
    <button type="button" data-page="${result.page + 1}" ${result.page >= result.pageCount ? "disabled" : ""}>다음</button>
  `;
}

function renderArticleRow(article) {
  const status = article.contentFetchStatus || "pending";
  const categoryOptions = state.categories.map((category) => {
    const selected = category === article.category ? "selected" : "";
    return `<option value="${escapeHtml(category)}" ${selected}>${escapeHtml(category)}</option>`;
  }).join("");

  return `
    <tr data-article-id="${escapeHtml(article.id)}">
      <td>
        <div class="article-title">
          <a href="${escapeHtml(article.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(article.title)}</a>
          <span>${escapeHtml(article.source)}${article.author ? ` · ${escapeHtml(article.author)}` : ""}</span>
          <span>${escapeHtml(article.summaryPreview || article.contentPreview || "")}</span>
          ${article.isExcluded ? '<span class="badge excluded">제외됨</span>' : ""}
        </div>
      </td>
      <td>
        <select class="row-category-select" data-id="${escapeHtml(article.id)}">
          ${categoryOptions}
        </select>
      </td>
      <td><span class="badge ${escapeHtml(status)}">${escapeHtml(status)}</span></td>
      <td>${formatDate(article.publishedAt)}</td>
      <td>
        <div class="row-actions">
          <button type="button" data-action="detail" data-id="${escapeHtml(article.id)}">상세</button>
          <button type="button" data-action="toggle-excluded" data-id="${escapeHtml(article.id)}" data-excluded="${article.isExcluded ? "false" : "true"}">
            ${article.isExcluded ? "복원" : "제외"}
          </button>
        </div>
      </td>
    </tr>
  `;
}

async function patchArticle(id, body) {
  const result = await api(`/api/admin/articles/${encodeURIComponent(id)}`, {
    method: "PATCH",
    body,
  });
  setStatus("기사 정보가 저장되었습니다.", "success");
  await loadArticles();
  return result;
}

async function openArticleDetail(id) {
  const article = await api(`/api/admin/articles/${encodeURIComponent(id)}`);
  $("#dialogTitle").textContent = article.title || "기사 상세";
  $("#articleDetailBody").innerHTML = `
    <div class="detail-grid" data-detail-id="${escapeHtml(article.id)}">
      <div class="detail-field full">
        <label>제목</label>
        <input id="detailTitle" value="${escapeHtml(article.title)}" />
      </div>
      <div class="detail-field">
        <label>출처</label>
        <input id="detailSource" value="${escapeHtml(article.source)}" />
      </div>
      <div class="detail-field">
        <label>작성자</label>
        <input id="detailAuthor" value="${escapeHtml(article.author)}" />
      </div>
      <div class="detail-field">
        <label>카테고리</label>
        <select id="detailCategory">
          ${state.categories.map((category) => `<option value="${escapeHtml(category)}" ${category === article.category ? "selected" : ""}>${escapeHtml(category)}</option>`).join("")}
        </select>
      </div>
      <label class="checkbox-label">
        <input id="detailExcluded" type="checkbox" ${article.isExcluded ? "checked" : ""} />
        공개 화면과 요약 후보에서 제외
      </label>
      <div class="detail-field full">
        <label>RSS 대표 내용</label>
        <textarea id="detailSummary">${escapeHtml(article.summary)}</textarea>
      </div>
      <div class="detail-field full">
        <label>수집 본문</label>
        <pre>${escapeHtml(article.content || "수집된 본문이 없습니다.")}</pre>
      </div>
      <div class="detail-field full">
        <label>원문 URL</label>
        <pre>${escapeHtml(article.url)}</pre>
      </div>
      <div class="row-actions">
        <button id="detailSaveButton" type="button">저장</button>
        <button id="detailCloseButton" class="secondary-button" type="button">닫기</button>
      </div>
    </div>
  `;
  $("#articleDialog").showModal();
}

async function saveArticleDetail() {
  const wrapper = $("#articleDetailBody [data-detail-id]");
  if (!wrapper) return;

  await patchArticle(wrapper.dataset.detailId, {
    title: $("#detailTitle").value,
    source: $("#detailSource").value,
    author: $("#detailAuthor").value,
    category: $("#detailCategory").value,
    summary: $("#detailSummary").value,
    isExcluded: $("#detailExcluded").checked,
  });
  $("#articleDialog").close();
}
