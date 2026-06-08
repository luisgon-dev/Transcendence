import { formatQueueLabel } from "@/lib/queues";
import { rankTierColorClass } from "@/lib/ranks";
import type { RuneTree } from "@/lib/staticData";

export type DataAgeMetadata = {
  fetchedAt?: string;
  ageDescription?: string;
  [k: string]: unknown;
};

export type RankInfo = {
  tier: string;
  division: string;
  leaguePoints: number;
  wins: number;
  losses: number;
};

export type ProfileOverviewStats = {
  totalMatches: number;
  wins: number;
  losses: number;
  winRate: number;
  avgKills: number;
  avgDeaths: number;
  avgAssists: number;
  kdaRatio: number;
  avgCsPerMin: number;
  avgVisionScore: number;
  avgDamageToChamps: number;
};

export type ProfileChampionStat = {
  championId: number;
  championName: string;
  games: number;
  wins: number;
  losses: number;
  winRate: number;
  kdaRatio: number;
};

export type PlayedWithEntry = {
  summonerId: string;
  gameName: string;
  tagLine: string;
  gamesTogether: number;
  sameTeamGames: number;
  sameTeamWins: number;
};

export type ChampionMasteryEntry = {
  championId: number;
  championName: string;
  championLevel: number;
  championPoints: number;
  lastPlayTime: number;
  chestGranted: boolean;
  tokensEarned: number;
};

export type SummonerProfileResponse = {
  summonerId?: string;
  puuid: string;
  gameName: string;
  tagLine: string;
  summonerLevel: number;
  profileIconId: number;
  soloRank?: RankInfo | null;
  flexRank?: RankInfo | null;
  overviewStats?: ProfileOverviewStats | null;
  topChampions?: ProfileChampionStat[] | null;
  frequentlyPlayedWith?: PlayedWithEntry[] | null;
  topMastery?: ChampionMasteryEntry[] | null;
  profileAge: DataAgeMetadata;
  rankAge: DataAgeMetadata;
  statsAge?: DataAgeMetadata | null;
};

export type AcceptedResponse = {
  message?: string;
  retryAfterSeconds?: number;
  poll?: string;
};

export type ApiErrorResponse = {
  message?: string;
  code?: string;
  requestId?: string;
  detail?: string;
};

export type ChampionStatic = {
  version: string;
  champions: Record<string, { id: string; name: string }>;
};

export type ItemStatic = {
  version: string;
  items: Record<string, { name: string; plaintext?: string }>;
};

export type SpellStatic = {
  version: string;
  spells: Record<string, { id: string; name: string }>;
};

export type RuneStatic = {
  version: string;
  runeById: Record<string, { name: string; icon: string }>;
  styleById: Record<string, { name: string; icon: string }>;
  runeSortById: Record<string, number>;
  /** Full structured trees (style → slots → runes) for rendering a complete rune page. */
  trees: RuneTree[];
};

export type MatchRuneDetail = {
  primaryStyleId: number;
  subStyleId: number;
  primarySelections: number[];
  subSelections: number[];
  statShards: number[];
};

export type MatchSummary = {
  matchId: string;
  matchDate: number;
  durationSeconds: number;
  queueId: number;
  queueType: string;
  win: boolean;
  championId: number;
  teamPosition?: string | null;
  kills: number;
  deaths: number;
  assists: number;
  visionScore: number;
  damageToChamps: number;
  csPerMin: number;
  summonerSpell1Id: number;
  summonerSpell2Id: number;
  items: number[];
  runesDetail: MatchRuneDetail;
};

export type PagedResultDto<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type MatchDetail = {
  matchId: string;
  matchDate: number;
  duration: number;
  queueId: number;
  queueType: string;
  patch?: string | null;
  participants: Array<{
    puuid?: string | null;
    gameName?: string | null;
    tagLine?: string | null;
    teamId: number;
    championId: number;
    teamPosition?: string | null;
    win: boolean;
    kills: number;
    deaths: number;
    assists: number;
    champLevel?: number;
    goldEarned: number;
    totalDamageDealtToChampions: number;
    physicalDamageDealtToChampions?: number;
    magicDamageDealtToChampions?: number;
    trueDamageDealtToChampions?: number;
    visionScore: number;
    totalMinionsKilled: number;
    neutralMinionsKilled: number;
    summonerSpell1Id: number;
    summonerSpell2Id: number;
    items: number[];
    runes: MatchRuneDetail;
  }>;
  bans?: Array<{ teamId: number; bannedChampionIds: number[] }>;
  objectives?: MatchTeamObjectives[];
};

