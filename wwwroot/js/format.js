function escapeHtml(value) {
  return String(value || "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function safeUrl(value) {
  try {
    const url = new URL(value);
    return ["http:", "https:"].includes(url.protocol) ? url.href : "";
  } catch {
    return "";
  }
}

function sourceNameFromUrl(value) {
  try {
    const host = new URL(value).hostname.replace(/^www\./, "").toLowerCase();
    if (host.includes("gonggam.korea.kr")) return "공감";
    if (host.includes("newsis.com")) return "뉴시스";
    if (host.includes("news.sbs.co.kr")) return "SBS 뉴스";
    if (host.includes("imbc.com")) return "MBC 뉴스";
    if (host.includes("news.jtbc.co.kr")) return "JTBC 뉴스";
    if (host.includes("korea.kr")) return "정책브리핑";
    if (host.includes("etnews.com")) return "전자신문";
    if (host.includes("yna.co.kr")) return "연합뉴스";
    if (host.includes("hani.co.kr")) return "한겨레";
    if (host.includes("bbc.com")) return "BBC";
    return host.split(".")[0].toUpperCase();
  } catch {
    return "";
  }
}

function displaySourceName(source, url) {
  const sourceName = String(source || "").trim();
  const publisher = sourceNameFromUrl(url);
  const genericSources = [
    "포토",
    "속보",
    "전체",
    "뉴스",
    "사회",
    "경제",
    "정치",
    "국제",
    "문화",
    "스포츠",
    "산업",
    "금융",
    "광장",
    "IT·바이오",
  ];

  if (!publisher || !sourceName) return sourceName || publisher || "출처";
  if (sourceName.includes(publisher) || publisher.includes(sourceName)) return sourceName;
  if (genericSources.includes(sourceName)) return `${publisher} · ${sourceName}`;
  return sourceName;
}

function compactText(value, maxLength = 260) {
  const text = String(value || "").replace(/\s+/g, " ").trim();
  if (text.length <= maxLength) return text;
  return `${text.slice(0, maxLength).replace(/[,\s.·-]+$/g, "")}...`;
}

function formatIssueTime(issue) {
  const date = getIssueDate(issue);
  const minutes = Math.max(1, Math.round((Date.now() - date.getTime()) / 60000));
  if (minutes < 60) return `${minutes}분 전`;
  if (minutes < 1440) return `${Math.floor(minutes / 60)}시간 전`;
  return date.toLocaleDateString("ko-KR", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  });
}
