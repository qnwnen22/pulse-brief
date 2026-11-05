const indexName = "PublishedAt_-1_FirstSeenAt_-1";
const sort = { PublishedAt: -1, FirstSeenAt: -1 };
const sampleLimit = 100;
const mode = globalThis.recentArticlesIndexMode ?? "verify";

if (!["verify", "prepare"].includes(mode)) throw new Error("Invalid index maintenance mode.");

let index = db.articles.getIndexes().find(item => item.name === indexName);
if (!index && mode === "prepare") {
  const result = db.runCommand({
    createIndexes: "articles",
    indexes: [{ key: sort, name: indexName }],
    maxTimeMS: 180000
  });
  if (result.ok !== 1) throw new Error("Recent article index creation failed.");
  index = db.articles.getIndexes().find(item => item.name === indexName);
}
if (!index || JSON.stringify(index.key) !== JSON.stringify(sort) || index.hidden || index.sparse || index.partialFilterExpression) {
  throw new Error("The expected visible, full compound index is not ready.");
}

function* stages(node) {
  if (!node || typeof node !== "object") return;
  if (typeof node.stage === "string") yield node;
  for (const child of Object.values(node)) yield* stages(child);
}

function verifyPlan(verbosity, limit, projection) {
  const find = { find: "articles", filter: {}, sort, limit, hint: indexName };
  if (projection) find.projection = projection;
  const result = db.runCommand({ explain: find, verbosity, maxTimeMS: 5000 });
  if (result.ok !== 1) throw new Error("Recent article explain failed.");
  const plan = [...stages(result.queryPlanner.winningPlan)];
  const usesIndex = plan.some(stage => stage.stage === "IXSCAN" && stage.indexName === indexName);
  const scansOrSorts = plan.some(stage => ["COLLSCAN", "SORT"].includes(stage.stage));
  if (!usesIndex || scansOrSorts) {
    throw new Error("The query plan does not use the compound index for ordering.");
  }
  return result;
}

verifyPlan("queryPlanner", 5000);
const sample = verifyPlan("executionStats", sampleLimit, { _id: 1, PublishedAt: 1, FirstSeenAt: 1 }).executionStats;
if (!sample.executionSuccess || sample.nReturned === 0 || sample.totalDocsExamined > sampleLimit || sample.totalKeysExamined > sampleLimit) {
  throw new Error("Bounded recent article verification failed.");
}
print(JSON.stringify({ indexName, mode, fullLimitPlan: "IXSCAN", sampleLimit,
  returned: sample.nReturned, examinedDocuments: sample.totalDocsExamined,
  examinedKeys: sample.totalKeysExamined, elapsedMs: sample.executionTimeMillis }));
