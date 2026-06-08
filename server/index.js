import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { dirname, extname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";
import { config } from "./config.js";
import { runPipeline } from "./pipeline.js";
import { readArticles, readGroups } from "./storage.js";
import { cleanText } from "./text.js";

const rootDir = normalize(join(dirname(fileURLToPath(import.meta.url)), ".."));
const mimeTypes = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "application/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
};

function sendJson(response, statusCode, payload) {
  response.writeHead(statusCode, { "Content-Type": "application/json; charset=utf-8" });
  response.end(JSON.stringify(payload, null, 2));
}

async function readRequestBody(request) {
  const chunks = [];
  for await (const chunk of request) chunks.push(chunk);
  return Buffer.concat(chunks).toString("utf8");
}

async function sendStatic(request, response) {
  const url = new URL(request.url, `http://${request.headers.host}`);
  const requestedPath = url.pathname === "/" ? "/index.html" : url.pathname;
  const filePath = normalize(join(rootDir, decodeURIComponent(requestedPath)));

  if (!filePath.startsWith(rootDir)) {
    sendJson(response, 403, { error: "Forbidden" });
    return;
  }

  try {
    await stat(filePath);
    response.writeHead(200, {
      "Content-Type": mimeTypes[extname(filePath)] || "application/octet-stream",
    });
    createReadStream(filePath).pipe(response);
  } catch {
    sendJson(response, 404, { error: "Not found" });
  }
}

function groupsToBriefs(groups, articles) {
  const byId = new Map(articles.map((article) => [article.id, article]));
  return groups.map((group) => {
    const groupArticles = group.articleIds.map((id) => byId.get(id)).filter(Boolean);
    const latestArticle = groupArticles[0];
    const minutes = latestArticle
      ? Math.max(1, Math.round((Date.now() - new Date(latestArticle.publishedAt).getTime()) / 60000))
      : 1;

    return {
      title: cleanText(group.representativeTitle),
      category: group.category,
      source: cleanText(group.sources.slice(0, 2).join(", ")),
      minutes,
      impact: group.score,
      heat: group.score >= 80 ? "hot" : "normal",
      summary: cleanText(group.summary),
      keywords: cleanText(group.representativeTitle).split(/\s+/).filter((word) => word.length >= 2).slice(0, 4),
      articleCount: group.articleCount,
      articleIds: group.articleIds,
      relatedLinks: groupArticles.slice(0, 8).map((article) => ({
        title: cleanText(article.title),
        source: cleanText(article.source),
        url: article.url,
      })),
    };
  });
}

async function handleApi(request, response) {
  const url = new URL(request.url, `http://${request.headers.host}`);

  if (request.method === "GET" && url.pathname === "/api/health") {
    sendJson(response, 200, {
      ok: true,
      hasOpenAiKey: Boolean(config.openaiApiKey),
      rssFeedCount: config.rssFeeds.length,
    });
    return;
  }

  if (request.method === "GET" && url.pathname === "/api/articles") {
    sendJson(response, 200, await readArticles());
    return;
  }

  if (request.method === "GET" && url.pathname === "/api/groups") {
    sendJson(response, 200, await readGroups());
    return;
  }

  if (request.method === "GET" && url.pathname === "/api/briefs") {
    const [groups, articles] = await Promise.all([readGroups(), readArticles()]);
    sendJson(response, 200, groupsToBriefs(groups, articles));
    return;
  }

  if (request.method === "POST" && url.pathname === "/api/refresh") {
    await readRequestBody(request);
    const result = await runPipeline();
    sendJson(response, 200, result);
    return;
  }

  sendJson(response, 404, { error: "Unknown API route" });
}

const server = globalThis.Bun
  ? null
  : (await import("node:http")).createServer(async (request, response) => {
      try {
        if (request.url.startsWith("/api/")) {
          await handleApi(request, response);
        } else {
          await sendStatic(request, response);
        }
      } catch (error) {
        console.error(error);
        sendJson(response, 500, { error: error.message });
      }
    });

if (server) {
  server.listen(config.port, () => {
    console.log(`Pulse Brief server running at http://localhost:${config.port}`);
  });
}
