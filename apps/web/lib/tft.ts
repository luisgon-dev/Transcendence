export type TftStaticEntity = {
  apiName: string;
  name: string;
  description?: string | null;
  icon?: string | null;
  composition?: string[] | null;
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
  regionScopeUsed?: string;
  rankScopeUsed?: string;
  avgPlacement: number;
  metaScore?: number;
  top4Rate: number;
  winRate: number;
  bot4Rate?: number;
  pickRate: number;
  placementDelta?: number;
  contestRate?: number;
  forceabilityIndex?: number;
  consistencyScore?: number;
  effectiveSampleSize?: number;
  sampleSize: number;
  minimumRecommendedSampleSize?: number;
  patchAgeHours?: number;
  isEarlyPatchWindow?: boolean;
  patchPhase?: string | null;
  isProvisional?: boolean;
  isEmerging?: boolean;
  sampleStatus?: string;
  trend: string;
  lastUpdatedUtc?: string;
  confidence?: TftCompConfidence | null;
  units: TftUnitSummary[];
  traits: TftTraitSummary[];
  augments: string[];
};

export type TftConfidenceInterval = {
  lower: number;
  upper: number;
};

export type TftCompConfidence = {
  top4: TftConfidenceInterval;
  win: TftConfidenceInterval;
  bot4: TftConfidenceInterval;
};

export type TftPlacementBucket = {
  placement: number;
  count: number;
  rate: number;
};

export type TftCompDetail = {
  summary: TftCompListItem;
  placementDistribution?: TftPlacementBucket[];
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
  compSlug?: string | null;
  compName?: string | null;
  augments: string[];
  units: TftUnitSummary[];
  traits: TftTraitSummary[];
};

export type TftSummonerCompInsight = {
  compSlug: string;
  compName: string;
  games: number;
  avgPlacement: number;
  top4Rate: number;
  winRate: number;
};

