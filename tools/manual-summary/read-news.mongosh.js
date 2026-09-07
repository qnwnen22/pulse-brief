// The launcher supplies a validated request. This script never writes to MongoDB.
const target = request.date;
if (!/^\d{4}-\d{2}-\d{2}$/.test(target)) throw new Error("Invalid date");
if (!["check", "export"].includes(request.mode)) throw new Error("Invalid mode");
const summaryIndex = db.summaries.getIndexes().find(index => Object.keys(index.key)[0] === "Date");
if (!summaryIndex) throw new Error("Summary Date index missing; refusing a collection scan");
const existing = db.summaries.find({ Date: target }, { _id: 0, Date: 1, Provider: 1, Model: 1 })
  .hint(summaryIndex.name).limit(1).maxTimeMS(3000).toArray()[0];
if (existing) {
  print(JSON.stringify({ type: "existing", date: target, summary: existing }));
} else if (request.mode === "check") {
  print(JSON.stringify({ type: "missing", date: target }));
} else {
  const maxArticles = request.maxArticles;
  if (!Number.isInteger(maxArticles) || maxArticles < 1 || maxArticles > 50000) throw new Error("Invalid article ceiling");
  const dateIndex = db.articles.getIndexes().find(index => Object.keys(index.key)[0] === "PublishedAt.DateTime");
  if (!dateIndex) throw new Error("PublishedAt.DateTime index missing; refusing a collection scan");
  const start = new Date(target + "T00:00:00+09:00");
  const end = new Date(start.getTime() + 86400000);
  const filter = { "PublishedAt.DateTime": { $gte: start, $lt: end }, IsExcluded: { $ne: true } };
  const projection = { _id: 1, Title: 1, Url: 1, Source: 1, Author: 1, Summary: 1, Content: 1, PublishedAt: 1, FirstSeenAt: 1 };
  const cursor = db.articles.find(filter, projection).hint(dateIndex.name)
    .sort({ "PublishedAt.DateTime": -1 }).batchSize(200).limit(maxArticles + 1).maxTimeMS(15000);
  let count = 0;
  print(JSON.stringify({ type: "begin", date: target, start: start.toISOString(), end: end.toISOString() }));
  try {
    while (cursor.hasNext()) {
      const article = cursor.next();
      if (++count > maxArticles) throw new Error("Article ceiling exceeded; export is incomplete and must not be summarized");
      print(JSON.stringify({ type: "article", article: {
        Id: String(article._id), Title: article.Title || "", Url: article.Url || "", Source: article.Source || "",
        Author: article.Author || "", Summary: article.Summary || "", Content: article.Content || "",
        PublishedAt: new Date(article.PublishedAt.DateTime).toISOString(),
        FirstSeenAt: article.FirstSeenAt?.DateTime ? new Date(article.FirstSeenAt.DateTime).toISOString() : null
      } }));
    }
    print(JSON.stringify({ type: "done", date: target, count, complete: true }));
  } finally {
    cursor.close();
  }
}
