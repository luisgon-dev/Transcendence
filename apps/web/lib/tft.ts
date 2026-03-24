export type TftStaticEntity = {
  apiName: string;
  name: string;
  description?: string | null;
  icon?: string | null;
};

export type TftUnitSummary = {
  characterId: string;
  name?: string | null;
  rarity: number;
  tier: number;
  items: number[];
};

export type TftTraitSummary = {
  name: string;
  numUnits: number;
  tierCurrent: number;
  style?: number | null;
};

export type TftCompListItem = {
  compSlug: string;
  name: string;
  setNumber?: number | null;
  setCoreName?: string | null;
  patch?: string | null;
  region: string;
  rankTier: string;
  avgPlacement: number;
  top4Rate: number;
  winRate: number;
  pickRate: number;
  sampleSize: number;
  trend: string;
  units: TftUnitSummary[];
  traits: TftTraitSummary[];
  augments: string[];
};

export type TftCompDetail = {
  summary: TftCompListItem;
  coreItems: TftStaticEntity[];
  coreAugments: TftStaticEntity[];
};

export type TftRank = {
  queueType: string;
  tier: string;
  rankNumber: string;
  leaguePoints: number;
  wins: number;
  losses: number;
  updatedAtUtc: string;
};

export type TftRecentMatchSummary = {
  matchId: string;
  matchDate: number;
  placement: number;
  level: number;
  lastRound: number;
  playersEliminated: number;
  totalDamageToPlayers: number;
  setNumber?: number | null;
  setCoreName?: string | null;
  patch?: string | null;
  augments: string[];
  units: TftUnitSummary[];
  traits: TftTraitSummary[];
};

export type TftSummonerProfile = {
  summonerId: string;
  puuid: string;
  gameName: string;
  tagLine: string;
  profileIconId: number;
  summonerLevel: number;
  platformRegion: string;
  region: string;
  updatedAtUtc: string;
  ranks: TftRank[];
  recentMatches: TftRecentMatchSummary[];
};

export type TftPagedMatches = {
  items: TftRecentMatchSummary[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type TftAcceptedResponse = {
  message?: string;
  poll?: string;
  retryAfterSeconds?: number;
};

export const TFT_RANK_OPTIONS = [
  "EMERALD_PLUS",
  "CHALLENGER",
  "GRANDMASTER",
  "MASTER",
  "DIAMOND",
  "EMERALD",
  "ALL"
] as const;

export const TFT_REGION_OPTIONS = [
  "ALL",
  "NA1",
  "EUW1",
  "EUN1",
  "KR"
] as const;

// ---------------------------------------------------------------------------
// Match detail types
// ---------------------------------------------------------------------------

export type TftMatchParticipant = {
  puuid: string;
  gameName?: string | null;
  tagLine?: string | null;
  placement: number;
  level: number;
  lastRound: number;
  playersEliminated: number;
  goldLeft: number;
  totalDamageToPlayers: number;
  timeEliminatedSeconds: number;
  win: boolean;
  augments: string[];
  units: TftUnitSummary[];
  traits: TftTraitSummary[];
};

export type TftMatchDetail = {
  matchId: string;
  matchDate: number;
  durationSeconds: number;
  patch?: string | null;
  setNumber?: number | null;
  setCoreName?: string | null;
  platformRegion?: string | null;
  participants: TftMatchParticipant[];
};

// ---------------------------------------------------------------------------
// Icon helpers
// ---------------------------------------------------------------------------

const CDRAGON_TFT_BASE = "https://raw.communitydragon.org/latest/game/assets/";

/** Convert a CommunityDragon icon path to a full URL. */
export function tftIconUrl(iconPath: string | null | undefined): string | null {
  if (!iconPath) return null;
  const trimmed = iconPath.trim();
  if (!trimmed) return null;
  if (/^https?:\/\//i.test(trimmed)) return trimmed;

  const withoutPrefix = trimmed
    .replace(/^ASSETS\//i, "")
    .replace(/\.tex$/i, ".png");
  return `${CDRAGON_TFT_BASE}${withoutPrefix.toLowerCase()}`;
}

// ---------------------------------------------------------------------------
// Placement helpers
// ---------------------------------------------------------------------------

export function placementColorClass(placement: number): string {
  if (placement === 1) return "text-tier-s";
  if (placement <= 4) return "text-success";
  return "text-danger";
}

export function placementBgClass(placement: number): string {
  if (placement === 1) return "border-tier-s/50 bg-tier-s/10";
  if (placement <= 4) return "border-success/40 bg-success/8";
  return "border-danger/40 bg-danger/8";
}

export function placementBarClass(placement: number): string {
  if (placement === 1) return "bg-tier-s";
  if (placement <= 4) return "bg-success";
  return "bg-danger";
}

export function formatPlacement(placement: number): string {
  if (placement === 1) return "1st";
  if (placement === 2) return "2nd";
  if (placement === 3) return "3rd";
  return `${placement}th`;
}

// ---------------------------------------------------------------------------
// Comp tier helpers
// ---------------------------------------------------------------------------

export function compTierLabel(avgPlacement: number): string {
  if (avgPlacement <= 3.5) return "S";
  if (avgPlacement <= 4.0) return "A";
  if (avgPlacement <= 4.5) return "B";
  if (avgPlacement <= 5.0) return "C";
  return "D";
}

export function compTierColorClass(tier: string): string {
  const map: Record<string, string> = {
    S: "border-tier-s/60 bg-tier-s/15 text-tier-s",
    A: "border-tier-a/60 bg-tier-a/15 text-tier-a",
    B: "border-tier-b/60 bg-tier-b/15 text-tier-b",
    C: "border-tier-c/60 bg-tier-c/15 text-tier-c",
    D: "border-tier-d/60 bg-tier-d/15 text-tier-d"
  };
  return map[tier] ?? "border-border/60 bg-surface/50 text-fg/70";
}

// ---------------------------------------------------------------------------
// Formatting helpers
// ---------------------------------------------------------------------------

export function formatTftPercent(value: number, decimals = 1) {
  return `${(value * 100).toFixed(decimals)}%`;
}

export function formatTftTime(epochMs: number) {
  return new Date(epochMs).toLocaleString();
}
