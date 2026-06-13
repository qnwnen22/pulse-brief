const summaryPath = "/tmp/pulsebrief-manual-summary.json";

function readText(path) {
  if (typeof cat === "function") return cat(path);
  if (typeof require === "function") return require("fs").readFileSync(path, "utf8");
  throw new Error("No file reader is available in mongosh.");
}

function toDateTimeOffset(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    throw new Error(`Invalid date value: ${value}`);
  }

  const ticks = BigInt(date.getTime()) * 10000n + 621355968000000000n;
  return {
    DateTime: date,
    Ticks: Long.fromString(ticks.toString()),
    Offset: 0
  };
}

function requireString(value, name) {
  if (typeof value !== "string" || !value.trim()) {
    throw new Error(`${name} is required.`);
  }

  return value.trim();
}

const summary = JSON.parse(readText(summaryPath));
summary.Date = requireString(summary.Date, "Date");
summary.Provider = requireString(summary.Provider || "manual", "Provider");
summary.GeneratedAt = toDateTimeOffset(summary.GeneratedAt || new Date().toISOString());
summary.Model = summary.Model || "";
summary.Categories = Array.isArray(summary.Categories) ? summary.Categories : [];
summary.TopIssues = Array.isArray(summary.TopIssues) ? summary.TopIssues : [];

const result = db.summaries.replaceOne(
  { Date: summary.Date },
  summary,
  { upsert: true }
);

print(JSON.stringify({
  ok: true,
  date: summary.Date,
  provider: summary.Provider,
  model: summary.Model,
  matchedCount: result.matchedCount,
  modifiedCount: result.modifiedCount,
  upsertedId: result.upsertedId || null,
  categoryCount: summary.Categories.length,
  topIssueCount: summary.TopIssues.length
}, null, 2));