export type ObjectiveStat = { kills: number; first: boolean };

export type MatchTeamObjectives = {
  teamId: number;
  firstBlood: boolean;
  baron: ObjectiveStat;
  dragon: ObjectiveStat;
  riftHerald: ObjectiveStat;
  horde: ObjectiveStat;
  tower: ObjectiveStat;
  inhibitor: ObjectiveStat;
};

export type TimelineFrame = {
  minuteMark: number;
  blueGold: number;
  redGold: number;
  blueXp: number;
  redXp: number;
};

export type MatchTimeline = {
  matchId: string;
  duration: number;
  frames: TimelineFrame[];
};

export type RankHistoryEntry = {
  queueType: string | null;
  tier: string | null;
  rankNumber: string | null;
  leaguePoints: number;
  wins: number;
  losses: number;
  dateRecorded: string;
};

export type QueueOption = {
  value: string;
  label: string;
};

export type MatchSortOption = "DATE_DESC" | "KDA_DESC" | "DMG_DESC";

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null;
}

export function pickApiError(status: number, json: unknown): ApiErrorResponse {
  if (!isRecord(json)) return { message: `Request failed (${status}).` };
  const message =
    typeof json.message === "string"
      ? (json.message as string)
      : typeof json.title === "string"
        ? (json.title as string)
        : `Request failed (${status}).`;

  return {
    message,
    requestId: typeof json.requestId === "string" ? (json.requestId as string) : undefined,
    detail:
      typeof json.detail === "string"
        ? (json.detail as string)
        : typeof json.traceId === "string"
          ? `traceId: ${json.traceId as string}`
          : undefined
  };
}

export function friendlyAcceptedMessage(msg?: string) {
  const m = (msg ?? "").toLowerCase();
  if (m.includes("refresh queued")) return "Update started. This page will refresh automatically.";
  if (m.includes("refresh in process")) return "Update in progress. This page will refresh automatically.";
  return msg ?? null;
}

export function rankColorClass(tier?: string): string {
  if (!tier) return "text-fg/80";
  return rankTierColorClass(tier) === "text-muted" ? "text-fg/80" : rankTierColorClass(tier);
}

export function queueValueForMatch(match: Pick<MatchSummary, "queueId" | "queueType">): string {
  if (match.queueId > 0) return `id:${match.queueId}`;
  return `type:${match.queueType || "UNKNOWN"}`;
}

export function normalizeInitialQueue(value?: string) {
  if (!value || value.toUpperCase() === "ALL") return "ALL";
  if (value.startsWith("id:") || value.startsWith("type:")) return value;

  if (/^\d+$/.test(value)) return `id:${value}`;
  if (value.includes("_")) return `type:${value}`;

  const normalizedLabel = formatQueueLabel(value);
  return `label:${normalizedLabel}`;
}

export function normalizeInitialSort(value?: string): MatchSortOption {
  if (!value) return "DATE_DESC";
  const normalized = value.trim().toUpperCase();
  if (normalized === "KDA_DESC") return "KDA_DESC";
  if (normalized === "DMG_DESC" || normalized === "DAMAGE_DESC") return "DMG_DESC";
  return "DATE_DESC";
}

export function matchKdaRatio(match: Pick<MatchSummary, "kills" | "deaths" | "assists">): number {
  const deaths = Math.max(1, match.deaths);
  return (match.kills + match.assists) / deaths;
}

export function participantDisplayName(gameName?: string | null, tagLine?: string | null) {
  if (gameName && tagLine) return `${gameName}#${tagLine}`;
  return gameName ?? "Unknown";
}

