function showLogin() {
  adminShell.hidden = true;
  loginView.hidden = false;
  $("#adminTokenInput")?.focus();
}

function showAdmin() {
  loginView.hidden = true;
  adminShell.hidden = false;
}

async function checkSession() {
  try {
    const session = await api("/api/admin/session");
    if (!session.authenticated) {
      showLogin();
      return;
    }

    state.csrfToken = session.csrfToken || "";
    showAdmin();
    await loadActivePanel();
  } catch {
    showLogin();
  }
}

async function login(event) {
  event.preventDefault();
  const button = event.submitter;
  const message = $("#loginMessage");
  setBusy(button, true);
  message.textContent = "";
  message.classList.remove("error");

  try {
    const token = $("#adminTokenInput").value;
    const result = await api("/api/admin/login", {
      method: "POST",
      body: { token },
    });
    state.csrfToken = result.csrfToken || "";
    $("#adminTokenInput").value = "";
    showAdmin();
    await loadActivePanel();
  } catch (error) {
    message.textContent = `로그인 실패: ${error.message}`;
    message.classList.add("error");
  } finally {
    setBusy(button, false);
  }
}

async function logout() {
  try {
    await api("/api/admin/logout", { method: "POST" });
  } finally {
    state.csrfToken = "";
    showLogin();
  }
}
