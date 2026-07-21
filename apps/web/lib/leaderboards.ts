export type LeaderboardEntry = {
  position: number;
  summonerId: string;
  gameName: string;
  tagLine: string;
  profileIconId: number;
  tier: string | null;
  division: string | null;
  leaguePoints: number | null;
  rankedWins: number;
  rankedLosses: number;
  championGames: number | null;
  championWins: number | null;
  championWinRate: number | null;
  championKda: number | null;
  updatedAtUtc: string | null;
};

export type LeaderboardResponse = {
  region: string;
  queue: string;
  championId: number | null;
  role: string | null;
  generatedAtUtc: string;
  entries: LeaderboardEntry[];
};

export type LeaderboardFilters = {
  region: string;
  queue: "solo" | "flex";
  championId: number | null;
  role: string | null;
};

const VALID_ROLES = new Set(["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"]);

export function normalizeLeaderboardFilters(input: {
  region?: string;
  queue?: string;
  championId?: string;
  role?: string;
}): LeaderboardFilters {
  const championId = Number(input.championId);
  const role = input.role?.trim().toUpperCase() ?? "";
  return {
    region: input.region?.trim().toLowerCase() || "na",
    queue: input.queue?.trim().toLowerCase() === "flex" ? "flex" : "solo",
    championId: Number.isInteger(championId) && championId > 0 ? championId : null,
    role: Number.isInteger(championId) && championId > 0 && VALID_ROLES.has(role) ? role : null
  };
}

export function leaderboardSearchParams(filters: LeaderboardFilters): URLSearchParams {
  const query = new URLSearchParams({ region: filters.region, queue: filters.queue });
  if (filters.championId) query.set("championId", String(filters.championId));
  if (filters.championId && filters.role) query.set("role", filters.role);
  return query;
}
