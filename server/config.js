export const config = {
  port: Number(process.env.PORT || 4000),
  openaiApiKey: process.env.OPENAI_API_KEY || "",
  summaryModel: process.env.OPENAI_SUMMARY_MODEL || "gpt-5.3",
  embeddingModel: process.env.OPENAI_EMBEDDING_MODEL || "text-embedding-3-small",
  groupSimilarityThreshold: Number(process.env.GROUP_SIMILARITY_THRESHOLD || 0.78),
  rssFeeds: [
    "https://www.yna.co.kr/rss/news.xml",
    "https://www.hani.co.kr/rss/",
    "https://rss.etnews.com/Section902.xml",
    "https://feeds.bbci.co.uk/news/world/rss.xml",
  ],
};