export function isCurrentProfilePlayer(
  participant: { gameName?: string | null; tagLine?: string | null },
  gameName: string,
  tagLine: string
) {
  return (
    (participant.gameName ?? "").toLowerCase() === gameName.toLowerCase() &&
    (participant.tagLine ?? "").toLowerCase() === tagLine.toLowerCase()
  );
}

const ROLE_ALIGNMENT_ORDER = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"] as const;

export type MatchParticipant = MatchDetail["participants"][number];

export type AlignedParticipantRow = {
  roleKey: string;
  blue: MatchParticipant | null;
  red: MatchParticipant | null;
};

export function sortRuneSelections(
  selectionIds: number[],
  runeSortById: Record<string, number> | undefined
): number[] {
  return selectionIds.slice().sort((a, b) => {
    const aSort = runeSortById?.[String(a)] ?? Number.MAX_SAFE_INTEGER;
    const bSort = runeSortById?.[String(b)] ?? Number.MAX_SAFE_INTEGER;
    return aSort - bSort;
  });
}

/** The keystone is the lowest-slot primary selection — i.e. first after sorting by rune slot order. */
export function primaryKeystoneId(
  runes: MatchRuneDetail | null | undefined,
  runeStatic: { runeSortById: Record<string, number> } | null
): number {
  return sortRuneSelections(runes?.primarySelections ?? [], runeStatic?.runeSortById)[0] ?? 0;
}

export function hasRunes(runes?: MatchRuneDetail | null): boolean {
  if (!runes) return false;
  return (
    (runes.primarySelections?.length ?? 0) > 0 ||
    (runes.subSelections?.length ?? 0) > 0 ||
    (runes.statShards?.length ?? 0) > 0
  );
}

export function buildRuneRowKey(
  matchId: string,
  teamId: 100 | 200,
  rowIndex: number,
  participant: MatchParticipant
): string {
  return `${matchId}:${teamId}:${rowIndex}:${participant.puuid ?? participant.gameName ?? "unknown"}:${participant.championId}`;
}

export function normalizeRoleKey(role?: string | null): string {
  const normalized = (role ?? "").trim().toUpperCase();
  if (!normalized || normalized === "UNKNOWN" || normalized === "NONE") return "UNKNOWN";
  if (normalized === "SUPPORT") return "UTILITY";
  return normalized;
}

export function buildAlignedParticipantRows(participants: MatchParticipant[]): AlignedParticipantRow[] {
  const blueByRole = new Map<string, MatchParticipant[]>();
  const redByRole = new Map<string, MatchParticipant[]>();

  for (const participant of participants) {
    const roleKey = normalizeRoleKey(participant.teamPosition);
    const target = participant.teamId === 100 ? blueByRole : participant.teamId === 200 ? redByRole : null;
    if (!target) continue;

    const bucket = target.get(roleKey) ?? [];
    bucket.push(participant);
    target.set(roleKey, bucket);
  }

  const roleKeys = new Set<string>([...blueByRole.keys(), ...redByRole.keys()]);
  const orderedRoles = ROLE_ALIGNMENT_ORDER.filter((role) => roleKeys.has(role));
  const extraRoles = [...roleKeys]
    .filter((role) => !ROLE_ALIGNMENT_ORDER.includes(role as (typeof ROLE_ALIGNMENT_ORDER)[number]) && role !== "UNKNOWN")
    .sort((a, b) => a.localeCompare(b));

  const finalRoleOrder = [...orderedRoles, ...extraRoles];
  if (roleKeys.has("UNKNOWN")) finalRoleOrder.push("UNKNOWN");

  const rows: AlignedParticipantRow[] = [];
  for (const roleKey of finalRoleOrder) {
    const bluePlayers = blueByRole.get(roleKey) ?? [];
    const redPlayers = redByRole.get(roleKey) ?? [];
    const maxRows = Math.max(bluePlayers.length, redPlayers.length, 1);

    for (let i = 0; i < maxRows; i += 1) {
      rows.push({
        roleKey,
        blue: bluePlayers[i] ?? null,
        red: redPlayers[i] ?? null
      });
    }
  }

  return rows;
}
