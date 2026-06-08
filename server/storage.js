import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const rootDir = dirname(dirname(fileURLToPath(import.meta.url)));
const dataDir = join(rootDir, "data");
const articlesPath = join(dataDir, "articles.json");
const groupsPath = join(dataDir, "groups.json");

async function ensureDataDir() {
  await mkdir(dataDir, { recursive: true });
}

async function readJson(path, fallback) {
  try {
    const text = await readFile(path, "utf8");
    return JSON.parse(text);
  } catch (error) {
    if (error.code === "ENOENT") return fallback;
    throw error;
  }
}

async function writeJson(path, value) {
  await ensureDataDir();
  await writeFile(path, JSON.stringify(value, null, 2), "utf8");
}

export async function readArticles() {
  return readJson(articlesPath, []);
}

export async function saveArticles(articles) {
  await writeJson(articlesPath, articles);
}

export async function readGroups() {
  return readJson(groupsPath, []);
}

export async function saveGroups(groups) {
  await writeJson(groupsPath, groups);
}

export async function upsertArticles(incoming) {
  const current = await readArticles();
  const byId = new Map(current.map((article) => [article.id, article]));

  incoming.forEach((article) => {
    const previous = byId.get(article.id);
    byId.set(article.id, {
      ...previous,
      ...article,
      embedding: previous?.embedding || article.embedding || null,
      firstSeenAt: previous?.firstSeenAt || article.firstSeenAt,
      updatedAt: new Date().toISOString(),
    });
  });

  const merged = [...byId.values()].sort((a, b) => {
    return new Date(b.publishedAt || b.firstSeenAt) - new Date(a.publishedAt || a.firstSeenAt);
  });

  await saveArticles(merged);
  return merged;
}
