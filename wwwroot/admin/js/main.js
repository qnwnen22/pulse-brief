document.addEventListener("click", async (event) => {
  const navButton = event.target.closest(".nav-button[data-panel]");
  if (navButton) {
    switchPanel(navButton.dataset.panel);
    return;
  }

  const actionButton = event.target.closest("button[data-action]");
  if (actionButton) {
    const id = actionButton.dataset.id;
    if (actionButton.dataset.action === "detail") await openArticleDetail(id);
    if (actionButton.dataset.action === "toggle-excluded") {
      await patchArticle(id, { isExcluded: actionButton.dataset.excluded === "true" });
    }
    return;
  }

  const pageButton = event.target.closest("#articlePagination button[data-page]");
  if (pageButton) {
    state.articlePage = Number(pageButton.dataset.page);
    await loadArticles();
    return;
  }

  const feedButton = event.target.closest("button[data-feed-action]");
  if (feedButton) {
    const url = feedButton.dataset.url;
    if (feedButton.dataset.feedAction === "toggle") {
      await api("/api/admin/rss-feeds", {
        method: "PATCH",
        body: { url, isActive: feedButton.dataset.active === "true" },
      });
      setStatus("RSS 피드 상태를 변경했습니다.", "success");
      await loadFeeds();
    }
    if (feedButton.dataset.feedAction === "remove" && confirm("이 RSS 피드를 삭제할까요?")) {
      await api("/api/admin/rss-feeds/remove", {
        method: "POST",
        body: { url },
      });
      setStatus("RSS 피드를 삭제했습니다.", "success");
      await loadFeeds();
    }
    return;
  }

  const jobButton = event.target.closest(".job-button[data-job]");
  if (jobButton) {
    await runJob(jobButton.dataset.job, jobButton);
    return;
  }

  if (event.target.id === "reloadButton") {
    setBusy(event.target, true);
    try {
      await loadActivePanel();
      setStatus("관리자 데이터를 다시 불러왔습니다.", "success");
    } finally {
      setBusy(event.target, false);
    }
  }

  if (event.target.id === "detailSaveButton") await saveArticleDetail();
  if (event.target.id === "detailCloseButton") $("#articleDialog").close();
});

document.addEventListener("change", async (event) => {
  const categorySelect = event.target.closest(".row-category-select");
  if (!categorySelect) return;

  await patchArticle(categorySelect.dataset.id, { category: categorySelect.value });
});

$("#loginForm").addEventListener("submit", login);
$("#logoutButton").addEventListener("click", logout);
$("#articleFilters").addEventListener("submit", async (event) => {
  event.preventDefault();
  state.articlePage = 1;
  await loadArticles();
});
$("#articleFilterReset").addEventListener("click", async () => {
  $("#articleSearch").value = "";
  $("#articleCategoryFilter").value = "";
  $("#articleSourceFilter").value = "";
  $("#articleStatusFilter").value = "";
  $("#articleExcludedFilter").value = "";
  state.articlePage = 1;
  await loadArticles();
});
$("#rssAddForm").addEventListener("submit", addFeed);

checkSession();
