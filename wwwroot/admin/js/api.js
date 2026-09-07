async function api(path, options = {}) {
  const method = options.method || "GET";
  const headers = { ...(options.headers || {}) };
  const request = {
    method,
    credentials: "include",
    headers,
  };

  if (method !== "GET" && state.csrfToken) {
    headers["X-CSRF-Token"] = state.csrfToken;
  }

  if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
    request.body = JSON.stringify(options.body);
  }

  const response = await fetch(path, request);
  const contentType = response.headers.get("content-type") || "";
  const payload = contentType.includes("application/json") ? await response.json() : await response.text();

  if (!response.ok) {
    if (response.status === 401) showLogin();
    const message = typeof payload === "string" ? payload : payload.error || "request_failed";
    throw new Error(message);
  }

  return payload;
}
