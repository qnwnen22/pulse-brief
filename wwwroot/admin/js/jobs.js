async function runJob(job, button) {
  setBusy(button, true);
  setStatus("작업을 실행 중입니다.", "");
  $("#jobResult").textContent = "";

  try {
    let result;
    if (job === "refresh") {
      result = await api("/api/admin/refresh", { method: "POST" });
    } else if (job === "content") {
      const limit = Number($("#contentLimitInput").value || 200);
      result = await api(`/api/admin/fetch-missing-content?limit=${encodeURIComponent(limit)}`, { method: "POST" });
    } else if (job === "images") {
      const limit = Number($("#imageLimitInput").value || 200);
      result = await api(`/api/admin/fetch-missing-images?limit=${encodeURIComponent(limit)}`, { method: "POST" });
    } else if (job === "daily") {
      result = await api("/api/admin/summaries/daily/regenerate", {
        method: "POST",
        body: { date: $("#dailyDateInput").value || null },
      });
    } else if (job === "weekly") {
      result = await api("/api/admin/summaries/weekly/regenerate", {
        method: "POST",
        body: { endDate: $("#weeklyDateInput").value || null },
      });
    }

    $("#jobResult").textContent = JSON.stringify(result, null, 2);
    setStatus("작업이 완료되었습니다.", "success");
    await loadDashboard();
  } catch (error) {
    setStatus(`작업 실패: ${error.message}`, "error");
  } finally {
    setBusy(button, false);
  }
}
