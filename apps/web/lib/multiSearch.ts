import { parseRiotIdInput, type RiotId } from "@/lib/riotid";

export type ParsedLobby = {
  summoners: RiotId[];
  rejected: string[];
  truncated: boolean;
};

const LOBBY_SUFFIX = /\s+(?:has\s+)?joined\s+the\s+lobby[.!]?$/i;

export function parseLobbyText(raw: string, limit = 5): ParsedLobby {
  const entries = raw
    .split(/[\n,;]+/)
    .map((entry) => entry.trim().replace(LOBBY_SUFFIX, "").trim())
    .filter(Boolean);
  const summoners: RiotId[] = [];
  const rejected: string[] = [];
  const seen = new Set<string>();
  let truncated = false;

  for (const entry of entries) {
    const riotId = parseRiotIdInput(entry);
    if (!riotId) {
      rejected.push(entry);
      continue;
    }

    const key = `${riotId.gameName}#${riotId.tagLine}`.toLocaleUpperCase();
    if (seen.has(key)) continue;
    seen.add(key);

    if (summoners.length >= limit) {
      truncated = true;
      continue;
    }
    summoners.push(riotId);
  }

  return { summoners, rejected, truncated };
}