export type TftSummonerInsights = {
  recentGames: number;
  avgPlacement: number;
  top4Rate: number;
  winRate: number;
  mostPlayedComps: TftSummonerCompInsight[];
  bestComps: TftSummonerCompInsight[];
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
  insights?: TftSummonerInsights | null;
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

export type TftLabelMap = Record<string, string>;
export type TftEntityMap = Record<string, TftStaticEntity>;

function titleCaseWord(word: string): string {
  if (!word) return word;
  if (word.includes("-")) return word.split("-").map(titleCaseWord).join("-");
  if (/^[A-Z0-9]+$/.test(word)) return word;
  return `${word[0]?.toUpperCase() ?? ""}${word.slice(1).toLowerCase()}`;
}

function prettifyTftToken(value: string) {
  let text = value.trim();
  if (!text) return text;

  text = text.replace(/^TFT\d+_/i, "").replace(/^TFT_/i, "");
  text = text.replace(/_/g, " ");
  text = text.replace(/([a-z0-9])([A-Z])/g, "$1 $2");
  text = text.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2");
  text = text.replace(/([A-Za-z])(\d)/g, "$1 $2");
  text = text.replace(/(\d)([A-Za-z])/g, "$1 $2");
  text = text.replace(/\bTeamup\b/gi, "Team-Up");
  text = text.replace(/\b([A-Za-z]+)\s+Trait\b/g, "$1");
  text = text.replace(/\b([A-Za-z]+)\s+Unique\b/g, "$1");
  text = text.replace(/\bDr Mundo\b/g, "Dr. Mundo");
  text = text.replace(/\s+/g, " ").trim();

  return text
    .split(" ")
    .map(titleCaseWord)
    .join(" ");
}

function lookupTftLabel(rawValue: string, labelMap?: TftLabelMap) {
  if (!labelMap) return null;
  return labelMap[rawValue] ?? labelMap[rawValue.trim()] ?? null;
}

export function buildTftLabelMap(entities: TftStaticEntity[]): TftLabelMap {
  return Object.fromEntries(
    entities.flatMap((entity) => {
      const entries: Array<[string, string]> = [];
      if (entity.apiName) entries.push([entity.apiName, entity.name]);
      if (entity.name) entries.push([entity.name, entity.name]);
      return entries;
    })
  );
}

export function buildTftEntityMap(entities: TftStaticEntity[]): TftEntityMap {
  return Object.fromEntries(
    entities.flatMap((entity) => {
      const entries: Array<[string, TftStaticEntity]> = [];
      if (entity.apiName) entries.push([entity.apiName, entity]);
      if (entity.name) entries.push([entity.name, entity]);
      return entries;
    })
  );
}

export function isTftCraftableItem(
  entity: Pick<TftStaticEntity, "composition">
): entity is Pick<TftStaticEntity, "composition"> & { composition: string[] } {
  return Array.isArray(entity.composition) && entity.composition.length === 2;
}

export function getTftCompositionEntities(
  entity: Pick<TftStaticEntity, "composition">,
  entityMap: TftEntityMap
) {
  if (!isTftCraftableItem(entity)) return [];
  const composition = entity.composition;
  return composition
    .map((apiName) => entityMap[apiName])
    .filter((value): value is TftStaticEntity => Boolean(value));
}

export function formatTftLabel(value: string | null | undefined, labelMap?: TftLabelMap) {
  if (!value) return "";

  const trimmed = value.trim();
  if (!trimmed) return "";

  return lookupTftLabel(trimmed, labelMap) ?? prettifyTftToken(trimmed);
}

export function formatTftUnitLabel(
  unit: Pick<TftUnitSummary, "characterId" | "name">,
  labelMap?: TftLabelMap
) {
  return formatTftLabel(unit.name ?? unit.characterId, labelMap) || formatTftLabel(unit.characterId, labelMap);
}

export function formatTftCompName(
  name: string | null | undefined,
  {
    traitLabels,
    unitLabels
  }: {
    traitLabels?: TftLabelMap;
    unitLabels?: TftLabelMap;
  } = {}
) {
  if (!name) return "";

  const formatted = name
    .split("/")
    .map((segment) => segment.trim())
    .filter(Boolean)
    .map((segment) => lookupTftLabel(segment, traitLabels) ?? lookupTftLabel(segment, unitLabels) ?? prettifyTftToken(segment))
    .filter(Boolean);

  return formatted.join(" / ");
}

function hasResolvableDisplayName(name: string | null | undefined) {
  if (!name) return false;
  const trimmed = name.trim();
  if (!trimmed) return false;
  if (trimmed.includes("@")) return false;
  if (trimmed.includes("<")) return false;
  return true;
}

function cleanTftEntityDisplayName(value: string) {
  return value.replace(/^(Item|Trait|Augment)\s+/i, "").trim();
}

export function formatTftEntityName(entity: Pick<TftStaticEntity, "apiName" | "name">) {
  if (hasResolvableDisplayName(entity.name)) return cleanTftEntityDisplayName(entity.name!.trim());
  return cleanTftEntityDisplayName(formatTftLabel(entity.apiName));
}

export function formatTftDescription(value: string | null | undefined) {
  if (!value) return null;

  const sanitized = value
    .replace(/<br\s*\/?>/gi, " ")
    .replace(/<\/?expandRow[^>]*>/gi, " ")
    .replace(/<\/?tftrules[^>]*>/gi, " ")
    .replace(/<\/?[^>]+>/g, " ")
    .replace(/%i:[^%\s]+%/gi, " ")
    .replace(/@[A-Za-z0-9._:*+-]+@%?/g, " ")
    .replace(/\(\s*\)/g, " ")
    .replace(/\s{2,}/g, " ")
    .replace(/\s+([,.;:!?])/g, "$1")
    .trim();

  if (!sanitized || sanitized.includes("@")) return null;
  return sanitized;
}

export function tftIconObjectClass(iconPath: string | null | undefined) {
  if (iconPath && /\/characters\//i.test(iconPath)) return "object-cover";
  return "object-contain";
}

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
  compSlug?: string | null;
  compName?: string | null;
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

export function formatTftSigned(value: number, decimals = 2) {
  const sign = value > 0 ? "+" : "";
  return `${sign}${value.toFixed(decimals)}`;
}

export function getTftMetaScore(comp: TftCompListItem) {
  if (typeof comp.metaScore === "number") return comp.metaScore;
  return comp.top4Rate * 100;
}

export function getTftBot4Rate(comp: TftCompListItem) {
  if (typeof comp.bot4Rate === "number") return comp.bot4Rate;
  return Math.max(0, 1 - comp.top4Rate);
}

export function getTftEffectiveSampleSize(comp: TftCompListItem) {
  if (typeof comp.effectiveSampleSize === "number") return comp.effectiveSampleSize;
  return comp.sampleSize;
}
