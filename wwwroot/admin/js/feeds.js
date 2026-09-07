async function loadFeeds() {
  const result = await api("/api/admin/rss-feeds");
  $("#rssSummary").textContent = `전체 ${formatNumber(result.totalCount)}개 · 활성 ${formatNumber(result.activeCount)}개 · 비활성 ${formatNumber(result.inactiveCount)}개`;
  $("#rssTableBody").innerHTML = (result.feeds || []).map((feed) => `
    <tr>
      <td><strong>${escapeHtml(feed.publisher || "알 수 없음")}</strong></td>
      <td><a href="${escapeHtml(feed.url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(feed.url)}</a></td>
      <td>
        ${feed.guideUrl
          ? `<a class="source-guide-link" href="${escapeHtml(feed.guideUrl)}" target="_blank" rel="noopener noreferrer">안내 페이지</a>`
          : '<span class="muted-text">-</span>'}
      </td>
      <td><span class="badge ${feed.isActive ? "success" : "pending"}">${feed.isActive ? "활성" : "비활성"}</span></td>
      <td>
        <div class="row-actions">
          <button type="button" data-feed-action="toggle" data-url="${escapeHtml(feed.url)}" data-active="${feed.isActive ? "false" : "true"}">
            ${feed.isActive ? "비활성화" : "활성화"}
          </button>
          <button type="button" data-feed-action="remove" data-url="${escapeHtml(feed.url)}">삭제</button>
        </div>
      </td>
    </tr>
  `).join("");
}

async function addFeed(event) {
  event.preventDefault();
  await api("/api/admin/rss-feeds", {
    method: "POST",
    body: {
      url: $("#rssUrlInput").value,
      isActive: $("#rssActiveInput").checked,
    },
  });
  $("#rssUrlInput").value = "";
  $("#rssActiveInput").checked = true;
  setStatus("RSS 피드를 추가했습니다.", "success");
  await loadFeeds();
}
