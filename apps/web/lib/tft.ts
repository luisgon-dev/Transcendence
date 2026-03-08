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

export function formatTftPercent(value: number, decimals = 1) {
  return `${(value * 100).toFixed(decimals)}%`;
}

export function formatTftTime(epochMs: number) {
  return new Date(epochMs).toLocaleString();
}
