import { createHash } from "node:crypto";
import { config } from "./config.js";

const VECTOR_SIZE = 128;

function normalize(vector) {
  const magnitude = Math.sqrt(vector.reduce((sum, value) => sum + value * value, 0)) || 1;
  return vector.map((value) => value / magnitude);
}

function localEmbedding(text) {
  const vector = Array.from({ length: VECTOR_SIZE }, () => 0);
  const tokens = text.toLowerCase().match(/[a-z0-9가-힣]{2,}/g) || [];

  tokens.forEach((token) => {
    const hash = createHash("sha256").update(token).digest();
    for (let i = 0; i < 4; i += 1) {
      const index = hash[i] % VECTOR_SIZE;
      vector[index] += hash[i + 4] > 127 ? 1 : -1;
    }
  });

  return normalize(vector);
}

async function openAiEmbeddings(inputs) {
  const response = await fetch("https://api.openai.com/v1/embeddings", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${config.openaiApiKey}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      model: config.embeddingModel,
      input: inputs,
    }),
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(`OpenAI embeddings failed: ${response.status} ${body}`);
  }

  const data = await response.json();
  return data.data.map((item) => item.embedding);
}

export async function ensureEmbeddings(articles) {
  const missing = articles.filter((article) => !article.embedding);
  if (!missing.length) return articles;

  const inputs = missing.map((article) => `${article.title}\n${article.summary || ""}`);
  let embeddings;

  if (config.openaiApiKey) {
    try {
      embeddings = await openAiEmbeddings(inputs);
    } catch (error) {
      console.warn(`[embedding] ${error.message}`);
      embeddings = inputs.map(localEmbedding);
    }
  } else {
    embeddings = inputs.map(localEmbedding);
  }

  missing.forEach((article, index) => {
    article.embedding = embeddings[index];
  });

  return articles;
}
