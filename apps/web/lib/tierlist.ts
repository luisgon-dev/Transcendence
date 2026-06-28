import type { components } from "@transcendence/api-client";

export type UITierGrade = "S" | "A" | "B" | "C" | "D";
export type UITierMovement = "NEW" | "UP" | "DOWN" | "SAME";

export type UITierListEntry = {
  championId: number;
  role: string;
  tier: UITierGrade;
  compositeScore: number;
  winRate: number;
  pickRate: number;
  banRate: number;
  games: number;
  movement: UITierMovement;
  previousTier: UITierGrade | null;
  strengthScore: number;
  contestedScore: number;
};

// The champion's grade for a specific (role, scope) — the SAME grade the tier list shows. Decoded from the
// profile endpoint's ChampionGradeDto so the champion detail hero renders one consistent grade.
export type UIChampionGrade = {
  tier: UITierGrade;
  strengthScore: number;
  winRate: number;
  pickRate: number;
  banRate: number;
  contestedScore: number;
  games: number;
  roleBaseline: number;
  isLowSample: boolean;
  movement: UITierMovement;
  previousTier: UITierGrade | null;
  role: string;
};

export type TierListChampionMap = Record<string, { name: string; id: string; title?: string }>;
export type TierListFocusTier = "ALL" | UITierGrade;
export type TierListSummary = {
  visibleCount: number;
  totalGames: number;
  averageWinRate: number | null;
  topWinRate: number | null;
  tierCounts: Record<UITierGrade, number>;
};

type ApiTierListEntry = components["schemas"]["TierListEntry"];

// Mirrors the tier-list response with the fields we read off it. `computedAtUtc`
// is the ISO-8601 UTC timestamp (or null) of when the precomputed analytics for
// the patch were last refreshed; it powers the "Updated N min ago" indicator.
export type TierListResponseLike = components["schemas"]["TierListResponse"] & {
  computedAtUtc?: string | null;
};

export const TIER_ORDER: UITierGrade[] = ["S", "A", "B", "C", "D"];

export function decodeTierGrade(
  value: components["schemas"]["TierGrade"] | string | null | undefined
): UITierGrade | null {
  const normalized = typeof value === "string" ? value.toUpperCase() : value;

  switch (normalized) {
    case 0:
    case "S":
      return "S";
    case 1:
    case "A":
      return "A";
    case 2:
    case "B":
      return "B";
    case 3:
    case "C":
      return "C";
    case 4:
    case "D":
      return "D";
    default:
      return null;
  }
}

export function decodeTierMovement(
  value: components["schemas"]["TierMovement"] | string | null | undefined
): UITierMovement {
  const normalized = typeof value === "string" ? value.toUpperCase() : value;

  switch (normalized) {
    case 0:
    case "NEW":
      return "NEW";
    case 1:
    case "UP":
      return "UP";
    case 2:
    case "DOWN":
      return "DOWN";
    case 3:
    case "SAME":
      return "SAME";
    default:
      return "SAME";
  }
}

function asFiniteNumber(value: unknown, fallback = 0): number {
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}

function asNonNegativeInteger(value: unknown, fallback = 0): number {
  const n = asFiniteNumber(value, fallback);
  return n > 0 ? Math.floor(n) : 0;
}

export function normalizeTierListEntries(
  rawEntries: ApiTierListEntry[] | null | undefined
): UITierListEntry[] {
  if (!rawEntries || rawEntries.length === 0) return [];

  const entries: UITierListEntry[] = [];

  for (const raw of rawEntries) {
    const tier = decodeTierGrade(raw.tier);
    if (!tier) continue;

    const championId = asFiniteNumber(raw.championId, Number.NaN);
    if (!Number.isInteger(championId) || championId <= 0) continue;

    entries.push({
      championId,
      role: typeof raw.role === "string" && raw.role ? raw.role : "ALL",
      tier,
      compositeScore: asFiniteNumber(raw.compositeScore, 0),
      winRate: asFiniteNumber(raw.winRate, 0),
      pickRate: asFiniteNumber(raw.pickRate, 0),
      banRate: asFiniteNumber(raw.banRate, 0),
      games: asNonNegativeInteger(raw.games, 0),
      movement: decodeTierMovement(raw.movement),
      previousTier: decodeTierGrade(raw.previousTier),
      strengthScore: asFiniteNumber(raw.strengthScore, 0),
      contestedScore: asFiniteNumber(raw.contestedScore, 0)
    });
  }

  return entries;
}

type ApiChampionGrade = components["schemas"]["ChampionGradeDto"];

/**
 * Decodes the profile endpoint's grade payload into the UI shape. Returns null when there is no grade
 * (champion not graded in scope) or the tier won't decode — callers render an "unrated" affordance.
 */
