const path = require("node:path");
const core = require("./summary-core.cjs");
const launcher = require("./run.cjs");

const USAGE_SOURCE = "codex_get_usage_limits";
const USAGE_MAX_AGE_MS = 5 * 60 * 1000;

function requireUsage(snapshot, now = new Date()) {
  core.requireThat(snapshot?.source === USAGE_SOURCE,
    "공식 Codex 사용량 출처가 확인되지 않습니다. 작업을 중단합니다.");
  const observedAt = snapshot?.observedAt;
  core.requireThat(typeof observedAt === "string"
      && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?(?:Z|[+-]\d{2}:\d{2})$/.test(observedAt),
    "Codex 사용량 관측 시각이 없거나 명확한 시간대가 없습니다. 작업을 중단합니다.");
  const observedMs = Date.parse(observedAt);
  core.requireThat(Number.isFinite(observedMs) && observedMs <= now.getTime(),
    "Codex 사용량 관측 시각이 유효하지 않거나 미래입니다. 작업을 중단합니다.");
  core.requireThat(now.getTime() - observedMs <= USAGE_MAX_AGE_MS,
    "Codex 사용량 데이터가 5분보다 오래되었습니다. 작업을 중단합니다.");
  const windows = snapshot?.windows;
  core.requireThat(windows && typeof windows === "object", "사용량 정보를 확인할 수 없습니다. 실행을 중단합니다.");
  const remaining = windows.weekly?.remainingPercent;
  core.requireThat(Number.isFinite(remaining) && remaining >= 0 && remaining <= 100,
    "Codex 주간 한도 잔여량을 확인할 수 없습니다. 실행을 중단합니다.");
  core.requireThat(remaining >= 20, "Codex 주간 한도 잔여량이 20% 미만입니다. 실행을 중단합니다.");
  return true;
}

async function runScheduledDaily({
  now = new Date(), config, storageRoot, readUsage,
  dependencies, log = console.log
}) {
  core.requireThat(typeof readUsage === "function", "사용량 조회 연결이 없습니다. 실행을 중단합니다.");
  core.requireThat(typeof storageRoot === "string" && path.isAbsolute(storageRoot), "복구 자료 저장 경로가 절대 경로여야 합니다.");
  for (const name of ["waitForCollectionGrace", "checkRemote", "exportArticles", "askCodex", "publishSummary", "verifyWebsite"])
    core.requireThat(typeof dependencies?.[name] === "function", `클라우드 연결에 ${name} 작업이 없습니다.`);

  const date = core.yesterday(now);
  const notBefore = new Date(`${date}T15:20:00Z`); // 00:20 Korea time after the target publication day.
  const graceEndedAt = await dependencies.waitForCollectionGrace(date, notBefore);
  core.requireThat(graceEndedAt instanceof Date && Number.isFinite(graceEndedAt.getTime())
      && graceEndedAt.getTime() >= notBefore.getTime(),
    "수집 유예 시각 전에 실행을 계속할 수 없습니다.");

  const usage = await readUsage();
  requireUsage(usage, graceEndedAt);

  const directory = path.join(storageRoot, "manual-summary-runs", date);
  const canonicalFile = path.join(storageRoot, "manual-summaries", `${date}.json`);
  return launcher.run({
    date, directory, canonicalFile, config, log,
    // The selected cloud model adapter is responsible for its own readiness.
    dependencies: { ...dependencies, checkLogin: async () => {} }
  });
}

module.exports = { requireUsage, runScheduledDaily };
