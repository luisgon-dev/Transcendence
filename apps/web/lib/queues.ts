const QUEUE_ID_LABELS: Record<number, string> = {
  400: "Normal Draft",
  420: "Ranked Solo/Duo",
  430: "Normal Blind",
  440: "Ranked Flex",
  450: "ARAM",
  490: "Quickplay",
  700: "Clash",
  900: "ARURF",
  1700: "Arena",
  1710: "Arena",
  1810: "Arena",
  1820: "Arena",
  1830: "Arena",
  1840: "Arena"
};

const QUEUE_TOKEN_LABELS: Record<string, string> = {
  ALL: "All Queues",
  RANKED_SOLO_DUO: "Ranked Solo/Duo",
  RANKED_SOLO_5X5: "Ranked Solo/Duo",
  RANKED_SOLO_5V5: "Ranked Solo/Duo",
  RANKED_FLEX: "Ranked Flex",
  RANKED_FLEX_SR: "Ranked Flex",
  RANKED_FLEX_5X5: "Ranked Flex",
  NORMAL_SR: "Normal (SR)",
  NORMAL_DRAFT: "Normal Draft",
  NORMAL_BLIND: "Normal Blind",
  QUICKPLAY: "Quickplay",
  ARAM: "ARAM",
  CLASH: "Clash",
  ARENA: "Arena",
  ROTATING: "Rotating Mode",
  CUSTOM: "Custom",
  BOT: "Co-op vs AI",
  OTHER: "Other"
};

function normalizeToken(input: string) {
  return input.trim().replace(/[\s-]+/g, "_").toUpperCase();
}

function titleCaseWords(input: string) {
  return input
    .toLowerCase()
    .split(/\s+/)
    .filter(Boolean)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(" ");
}

export function formatQueueLabel(queueType?: string | null, queueId?: number | null): string {
  if (typeof queueId === "number" && Number.isFinite(queueId)) {
    const byId = QUEUE_ID_LABELS[queueId];
    if (byId) return byId;
  }

  const raw = (queueType ?? "").trim();
  if (!raw) return "Unknown Queue";

  if (/^\d+$/.test(raw)) {
    const parsed = Number(raw);
    const byParsedId = QUEUE_ID_LABELS[parsed];
    return byParsedId ?? `Queue ${parsed}`;
  }

  const token = normalizeToken(raw);
  const byToken = QUEUE_TOKEN_LABELS[token];
  if (byToken) return byToken;

  if (raw.includes("_")) {
    return titleCaseWords(raw.replaceAll("_", " "));
  }

  return raw;
}