export function decodeGrade(raw: ApiChampionGrade | null | undefined): UIChampionGrade | null {
  if (!raw) return null;
  const tier = decodeTierGrade(raw.tier);
  if (!tier) return null;

  return {
    tier,
    strengthScore: asFiniteNumber(raw.strengthScore, 0),
    winRate: asFiniteNumber(raw.winRate, 0),
    pickRate: asFiniteNumber(raw.pickRate, 0),
    banRate: asFiniteNumber(raw.banRate, 0),
    contestedScore: asFiniteNumber(raw.contestedScore, 0),
    games: asNonNegativeInteger(raw.games, 0),
    roleBaseline: asFiniteNumber(raw.roleBaseline, 0),
    isLowSample: raw.isLowSample === true,
    movement: decodeTierMovement(raw.movement),
    previousTier: decodeTierGrade(raw.previousTier),
    role: typeof raw.role === "string" && raw.role ? raw.role : "ALL"
  };
}

/**
 * Formats a strength delta (signed fraction vs the role baseline, e.g. 0.032) as a signed percentage
 * string like "+3.2%". This is the value the tiers are cut on.
 */
export function formatStrengthDelta(strengthScore: number, decimals = 1): string {
  const pct = (Number.isFinite(strengthScore) ? strengthScore : 0) * 100;
  const sign = pct > 0 ? "+" : pct < 0 ? "−" : "";
  return `${sign}${Math.abs(pct).toFixed(decimals)}%`;
}

export function movementLabel(movement: UITierMovement): string {
  switch (movement) {
    case "UP":
      return "Up";
    case "DOWN":
      return "Down";
    case "NEW":
      return "New";
    case "SAME":
    default:
      return "Same";
  }
}

export function movementClass(movement: UITierMovement): string {
  switch (movement) {
    case "UP":
      return "text-wr-high";
    case "DOWN":
      return "text-wr-low";
    case "NEW":
      return "text-primary";
    case "SAME":
    default:
      return "text-fg/70";
  }
}

export function movementIcon(movement: UITierMovement): string {
  switch (movement) {
    case "UP":
      return "\u25B2";
    case "DOWN":
      return "\u25BC";
    case "NEW":
      return "\u2605";
    case "SAME":
    default:
      return "\u2013";
  }
}

export function tierColorClass(tier: UITierGrade): string {
  switch (tier) {
    case "S":
      return "text-tier-s";
    case "A":
      return "text-tier-a";
    case "B":
      return "text-tier-b";
    case "C":
      return "text-tier-c";
    case "D":
      return "text-tier-d";
  }
}

export function tierBgClass(tier: UITierGrade): string {
  switch (tier) {
    case "S":
      return "bg-tier-s/15";
    case "A":
      return "bg-tier-a/15";
    case "B":
      return "bg-tier-b/15";
    case "C":
      return "bg-tier-c/15";
    case "D":
      return "bg-tier-d/15";
  }
}

export function tierBorderClass(tier: UITierGrade): string {
  switch (tier) {
    case "S":
      return "border-tier-s/40";
    case "A":
      return "border-tier-a/40";
    case "B":
      return "border-tier-b/40";
    case "C":
      return "border-tier-c/40";
    case "D":
      return "border-tier-d/40";
  }
}

export function filterTierListEntries<T extends UITierListEntry>(
  entries: readonly T[],
  champions: TierListChampionMap,
  options?: {
    query?: string | null;
    focusTier?: TierListFocusTier;
  }
): T[] {
  const query = options?.query?.trim().toLowerCase() ?? "";
  const focusTier = options?.focusTier ?? "ALL";

  return entries.filter((entry) => {
    if (focusTier !== "ALL" && entry.tier !== focusTier) return false;
    if (!query) return true;

    const champion = champions[String(entry.championId)];
    const championName = champion?.name.toLowerCase() ?? "";
    const championSlug = champion?.id.toLowerCase() ?? "";
    const championTitle = champion?.title?.toLowerCase() ?? "";
    const championId = String(entry.championId);

    return (
      championName.includes(query) ||
      championSlug.includes(query) ||
      championTitle.includes(query) ||
      championId.includes(query)
    );
  });
}

export function summarizeTierListEntries(entries: UITierListEntry[]): TierListSummary {
  const tierCounts: Record<UITierGrade, number> = {
    S: 0,
    A: 0,
    B: 0,
    C: 0,
    D: 0
  };

  if (entries.length === 0) {
    return {
      visibleCount: 0,
      totalGames: 0,
      averageWinRate: null,
      topWinRate: null,
      tierCounts
    };
  }

  let totalGames = 0;
  let totalWinRate = 0;
  let topWinRate = Number.NEGATIVE_INFINITY;

  for (const entry of entries) {
    tierCounts[entry.tier] += 1;
    totalGames += entry.games;
    totalWinRate += entry.winRate;
    topWinRate = Math.max(topWinRate, entry.winRate);
  }

  return {
    visibleCount: entries.length,
    totalGames,
    averageWinRate: totalWinRate / entries.length,
    topWinRate,
    tierCounts
  };
}

