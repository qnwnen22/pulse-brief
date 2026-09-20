const path = require("node:path");
const core = require("./summary-core.cjs");
const launcher = require("./run.cjs");
const { latestWeek, fileStem } = require("./periods.cjs");
const weekly = require("./weekly.cjs");

async function runWorkflow({ mode = "all", now = new Date(), config, root = launcher.root, checkOnly = false, log = console.log, dependencies = {} }) {
  core.requireThat(["all", "daily", "weekly"].includes(mode), "지원하지 않는 실행 모드입니다.");
  const date = core.yesterday(now), week = latestWeek(now);
  const api = { run: launcher.run, readRemote: launcher.readRemote, ...dependencies };
  const results = [];
  function options(key) {
    const stem = fileStem(key);
    return { date: key, directory: path.join(root, "data/manual-summary-runs", stem), config,
      canonicalFile: path.join(root, "manual-summaries", stem + ".json"), checkOnly, log };
  }
  async function daily(target) {
    const result = await api.run(options(target));
    log(`${target}: ${result.status}`);
    results.push(result);
    return result;
  }
  log(`전날 대상: ${date} / 완료 주간: ${week.start}~${week.end}`);
  if (mode !== "weekly") await daily(date);
  if (mode !== "daily") {
    const opts = options(week.key);
    const result = await api.run({ ...opts, generateSummary: async () => {
      const summaries = await weekly.gatherDaily({ week, directory: opts.directory, config, ensureDaily: daily, readRemote: api.readRemote, log });
      log("7일의 일간 요약을 PC에서 합산합니다. 주간 합산은 Codex/API를 호출하지 않습니다.");
      return weekly.buildWeekly(week, summaries);
    } });
    results.push(result);
    log(`${week.key}: ${result.status}`);
  }
  const result = { status: checkOnly ? "checked" : "completed", date, week: week.key, mode, results };
  core.writeJson(path.join(root, "data/summary-launcher/last-result.json"), result);
  return result;
}
async function main(args) {
  const mode = args[0] || "all";
  const config = launcher.loadConfig(path.join(launcher.root, "data/summary-launcher/config.json"));
  console.log(JSON.stringify(await runWorkflow({ mode, config, checkOnly: args.includes("--check-only") })));
}
module.exports = { runWorkflow };
if (require.main === module) main(process.argv.slice(2)).catch(error => { console.error(`중단: ${error.message}`); process.exitCode = 1; });
