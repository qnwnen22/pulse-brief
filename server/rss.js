import { createHash } from "node:crypto";
import { cleanText } from "./text.js";

function textBetween(xml, tag) {
  const match = xml.match(new RegExp(`<${tag}[^>]*>([\\s\\S]*?)<\\/${tag}>`, "i"));
  return match ? cleanText(stripCdata(match[1]).trim()) : "";
}

function stripCdata(value) {
  return value.replace(/^<!\[CDATA\[/, "").replace(/\]\]>$/, "");
}

function idForArticle(article) {
  return createHash("sha256").update(article.url || `${article.title}:${article.publishedAt}`).digest("hex");
}

function toIsoDate(value) {
  const date = value ? new Date(value) : new Date();
  return Number.isNaN(date.getTime()) ? new Date().toISOString() : date.toISOString();
}

function parseRssItems(xml, feedUrl) {
  const source = textBetween(xml, "title") || new URL(feedUrl).hostname;
  const itemBlocks = [...xml.matchAll(/<item[\s\S]*?<\/item>/gi)].map((match) => match[0]);
  const entryBlocks = [...xml.matchAll(/<entry[\s\S]*?<\/entry>/gi)].map((match) => match[0]);
  const blocks = itemBlocks.length ? itemBlocks : entryBlocks;

  return blocks.map((block) => {
    const atomLinkMatch = block.match(/<link[^>]*href=["']([^"']+)["'][^>]*>/i);
    const link = textBetween(block, "link") || atomLinkMatch?.[1] || "";
    const title = textBetween(block, "title");
    const summary = textBetween(block, "description") || textBetween(block, "summary") || textBetween(block, "content");
    const publishedAt = textBetween(block, "pubDate") || textBetween(block, "published") || textBetween(block, "updated");
    const article = {
      id: "",
      title,
      url: link,
      source,
      feedUrl,
      summary,
      publishedAt: toIsoDate(publishedAt),
      firstSeenAt: new Date().toISOString(),
    };
    article.id = idForArticle(article);
    return article;
  }).filter((article) => article.title && article.url);
}

export async function fetchRssArticles(feedUrls) {
  const results = [];

  for (const feedUrl of feedUrls) {
    try {
      const response = await fetch(feedUrl, {
        headers: {
          "User-Agent": "PulseBrief/0.1 (+local development)",
        },
      });
      if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
      const xml = await response.text();
      results.push(...parseRssItems(xml, feedUrl));
    } catch (error) {
      console.warn(`[rss] ${feedUrl}: ${error.message}`);
    }
  }

  return results;
}
