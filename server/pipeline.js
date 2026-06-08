import { config } from "./config.js";
import { enrichGroups } from "./ai.js";
import { groupSimilarArticles } from "./cluster.js";
import { ensureEmbeddings } from "./embedding.js";
import { fetchRssArticles } from "./rss.js";
import { saveArticles, saveGroups, upsertArticles } from "./storage.js";

export async function runPipeline() {
  const fetched = await fetchRssArticles(config.rssFeeds);
  const articles = await upsertArticles(fetched);
  const withEmbeddings = await ensureEmbeddings(articles);
  await saveArticles(withEmbeddings);

  const groups = groupSimilarArticles(withEmbeddings);
  const enrichedGroups = await enrichGroups(groups, withEmbeddings);
  await saveGroups(enrichedGroups);

  return {
    fetchedCount: fetched.length,
    articleCount: withEmbeddings.length,
    groupCount: enrichedGroups.length,
    updatedAt: new Date().toISOString(),
  };
}
