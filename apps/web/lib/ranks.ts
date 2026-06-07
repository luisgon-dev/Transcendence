const RANK_DISPLAY_LABELS: Record<string, string> = {
  ALL: "All Ranks",
  EMERALD_PLUS: "Emerald+",
  IRON: "Iron",
  BRONZE: "Bronze",
  SILVER: "Silver",
  GOLD: "Gold",
  PLATINUM: "Platinum",
  EMERALD: "Emerald",
  DIAMOND: "Diamond",
  MASTER: "Master",
  GRANDMASTER: "Grandmaster",
  CHALLENGER: "Challenger"
};

export const RANK_TIER_FILTERS = [
  "all",
  "EMERALD_PLUS",
  "IRON",
  "BRONZE",
  "SILVER",
  "GOLD",
  "PLATINUM",
  "EMERALD",
  "DIAMOND",
  "MASTER",
  "GRANDMASTER",
  "CHALLENGER"
] as const;

export const DEFAULT_TIERLIST_RANK_TIER = "EMERALD_PLUS";

function normalizeRankToken(value: string) {
  return value.trim().replaceAll("+", "_PLUS").toUpperCase();
}

export function normalizeRankTierParam(value: string | null | undefined): string | null {
  if (!value) return null;
  const normalized = normalizeRankToken(value);
  if (normalized === "ALL") return null;
  return RANK_DISPLAY_LABELS[normalized] ? normalized : null;
}

/**
 * Resolve a `rankTier` URL param for pages that default to Emerald+ (the
 * champion detail page). Distinguishes an *absent* param (→ Emerald+ default)
 * from an *explicit* "all" selection (→ null = all ranks):
 * - undefined / empty  → DEFAULT_TIERLIST_RANK_TIER ("EMERALD_PLUS")
 * - "all"              → null (explicit all-ranks)
 * - a valid tier       → that tier
 *
 * For "All Ranks" to remain selectable, the rank/role controls on these pages
 * must emit an explicit `?rankTier=all` (see RankFilterDropdown.emitAllRank).
 */
export function resolveDefaultedRankTier(value: string | null | undefined): string | null {
  if (value === undefined || value === null || value.trim().length === 0) {
    return DEFAULT_TIERLIST_RANK_TIER;
  }
  return normalizeRankTierParam(value);
}

export function rankTierDisplayLabel(value: string | null | undefined): string {
  if (!value) return RANK_DISPLAY_LABELS.ALL;
  const normalized = normalizeRankToken(value);
  return RANK_DISPLAY_LABELS[normalized] ?? normalized;
}

const RANK_COLOR_CLASS: Record<string, string> = {
  IRON: "text-rank-iron",
  BRONZE: "text-rank-bronze",
  SILVER: "text-rank-silver",
  GOLD: "text-rank-gold",
  PLATINUM: "text-rank-platinum",
  EMERALD: "text-rank-emerald",
  DIAMOND: "text-rank-diamond",
  MASTER: "text-rank-master",
  GRANDMASTER: "text-rank-grandmaster",
  CHALLENGER: "text-rank-challenger"
};

export function rankTierColorClass(tier: string | null | undefined): string {
  if (!tier) return "text-muted";
  return RANK_COLOR_CLASS[tier.toUpperCase()] ?? "text-muted";
}

export function rankEmblemUrl(tier: string | null | undefined): string | null {
  if (!tier) return null;
  const normalized = normalizeRankToken(tier);
  if (normalized === "ALL" || normalized === "UNRANKED" || normalized === "EMERALD_PLUS") {
    return null;
  }

  const slug = normalized.toLowerCase();
  return `https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-static-assets/global/default/ranked-emblem/emblem-${slug}.png`;
}
