import { config } from "./config.js";

function fallbackBrief(group, articles) {
  const first = articles[0];
  return {
    representativeTitle: group.seedTitle,
    summary: first?.summary || `${group.articleCount} related articles were detected in this issue group.`,
  };
}

function extractTextFromResponse(data) {
  if (typeof data.output_text === "string") return data.output_text;
  const chunks = data.output?.flatMap((item) => item.content || []) || [];
  return chunks.map((chunk) => chunk.text || "").join("\n").trim();
}

async function generateWithOpenAi(group, articles) {
  const sourceText = articles.slice(0, 8).map((article, index) => {
    return `${index + 1}. ${article.title}\nSource: ${article.source}\nSummary: ${article.summary || ""}`;
  }).join("\n\n");

  const response = await fetch("https://api.openai.com/v1/responses", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${config.openaiApiKey}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      model: config.summaryModel,
      input: [
        {
          role: "system",
          content: "You summarize clustered news articles for a Korean real-time issue briefing service. Return only compact JSON.",
        },
        {
          role: "user",
          content: `Write a Korean representative title and Korean summary for this cluster of similar news articles. Return compact JSON with only representativeTitle and summary.\n\n${sourceText}`,
        },
      ],
    }),
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(`OpenAI summary failed: ${response.status} ${body}`);
  }

  const text = extractTextFromResponse(await response.json());
  const jsonStart = text.indexOf("{");
  const jsonEnd = text.lastIndexOf("}");
  if (jsonStart === -1 || jsonEnd === -1) throw new Error("OpenAI summary did not return JSON");
  return JSON.parse(text.slice(jsonStart, jsonEnd + 1));
}

export async function enrichGroups(groups, articles) {
  const byId = new Map(articles.map((article) => [article.id, article]));
  const enriched = [];

  for (const group of groups) {
    const groupArticles = group.articleIds.map((id) => byId.get(id)).filter(Boolean);
    let brief;

    if (config.openaiApiKey) {
      try {
        brief = await generateWithOpenAi(group, groupArticles);
      } catch (error) {
        console.warn(`[ai] ${error.message}`);
        brief = fallbackBrief(group, groupArticles);
      }
    } else {
      brief = fallbackBrief(group, groupArticles);
    }

    enriched.push({
      ...group,
      representativeTitle: brief.representativeTitle || group.seedTitle,
      summary: brief.summary || group.seedSummary,
    });
  }

  return enriched;
}
