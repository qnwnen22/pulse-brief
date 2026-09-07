// The launcher supplies a validated summary. Existing dates are never updated.
const summary = request.summary;
if (!summary || summary.Date !== request.date || summary.Provider !== "manual") throw new Error("Invalid manual summary");
const index = db.summaries.getIndexes().find(item => item.unique === true && Object.keys(item.key).length === 1 && item.key.Date === 1 && !item.partialFilterExpression);
if (!index) throw new Error("Unique Date index missing; refusing publication without duplicate protection");
const generatedAt = new Date(summary.GeneratedAt);
if (Number.isNaN(generatedAt.getTime())) throw new Error("Invalid GeneratedAt");
const ticks = BigInt(generatedAt.getTime()) * 10000n + 621355968000000000n;
const document = { ...summary, GeneratedAt: { DateTime: generatedAt, Ticks: Long.fromString(ticks.toString()), Offset: 0 } };
const projection = Object.fromEntries(Object.keys(document).map(key => [key, 1]));
projection._id = 0;
function readExisting() {
  return db.summaries.find({ Date: request.date }, projection).hint(index.name).limit(1).maxTimeMS(3000).toArray()[0];
}
let existing = readExisting();
let inserted = false;
if (!existing) {
  try {
    const result = db.summaries.insertOne(document, { writeConcern: { w: 1, j: true, wtimeout: 5000 } });
    if (!result.acknowledged) throw new Error("Publication was not acknowledged");
    inserted = true;
  } catch (error) {
    // A concurrent publisher can win after the check; the unique index is authoritative.
    if (error.code !== 11000) throw error;
  }
  existing = readExisting();
}
if (!existing) throw new Error("Published summary could not be read back");
const matches = Object.keys(summary).every(key => key === "GeneratedAt"
  ? new Date(existing.GeneratedAt?.DateTime).getTime() === generatedAt.getTime()
  : JSON.stringify(existing[key]) === JSON.stringify(summary[key]));
if (inserted && !matches) throw new Error("Published summary read-back mismatch");
print(JSON.stringify({ type: inserted ? "published" : "existing", date: request.date, matches }));
