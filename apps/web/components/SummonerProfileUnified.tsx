"use client";

import Image from "next/image";
import Link from "next/link";
import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import { useCallback, useEffect, useMemo, useState } from "react";
import { usePathname, useRouter } from "next/navigation";

import { FavoriteButton } from "@/components/FavoriteButton";
import { Input } from "@/components/ui/Input";
import { LiveGameCard } from "@/components/LiveGameCard";
import { RuneSetupDisplay } from "@/components/RuneSetupDisplay";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { Skeleton } from "@/components/ui/Skeleton";
import {
  formatDateTimeMs,
  formatDurationSeconds,
  formatPercent,
  formatRelativeTime,
  winRateColorClass
} from "@/lib/format";
import { computeNextPollDelayMs } from "@/lib/polling";
import { formatQueueLabel } from "@/lib/queues";
import { rankEmblemUrl, rankTierDisplayLabel } from "@/lib/ranks";
import { roleDisplayLabel } from "@/lib/roles";
import { encodeRiotIdPath } from "@/lib/riotid";
import {
  championIconUrl,
  itemIconUrl,
  profileIconUrl,
  runeIconUrl,
  summonerSpellIconUrl
} from "@/lib/staticData";

type DataAgeMetadata = {
  fetchedAt?: string;
  ageDescription?: string;
  [k: string]: unknown;
};

type RankInfo = {
  tier: string;
  division: string;
  leaguePoints: number;
  wins: number;
  losses: number;
};

type ProfileOverviewStats = {
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

type ProfileChampionStat = {
  championId: number;
  championName: string;
  games: number;
  wins: number;
  losses: number;
  winRate: number;
  kdaRatio: number;
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
  profileAge: DataAgeMetadata;
  rankAge: DataAgeMetadata;
  statsAge?: DataAgeMetadata | null;
};

type AcceptedResponse = {
  message?: string;
  retryAfterSeconds?: number;
  poll?: string;
};

type ApiErrorResponse = {
  message?: string;
  code?: string;
  requestId?: string;
  detail?: string;
};

type ChampionStatic = {
  version: string;
  champions: Record<string, { id: string; name: string }>;
};

type ItemStatic = {
  version: string;
  items: Record<string, { name: string; plaintext?: string }>;
};

type SpellStatic = {
  version: string;
  spells: Record<string, { id: string; name: string }>;
};

type RuneStatic = {
  version: string;
  runeById: Record<string, { name: string; icon: string }>;
  styleById: Record<string, { name: string; icon: string }>;
  runeSortById: Record<string, number>;
};

type MatchRuneDetail = {
  primaryStyleId: number;
  subStyleId: number;
  primarySelections: number[];
  subSelections: number[];
  statShards: number[];
};

type MatchSummary = {
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

type PagedResultDto<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

type MatchDetail = {
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
    goldEarned: number;
    totalDamageDealtToChampions: number;
    totalMinionsKilled: number;
    neutralMinionsKilled: number;
    summonerSpell1Id: number;
    summonerSpell2Id: number;
    items: number[];
    runes: MatchRuneDetail;
  }>;
};

type QueueOption = {
  value: string;
  label: string;
};

type MatchSortOption = "DATE_DESC" | "KDA_DESC" | "DMG_DESC";

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null;
}

function pickApiError(status: number, json: unknown): ApiErrorResponse {
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

function friendlyAcceptedMessage(msg?: string) {
  const m = (msg ?? "").toLowerCase();
  if (m.includes("refresh queued")) return "Update started and data is being refreshed.";
  if (m.includes("refresh in process")) return "Refresh in progress. This page will update automatically.";
  return msg ?? null;
}

function rankColorClass(tier?: string): string {
  if (!tier) return "text-fg/80";
  const map: Record<string, string> = {
    IRON: "text-zinc-400",
    BRONZE: "text-amber-600",
    SILVER: "text-zinc-300",
    GOLD: "text-yellow-400",
    PLATINUM: "text-cyan-400",
    EMERALD: "text-emerald-400",
    DIAMOND: "text-sky-300",
    MASTER: "text-purple-400",
    GRANDMASTER: "text-red-400",
    CHALLENGER: "text-amber-300"
  };
  return map[tier.toUpperCase()] ?? "text-fg/80";
}

function queueValueForMatch(match: Pick<MatchSummary, "queueId" | "queueType">): string {
  if (match.queueId > 0) return `id:${match.queueId}`;
  return `type:${match.queueType || "UNKNOWN"}`;
}

function normalizeInitialQueue(value?: string) {
  if (!value || value.toUpperCase() === "ALL") return "ALL";
  if (value.startsWith("id:") || value.startsWith("type:")) return value;

  if (/^\d+$/.test(value)) return `id:${value}`;
  if (value.includes("_")) return `type:${value}`;

  const normalizedLabel = formatQueueLabel(value);
  return `label:${normalizedLabel}`;
}

function normalizeInitialSort(value?: string): MatchSortOption {
  if (!value) return "DATE_DESC";
  const normalized = value.trim().toUpperCase();
  if (normalized === "KDA_DESC") return "KDA_DESC";
  if (normalized === "DMG_DESC" || normalized === "DAMAGE_DESC") return "DMG_DESC";
  return "DATE_DESC";
}

function matchKdaRatio(match: Pick<MatchSummary, "kills" | "deaths" | "assists">): number {
  const deaths = Math.max(1, match.deaths);
  return (match.kills + match.assists) / deaths;
}

function participantDisplayName(gameName?: string | null, tagLine?: string | null) {
  if (gameName && tagLine) return `${gameName}#${tagLine}`;
  return gameName ?? "Unknown";
}

function isCurrentProfilePlayer(
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

type MatchParticipant = MatchDetail["participants"][number];
type AlignedParticipantRow = {
  roleKey: string;
  blue: MatchParticipant | null;
  red: MatchParticipant | null;
};

function hasRunes(runes?: MatchRuneDetail | null): boolean {
  if (!runes) return false;
  return (
    (runes.primarySelections?.length ?? 0) > 0 ||
    (runes.subSelections?.length ?? 0) > 0 ||
    (runes.statShards?.length ?? 0) > 0
  );
}

function buildRuneRowKey(
  matchId: string,
  teamId: 100 | 200,
  rowIndex: number,
  participant: MatchParticipant
): string {
  return `${matchId}:${teamId}:${rowIndex}:${participant.puuid ?? participant.gameName ?? "unknown"}:${participant.championId}`;
}

function normalizeRoleKey(role?: string | null): string {
  const normalized = (role ?? "").trim().toUpperCase();
  if (!normalized || normalized === "UNKNOWN" || normalized === "NONE") return "UNKNOWN";
  if (normalized === "SUPPORT") return "UTILITY";
  return normalized;
}

function buildAlignedParticipantRows(participants: MatchParticipant[]): AlignedParticipantRow[] {
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

export function SummonerProfileClient({
  region,
  gameName,
  tagLine,
  initialStatus,
  initialBody,
  initialPage = 1,
  initialQueue = "ALL",
  initialExpandMatchId = null,
  initialSort = "DATE_DESC",
  initialChampion = ""
}: {
  region: string;
  gameName: string;
  tagLine: string;
  initialStatus: number;
  initialBody: unknown;
  initialPage?: number;
  initialQueue?: string;
  initialExpandMatchId?: string | null;
  initialSort?: string;
  initialChampion?: string;
}) {
  const router = useRouter();
  const pathname = usePathname();
  const prefersReducedMotion = useReducedMotion();
  const title = `${gameName}#${tagLine}`;

  const [profile, setProfile] = useState<SummonerProfileResponse | null>(
    initialStatus === 200 ? (initialBody as SummonerProfileResponse) : null
  );
  const [accepted, setAccepted] = useState<AcceptedResponse | null>(
    initialStatus === 202 ? (initialBody as AcceptedResponse) : null
  );
  const [error, setError] = useState<ApiErrorResponse | null>(
    initialStatus !== 200 && initialStatus !== 202 ? pickApiError(initialStatus, initialBody) : null
  );
  const [busy, setBusy] = useState(false);
  const [polling, setPolling] = useState(initialStatus === 202);
  const [pollDelayMs, setPollDelayMs] = useState(2000);
  const [championStatic, setChampionStatic] = useState<ChampionStatic | null>(null);
  const [itemStatic, setItemStatic] = useState<ItemStatic | null>(null);
  const [spellStatic, setSpellStatic] = useState<SpellStatic | null>(null);
  const [runeStatic, setRuneStatic] = useState<RuneStatic | null>(null);

  const [page, setPage] = useState(Math.max(1, initialPage));
  const [queue, setQueue] = useState(normalizeInitialQueue(initialQueue));
  const [expandedMatchId, setExpandedMatchId] = useState<string | null>(initialExpandMatchId);
  const [sort, setSort] = useState<MatchSortOption>(normalizeInitialSort(initialSort));
  const [championFilter, setChampionFilter] = useState(initialChampion.trim());
  const [history, setHistory] = useState<PagedResultDto<MatchSummary> | null>(null);
  const [historyBusy, setHistoryBusy] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [details, setDetails] = useState<Record<string, MatchDetail | null>>({});
  const [detailBusy, setDetailBusy] = useState<Record<string, boolean>>({});
  const [expandedRunes, setExpandedRunes] = useState<Record<string, boolean>>({});

  const queueOptions = useMemo<QueueOption[]>(() => {
    const optionMap = new Map<string, QueueOption>();
    optionMap.set("ALL", { value: "ALL", label: "All Queues" });

    for (const match of history?.items ?? []) {
      const value = queueValueForMatch(match);
      const label = formatQueueLabel(match.queueType, match.queueId);
      optionMap.set(value, { value, label });
    }

    return Array.from(optionMap.values());
  }, [history?.items]);

  const queueFilteredMatches = useMemo(() => {
    if (!history?.items) return [];
    if (queue === "ALL") return history.items;

    if (queue.startsWith("id:")) {
      const queueId = Number(queue.slice(3));
      return history.items.filter((m) => m.queueId === queueId);
    }

    if (queue.startsWith("type:")) {
      const queueType = queue.slice(5);
      return history.items.filter((m) => (m.queueType || "UNKNOWN") === queueType);
    }

    if (queue.startsWith("label:")) {
      const label = queue.slice(6);
      return history.items.filter((m) => formatQueueLabel(m.queueType, m.queueId) === label);
    }

    return history.items;
  }, [history?.items, queue]);

  const championOptions = useMemo(() => {
    const championMap = new Map<number, string>();
    for (const match of history?.items ?? []) {
      if (championMap.has(match.championId)) continue;
      const name = championStatic?.champions[String(match.championId)]?.name;
      championMap.set(match.championId, name ?? `Champion ${match.championId}`);
    }
    return [...championMap.entries()]
      .map(([id, label]) => ({ id, label }))
      .sort((a, b) => a.label.localeCompare(b.label));
  }, [championStatic?.champions, history?.items]);

  const visibleMatches = useMemo(() => {
    const normalizedChampionFilter = championFilter.trim().toLowerCase();
    const filtered =
      normalizedChampionFilter.length === 0
        ? queueFilteredMatches
        : queueFilteredMatches.filter((match) => {
            const championIdToken = String(match.championId);
            const championName =
              championStatic?.champions[String(match.championId)]?.name.toLowerCase() ?? "";
            return (
              championIdToken.includes(normalizedChampionFilter) ||
              championName.includes(normalizedChampionFilter)
            );
          });

    const sorted = filtered.slice();
    if (sort === "KDA_DESC") {
      sorted.sort((a, b) => matchKdaRatio(b) - matchKdaRatio(a));
    } else if (sort === "DMG_DESC") {
      sorted.sort((a, b) => b.damageToChamps - a.damageToChamps);
    } else {
      sorted.sort((a, b) => b.matchDate - a.matchDate);
    }
    return sorted;
  }, [championFilter, championStatic?.champions, queueFilteredMatches, sort]);

  const recentForm = useMemo(() => {
    return (history?.items ?? []).slice(0, 10).map((match) => match.win);
  }, [history?.items]);

  useEffect(() => {
    if (!history || !expandedMatchId) return;
    if (visibleMatches.some((match) => match.matchId === expandedMatchId)) return;
    setExpandedMatchId(null);
  }, [expandedMatchId, history, visibleMatches]);

  useEffect(() => {
    const params = new URLSearchParams();
    if (page > 1) params.set("page", String(page));
    if (queue !== "ALL") params.set("queue", queue);
    if (sort !== "DATE_DESC") params.set("sort", sort.toLowerCase());
    if (championFilter.trim()) params.set("champion", championFilter.trim());
    if (expandedMatchId) params.set("expandMatchId", expandedMatchId);
    const next = params.toString();
    router.replace(next ? `${pathname}?${next}` : pathname, { scroll: false });
  }, [championFilter, expandedMatchId, page, pathname, queue, router, sort]);

  useEffect(() => {
    let cancelled = false;
    async function loadStatic() {
      try {
        const [champRes, itemRes, spellRes, runeRes] = await Promise.all([
          fetch("/api/static/champions"),
          fetch("/api/static/items"),
          fetch("/api/static/spells"),
          fetch("/api/static/runes")
        ]);

        if (cancelled) return;

        if (champRes.ok) {
          const json = (await champRes.json()) as ChampionStatic;
          if (!cancelled) setChampionStatic(json);
        }

        if (itemRes.ok) {
          const json = (await itemRes.json()) as ItemStatic;
          if (!cancelled) setItemStatic(json);
        }

        if (spellRes.ok) {
          const json = (await spellRes.json()) as SpellStatic;
          if (!cancelled) setSpellStatic(json);
        }

        if (runeRes.ok) {
          const json = (await runeRes.json()) as RuneStatic;
          if (!cancelled) setRuneStatic(json);
        }
      } catch {
        // Keep rendering profile shell even when static assets fail to load.
      }
    }
    void loadStatic();
    return () => {
      cancelled = true;
    };
  }, []);

  const fetchProfileOnce = useCallback(async () => {
    const res = await fetch(
      `/api/trn/public/summoners/${encodeURIComponent(region)}/${encodeURIComponent(
        gameName
      )}/${encodeURIComponent(tagLine)}`,
      { cache: "no-store" }
    );
    const json = (await res.json().catch(() => null)) as unknown;
    if (res.status === 200) {
      setProfile(json as SummonerProfileResponse);
      setAccepted(null);
      setPolling(false);
      return;
    }
    if (res.status === 202) {
      setAccepted((json as AcceptedResponse) ?? { message: "Refresh in process." });
      return;
    }
    setAccepted(null);
    setError(pickApiError(res.status, json));
  }, [gameName, region, tagLine]);

  useEffect(() => {
    if (!polling) return;
    const t = setTimeout(async () => {
      try {
        await fetchProfileOnce();
      } finally {
        setPollDelayMs((d) => computeNextPollDelayMs(d));
      }
    }, pollDelayMs);
    return () => clearTimeout(t);
  }, [fetchProfileOnce, pollDelayMs, polling]);

  useEffect(() => {
    const id = profile?.summonerId;
    if (!id) return;
    let cancelled = false;
    async function load(summonerId: string) {
      setHistoryBusy(true);
      setHistoryError(null);
      try {
        const res = await fetch(
          `/api/trn/public/summoners/${encodeURIComponent(summonerId)}/matches/recent?page=${page}&pageSize=20`,
          { cache: "no-store" }
        );
        const json = (await res.json().catch(() => null)) as PagedResultDto<MatchSummary> | { message?: string } | null;
        if (!res.ok) {
          if (!cancelled) setHistoryError(json && "message" in json ? json.message ?? "Failed to load matches." : "Failed to load matches.");
          return;
        }
        if (!cancelled) setHistory(json as PagedResultDto<MatchSummary>);
      } catch (e) {
        if (!cancelled) setHistoryError(e instanceof Error ? e.message : "Failed to load matches.");
      } finally {
        if (!cancelled) setHistoryBusy(false);
      }
    }
    void load(id);
    return () => {
      cancelled = true;
    };
  }, [page, profile?.summonerId]);

  async function queueRefresh() {
    setBusy(true);
    setError(null);
    try {
      const res = await fetch(
        `/api/trn/public/summoners/${encodeURIComponent(region)}/${encodeURIComponent(
          gameName
        )}/${encodeURIComponent(tagLine)}/refresh`,
        { method: "POST" }
      );
      const json = (await res.json().catch(() => null)) as AcceptedResponse | null;
      if (!res.ok) {
        setAccepted(null);
        setError(pickApiError(res.status, json));
        return;
      }
      setAccepted(json ?? { message: "Refresh queued." });
      setPolling(true);
      setPollDelayMs(computeNextPollDelayMs(2000, json?.retryAfterSeconds));
    } catch (e) {
      setAccepted(null);
      setError({
        message: e instanceof Error ? e.message : "Request failed.",
        code: "CLIENT_FETCH_FAILED"
      });
    } finally {
      setBusy(false);
    }
  }

  async function toggleExpanded(matchId: string) {
    const next = expandedMatchId === matchId ? null : matchId;
    setExpandedMatchId(next);
    if (!next || details[next] || !profile?.summonerId) return;

    setDetailBusy((s) => ({ ...s, [next]: true }));
    try {
      const res = await fetch(
        `/api/trn/public/summoners/${encodeURIComponent(profile.summonerId)}/matches/${encodeURIComponent(next)}`,
        { cache: "no-store" }
      );
      const json = (await res.json().catch(() => null)) as MatchDetail | null;
      if (res.ok && json?.participants) setDetails((s) => ({ ...s, [next]: json }));
      else setDetails((s) => ({ ...s, [next]: null }));
    } finally {
      setDetailBusy((s) => ({ ...s, [next]: false }));
    }
  }

  function toggleRuneRow(runeRowKey: string) {
    setExpandedRunes((state) => ({
      ...state,
      [runeRowKey]: !state[runeRowKey]
    }));
  }

  function toggleAllRunesForMatch(matchId: string, participants: MatchParticipant[], expanded: boolean) {
    const rows = buildAlignedParticipantRows(participants ?? []);
    const updates: Record<string, boolean> = {};

    rows.forEach((row, rowIndex) => {
      if (row.blue && hasRunes(row.blue.runes)) {
        updates[buildRuneRowKey(matchId, 100, rowIndex, row.blue)] = expanded;
      }
      if (row.red && hasRunes(row.red.runes)) {
        updates[buildRuneRowKey(matchId, 200, rowIndex, row.red)] = expanded;
      }
    });

    if (Object.keys(updates).length === 0) return;
    setExpandedRunes((state) => ({ ...state, ...updates }));
  }

  const rankedEntries = useMemo(() => {
    const entries: Array<{ label: string; rank: RankInfo }> = [];
    if (profile?.soloRank) entries.push({ label: "Solo/Duo", rank: profile.soloRank });
    if (profile?.flexRank) entries.push({ label: "Flex", rank: profile.flexRank });
    return entries;
  }, [profile?.flexRank, profile?.soloRank]);

  const unrankedQueues = useMemo(() => {
    const queues: string[] = [];
    if (!profile?.soloRank) queues.push("Solo/Duo: Unranked");
    if (!profile?.flexRank) queues.push("Flex: Unranked");
    return queues;
  }, [profile?.flexRank, profile?.soloRank]);

  const dataAge = profile?.profileAge?.ageDescription ?? "updated recently";
  const sortOptions: Array<{ value: MatchSortOption; label: string }> = [
    { value: "DATE_DESC", label: "Most Recent" },
    { value: "KDA_DESC", label: "Best KDA" },
    { value: "DMG_DESC", label: "Highest Damage" }
  ];

  return (
    <div className="grid gap-6">
      <Card className="rounded-3xl p-5 md:p-6">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="flex min-w-0 items-center gap-4">
            {profile && championStatic ? (
              <Image
                src={profileIconUrl(championStatic.version, profile.profileIconId)}
                alt={`${title} icon`}
                width={72}
                height={72}
                className="rounded-2xl border border-border/80"
              />
            ) : (
              <div className="h-[72px] w-[72px] rounded-2xl border border-border/70 bg-surface/70" />
            )}
            <div className="min-w-0">
              <h1 className="truncate font-[var(--font-sora)] text-3xl font-semibold">{title}</h1>
              <p className="text-sm text-fg/80">{profile ? `Level ${profile.summonerLevel} · ${dataAge}` : region.toUpperCase()}</p>
              {recentForm.length > 0 ? (
                <div className="mt-2">
                  <p className="text-[11px] uppercase tracking-wide text-fg/70">Recent Form</p>
                  <div className="mt-1 flex flex-wrap items-center gap-1" aria-label="Recent match outcomes (latest first)">
                    {recentForm.map((win, idx) => (
                      <span
                        key={`${win ? "w" : "l"}-${idx}`}
                        className={`h-2.5 w-6 rounded-full ${win ? "bg-success/70" : "bg-danger/70"}`}
                        aria-label={win ? "Win" : "Loss"}
                        title={win ? "Win" : "Loss"}
                      />
                    ))}
                  </div>
                </div>
              ) : null}
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Button variant="outline" onClick={queueRefresh} disabled={busy}>{busy ? "Starting..." : "Update"}</Button>
            <FavoriteButton region={region} gameName={gameName} tagLine={tagLine} />
          </div>
        </div>
        {accepted?.message ? <p className="mt-3 text-sm text-fg/85">{friendlyAcceptedMessage(accepted.message)}</p> : null}
        {error?.message ? <p className="mt-3 text-sm text-danger">{error.message}</p> : null}
      </Card>

      {!profile ? (
        <Card className="p-5"><Skeleton className="h-16 w-full" /></Card>
      ) : (
        <div className="grid gap-6 lg:grid-cols-12 lg:items-start">
          <aside className="grid content-start gap-4 lg:col-span-3 xl:col-span-3 lg:self-start">
            <Card className="p-4">
              <div className="flex items-center justify-between gap-2">
                <h2 className="font-[var(--font-sora)] text-lg font-semibold">Ranked</h2>
                <Badge>{profile.rankAge?.ageDescription ?? "updated recently"}</Badge>
              </div>
              {rankedEntries.length === 0 ? (
                <p className="mt-3 text-sm text-fg/80">No ranked data available. Solo/Duo and Flex are currently unranked.</p>
              ) : (
                <div className="mt-3 grid gap-2 text-sm">
                  {rankedEntries.map(({ label, rank }) => {
                    const emblem = rankEmblemUrl(rank.tier);
                    const totalGames = rank.wins + rank.losses;
                    const wr = totalGames > 0 ? (rank.wins / totalGames) * 100 : null;
                    return (
                      <div
                        key={label}
                        className="grid grid-cols-[72px_minmax(0,1fr)] items-center gap-2.5 rounded-xl border border-border/60 bg-surface/50 px-2.5 py-2"
                      >
                        {emblem ? (
                          <div className="flex h-[72px] w-[72px] items-center justify-center rounded-lg border border-border/50 bg-surface/70 p-1">
                            <Image
                              src={emblem}
                              alt={`${rankTierDisplayLabel(rank.tier)} emblem`}
                              width={72}
                              height={72}
                              unoptimized
                              sizes="72px"
                              className="h-full w-full select-none object-contain"
                            />
                          </div>
                        ) : (
                          <div className="h-[72px] w-[72px] rounded-lg border border-border/60 bg-surface/70" />
                        )}
                        <div className="min-w-0">
                          <p className="text-[11px] uppercase tracking-wide text-fg/70">{label}</p>
                          <p className={`truncate font-semibold ${rankColorClass(rank?.tier)}`}>
                            {rankTierDisplayLabel(rank.tier)} {rank.division} · {rank.leaguePoints} LP
                          </p>
                          <p className="text-xs text-fg/80">
                            {rank.wins}W {rank.losses}L
                            {wr != null ? ` · ${formatPercent(wr, { input: "percent", decimals: 1 })}` : ""}
                          </p>
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
              {unrankedQueues.length > 0 ? (
                <div className="mt-2 grid gap-1">
                  {unrankedQueues.map((label) => (
                    <p key={label} className="text-xs text-fg/75">
                      {label}
                    </p>
                  ))}
                </div>
              ) : null}
            </Card>
            <Card className="p-4">
              <h2 className="font-[var(--font-sora)] text-lg font-semibold">Top Champions</h2>
              <div className="mt-3 grid gap-2">
                {(profile.topChampions ?? []).slice(0, 6).map((c) => {
                  const champ = championStatic?.champions[String(c.championId)];
                  return (
                    <Link key={c.championId} href={`/lol/champions/${c.championId}`} className="rounded-lg border border-border/60 bg-surface/50 px-2.5 py-2 text-sm hover:bg-surface/70">
                      {champ?.name ?? c.championName} · {c.games} games · <span className={winRateColorClass(c.winRate)}>{formatPercent(c.winRate)}</span>
                    </Link>
                  );
                })}
              </div>
            </Card>
            <LiveGameCard region={region} gameName={gameName} tagLine={tagLine} />
          </aside>

          <section className="grid gap-4 lg:col-span-9 xl:col-span-9">
            <Card className="p-5">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <h2 className="font-[var(--font-sora)] text-xl font-semibold">Match History</h2>
                  <div className="mt-2 flex flex-wrap gap-2">
                    {queueOptions.map((option) => (
                      <button
                        key={option.value}
                        className={`rounded-full border px-3 py-1 text-xs focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/45 ${
                          option.value === queue
                            ? "border-primary/45 bg-primary/15 text-primary"
                            : "border-border/70 bg-surface/50 text-fg/90"
                        }`}
                        onClick={() => {
                          setQueue(option.value);
                          setPage(1);
                        }}
                        aria-pressed={option.value === queue}
                      >
                        {option.label}
                      </button>
                    ))}
                  </div>
                </div>
                <div className="grid gap-2 sm:grid-cols-[minmax(200px,1fr)_auto]">
                  <div>
                    <label htmlFor="match-champion-filter" className="sr-only">
                      Filter matches by champion
                    </label>
                    <Input
                      id="match-champion-filter"
                      list="match-champion-options"
                      placeholder="Filter champion (name or ID)"
                      value={championFilter}
                      onChange={(event) => {
                        setChampionFilter(event.currentTarget.value);
                        setPage(1);
                      }}
                      className="h-9 min-w-[220px] text-xs"
                      spellCheck={false}
                    />
                    <datalist id="match-champion-options">
                      {championOptions.map((option) => (
                        <option key={`champion-filter-${option.id}`} value={option.label} />
                      ))}
                    </datalist>
                  </div>
                  <div>
                    <label htmlFor="match-sort" className="sr-only">
                      Sort matches
                    </label>
                    <select
                      id="match-sort"
                      value={sort}
                      onChange={(event) => {
                        setSort(normalizeInitialSort(event.currentTarget.value));
                        setPage(1);
                      }}
                      className="h-9 rounded-xl border border-border/70 bg-surface/50 px-3 text-xs text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/45"
                    >
                      {sortOptions.map((option) => (
                        <option key={option.value} value={option.value}>
                          {option.label}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>
              </div>

              <div className="mt-3 flex flex-wrap items-center gap-2">
                <Badge>Page {history?.page ?? page}/{history?.totalPages ?? 1}</Badge>
                <Badge>{history?.totalCount?.toLocaleString() ?? 0} total</Badge>
                <Badge>{visibleMatches.length} shown</Badge>
              </div>

              {historyError ? <p className="mt-3 text-sm text-danger">{historyError}</p> : null}
              {historyBusy && !history ? <Skeleton className="mt-3 h-16 w-full" /> : null}

              <div className="mt-4 grid gap-3">
                {visibleMatches.map((m) => {
                  const expanded = expandedMatchId === m.matchId;
                  const d = details[m.matchId];
                  const queueLabel = formatQueueLabel(m.queueType, m.queueId);
                  const champion = championStatic?.champions[String(m.championId)];
                  const championName = champion?.name ?? `Champion ${m.championId}`;
                  const roleLabel = m.teamPosition ? roleDisplayLabel(m.teamPosition) : "Unknown";
                  const orderedPrimarySelections = (m.runesDetail?.primarySelections ?? []).slice().sort((a, b) => {
                    const aSort = runeStatic?.runeSortById[String(a)] ?? Number.MAX_SAFE_INTEGER;
                    const bSort = runeStatic?.runeSortById[String(b)] ?? Number.MAX_SAFE_INTEGER;
                    return aSort - bSort;
                  });
                  const primaryRuneId = orderedPrimarySelections[0] ?? 0;
                  const primaryRuneMeta = runeStatic?.runeById[String(primaryRuneId)];
                  const subStyleMeta = runeStatic?.styleById[String(m.runesDetail?.subStyleId ?? 0)];
                  const spellIds = [m.summonerSpell1Id, m.summonerSpell2Id];
                  const itemSlots = Array.from({ length: 7 }, (_, idx) => m.items[idx] ?? 0);
                  const matchMetaId = `match-meta-${m.matchId}`;
                  const matchPanelId = `match-panel-${m.matchId}`;
                  return (
                    <motion.div
                      key={m.matchId}
                      layout={!prefersReducedMotion}
                      className={`group relative overflow-hidden rounded-2xl border p-3 ${
                        m.win ? "border-success/40 bg-success/10" : "border-danger/40 bg-danger/10"
                      }`}
                    >
                      <span
                        className={`absolute inset-y-0 left-0 w-1 ${m.win ? "bg-success/75" : "bg-danger/75"}`}
                        aria-hidden="true"
                      />
                      <div
                        className={`pointer-events-none absolute inset-0 ${
                          m.win ? "bg-gradient-to-r from-success/10 to-transparent" : "bg-gradient-to-r from-danger/10 to-transparent"
                        }`}
                        aria-hidden="true"
                      />
                      <button
                        className="relative z-10 w-full text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/45"
                        onClick={() => void toggleExpanded(m.matchId)}
                        aria-expanded={expanded}
                        aria-controls={matchPanelId}
                        aria-describedby={matchMetaId}
                        aria-label={`${m.win ? "Victory" : "Defeat"} on ${championName}. KDA ${m.kills}/${m.deaths}/${m.assists}. ${formatDurationSeconds(m.durationSeconds)}.`}
                      >
                        <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto]">
                          <div className="grid gap-2">
                            <div className="flex flex-wrap items-center gap-1.5">
                              <span className={`text-xs font-semibold ${m.win ? "text-success" : "text-danger"}`}>
                                {m.win ? "VICTORY" : "DEFEAT"}
                              </span>
                              <span className="rounded-full border border-border/60 bg-surface/50 px-2 py-0.5 text-[11px] font-medium text-fg/95">
                                {queueLabel}
                              </span>
                              <span className="rounded-full border border-border/60 bg-surface/50 px-2 py-0.5 text-[11px] font-medium text-fg/95">
                                {roleLabel}
                              </span>
                              <span className="rounded-full border border-border/60 bg-surface/50 px-2 py-0.5 text-[11px] font-medium text-fg/95">
                                {formatDurationSeconds(m.durationSeconds)}
                              </span>
                            </div>
                            <div className="grid gap-2 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center">
                              <div className="flex min-w-0 items-center gap-2">
                                {champion && championStatic ? (
                                  <Image
                                    src={championIconUrl(championStatic.version, champion.id)}
                                    alt={championName}
                                    width={42}
                                    height={42}
                                    className="rounded-lg border border-border/60"
                                  />
                                ) : (
                                  <div className="h-[42px] w-[42px] rounded-lg border border-border/60 bg-surface/60" />
                                )}
                                <div className="min-w-0">
                                  <p className="truncate text-sm font-semibold">{championName}</p>
                                  <p id={matchMetaId} className="text-xs text-fg/80">
                                    {formatRelativeTime(m.matchDate)} · {formatDateTimeMs(m.matchDate)}
                                  </p>
                                </div>
                              </div>
                              <div className="rounded-lg border border-border/55 bg-surface/65 px-3 py-1.5 text-right shadow-[inset_0_1px_0_hsl(0_0%_100%_/_0.04)]">
                                <p className="text-lg font-semibold leading-tight tracking-tight text-fg">
                                  <span>{m.kills}</span>/<span className="text-danger/90">{m.deaths}</span>/<span>{m.assists}</span>
                                </p>
                                <p className="text-xs font-medium text-fg/85">
                                  {matchKdaRatio(m).toFixed(2)} KDA · {m.csPerMin.toFixed(1)} CS/min
                                </p>
                              </div>
                            </div>
                          </div>
                          <div className="grid gap-2">
                            <div className="flex items-center justify-end gap-2">
                              <div className="flex items-center gap-1" aria-label="Summoner spells">
                                {spellIds.map((spellId, spellIdx) => {
                                  const spellMeta = spellStatic?.spells[String(spellId)];
                                  return spellMeta && spellStatic ? (
                                    <Image
                                      key={`${m.matchId}-spell-${spellIdx}-${spellId}`}
                                      src={summonerSpellIconUrl(spellStatic.version, spellMeta.id)}
                                      alt={spellMeta.name}
                                      title={spellMeta.name}
                                      width={20}
                                      height={20}
                                      className="rounded-md border border-border/50"
                                    />
                                  ) : (
                                    <div
                                      key={`${m.matchId}-spell-empty-${spellIdx}-${spellId}`}
                                      className="h-5 w-5 rounded-md border border-border/40 bg-surface/60"
                                      aria-hidden="true"
                                    />
                                  );
                                })}
                              </div>
                              <div className="flex items-center gap-1" aria-label="Rune preview">
                                {primaryRuneMeta ? (
                                  <Image
                                    src={runeIconUrl(primaryRuneMeta.icon)}
                                    alt={primaryRuneMeta.name}
                                    title={primaryRuneMeta.name}
                                    width={20}
                                    height={20}
                                    className="rounded-full border border-border/40 bg-black/20 p-0.5"
                                  />
                                ) : (
                                  <span className="h-5 w-5 rounded-full border border-border/40 bg-black/20" aria-hidden="true" />
                                )}
                                {subStyleMeta ? (
                                  <Image
                                    src={runeIconUrl(subStyleMeta.icon)}
                                    alt={subStyleMeta.name}
                                    title={subStyleMeta.name}
                                    width={20}
                                    height={20}
                                    className="rounded-full border border-border/40 bg-black/20 p-0.5"
                                  />
                                ) : (
                                  <span className="h-5 w-5 rounded-full border border-border/40 bg-black/20" aria-hidden="true" />
                                )}
                              </div>
                            </div>
                            <div className="flex flex-wrap items-center justify-end gap-1" aria-label="Item build preview">
                              {itemSlots.map((itemId, itemIdx) => {
                                if (!itemId) {
                                  return (
                                    <div
                                      key={`${m.matchId}-item-empty-${itemIdx}`}
                                      className="h-5 w-5 rounded-md border border-border/35 bg-surface/60"
                                      aria-hidden="true"
                                    />
                                  );
                                }
                                const itemMeta = itemStatic?.items[String(itemId)];
                                return itemStatic ? (
                                  <Image
                                    key={`${m.matchId}-item-${itemIdx}-${itemId}`}
                                    src={itemIconUrl(itemStatic.version, itemId)}
                                    alt={itemMeta?.name ?? `Item ${itemId}`}
                                    title={itemMeta?.name ?? `Item ${itemId}`}
                                    width={20}
                                    height={20}
                                    className="rounded-md border border-border/35"
                                  />
                                ) : (
                                  <div
                                    key={`${m.matchId}-item-loading-${itemIdx}-${itemId}`}
                                    className="h-5 w-5 rounded-md border border-border/35 bg-surface/60"
                                    aria-hidden="true"
                                  />
                                );
                              })}
                            </div>
                          </div>
                        </div>
                      </button>
                      <AnimatePresence initial={false}>
                        {expanded ? (
                          <motion.div
                            id={matchPanelId}
                            initial={prefersReducedMotion ? undefined : { height: 0, opacity: 0 }}
                            animate={prefersReducedMotion ? undefined : { height: "auto", opacity: 1 }}
                            exit={prefersReducedMotion ? undefined : { height: 0, opacity: 0 }}
                            className="overflow-hidden"
                            style={prefersReducedMotion ? { height: "auto", opacity: 1 } : undefined}
                          >
                            <div className="mt-3 border-t border-border/35 pt-3">
                              {detailBusy[m.matchId] ? <Skeleton className="h-12 w-full" /> : null}
                              {!detailBusy[m.matchId] && !d ? <p className="text-sm text-fg/75">Detailed rows are unavailable for this match.</p> : null}
                              {d
                                ? (() => {
                                    const alignedRows = buildAlignedParticipantRows(d.participants ?? []);
                                    const runeRowKeys: string[] = [];
                                    alignedRows.forEach((row, rowIndex) => {
                                      if (row.blue && hasRunes(row.blue.runes)) {
                                        runeRowKeys.push(buildRuneRowKey(m.matchId, 100, rowIndex, row.blue));
                                      }
                                      if (row.red && hasRunes(row.red.runes)) {
                                        runeRowKeys.push(buildRuneRowKey(m.matchId, 200, rowIndex, row.red));
                                      }
                                    });
                                    const allRunesExpanded =
                                      runeRowKeys.length > 0 &&
                                      runeRowKeys.every((runeRowKey) => expandedRunes[runeRowKey] === true);
                                    const canToggleAllRunes = Boolean(runeStatic && runeRowKeys.length > 0);

                                    const renderParticipantCard = (
                                      participant: MatchParticipant | null,
                                      teamId: 100 | 200,
                                      roleKey: string,
                                      rowIndex: number
                                    ) => {
                                      if (!participant) {
                                        return (
                                          <div className="rounded-lg border border-dashed border-border/35 bg-surface/20 px-2 py-2 text-xs text-muted">
                                            {roleDisplayLabel(roleKey)} unavailable
                                          </div>
                                        );
                                      }

                                      const isCurrent = isCurrentProfilePlayer(participant, gameName, tagLine);
                                      const champMeta = championStatic?.champions[String(participant.championId)];
                                      const itemIds = (participant.items ?? []).slice(0, 7);
                                      const cs = (participant.totalMinionsKilled + participant.neutralMinionsKilled).toLocaleString();
                                      const runeRowKey = buildRuneRowKey(m.matchId, teamId, rowIndex, participant);
                                      const runesExpanded = expandedRunes[runeRowKey] === true;
                                      const orderedPrimarySelections = (participant.runes?.primarySelections ?? [])
                                        .slice()
                                        .sort((a, b) => {
                                          const aSort =
                                            runeStatic?.runeSortById[String(a)] ?? Number.MAX_SAFE_INTEGER;
                                          const bSort =
                                            runeStatic?.runeSortById[String(b)] ?? Number.MAX_SAFE_INTEGER;
                                          return aSort - bSort;
                                        });
                                      const hasRunesData = hasRunes(participant.runes);
                                      const primaryRuneId = orderedPrimarySelections[0] ?? 0;
                                      const primaryRuneMeta = runeStatic?.runeById[String(primaryRuneId)];
                                      const canExpandRunes = Boolean(runeStatic && hasRunesData);

                                      return (
                                        <div
                                          className={`rounded-lg border px-2 py-1.5 ${
                                            isCurrent
                                              ? "border-primary/50 bg-primary/10"
                                              : "border-border/25 bg-surface/30"
                                          }`}
                                        >
                                          <div className="grid items-center gap-1.5 sm:grid-cols-[minmax(0,1fr)_auto]">
                                            <div className="flex items-center gap-2">
                                              {champMeta && championStatic ? (
                                                <Image
                                                  src={championIconUrl(championStatic.version, champMeta.id)}
                                                  alt={champMeta.name}
                                                  width={28}
                                                  height={28}
                                                  className="rounded-md border border-border/50"
                                                />
                                              ) : (
                                                <div className="h-7 w-7 rounded-md border border-border/50 bg-surface/70" />
                                              )}
                                              <div className="min-w-0 flex-1">
                                                <p className="truncate text-xs font-medium text-fg/95">
                                                  {participantDisplayName(participant.gameName, participant.tagLine)}
                                                </p>
                                                <div className="mt-0.5 flex flex-wrap items-center gap-1.5 text-[11px] text-muted">
                                                  <span>{participant.kills}/{participant.deaths}/{participant.assists}</span>
                                                  <span>{cs} CS</span>
                                                  <span>{participant.goldEarned.toLocaleString()}g</span>
                                                  <span>{participant.totalDamageDealtToChampions.toLocaleString()} dmg</span>
                                                </div>
                                              </div>
                                            </div>

                                            <div className="flex items-center gap-1.5 lg:justify-end">
                                              {[participant.summonerSpell1Id, participant.summonerSpell2Id].map((spellId, spellIdx) => {
                                                const spellMeta = spellStatic?.spells[String(spellId)];
                                                return spellMeta && spellStatic ? (
                                                  <Image
                                                    key={`${spellId}-${spellIdx}`}
                                                    src={summonerSpellIconUrl(spellStatic.version, spellMeta.id)}
                                                    alt={spellMeta.name}
                                                    title={spellMeta.name}
                                                    width={18}
                                                    height={18}
                                                    className="rounded-md border border-border/40"
                                                  />
                                                ) : (
                                                  <div
                                                    key={`${spellId}-${spellIdx}`}
                                                    className="h-[18px] w-[18px] rounded-md border border-border/40 bg-surface/60"
                                                  />
                                                );
                                              })}
                                            </div>
                                          </div>

                                          <div className="mt-1.5 flex flex-wrap items-center justify-between gap-2">
                                            <div className="flex flex-wrap items-center gap-1">
                                              {itemIds.length > 0
                                                ? itemIds.map((itemId, itemIdx) => {
                                                    if (!itemId) {
                                                      return (
                                                        <div
                                                          key={`empty-${itemIdx}`}
                                                          className="h-5 w-5 rounded-md border border-border/35 bg-surface/60"
                                                        />
                                                      );
                                                    }

                                                    const itemMeta = itemStatic?.items[String(itemId)];
                                                    return itemStatic ? (
                                                      <Image
                                                        key={`${itemId}-${itemIdx}`}
                                                        src={itemIconUrl(itemStatic.version, itemId)}
                                                        alt={itemMeta?.name ?? `Item ${itemId}`}
                                                        title={itemMeta?.name ?? `Item ${itemId}`}
                                                        width={20}
                                                        height={20}
                                                        className="rounded-md border border-border/35"
                                                      />
                                                    ) : (
                                                      <div
                                                        key={`${itemId}-${itemIdx}`}
                                                        className="h-5 w-5 rounded-md border border-border/35 bg-surface/60"
                                                      />
                                                    );
                                                  })
                                                : null}
                                            </div>

                                            <button
                                              type="button"
                                              onClick={() => toggleRuneRow(runeRowKey)}
                                              disabled={!canExpandRunes}
                                              className={`inline-flex items-center gap-1.5 rounded-md border px-2 py-1 text-[11px] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 ${
                                                canExpandRunes
                                                  ? "border-border/50 bg-surface/40 text-fg/85 hover:bg-surface/60"
                                                  : "border-border/25 bg-surface/25 text-muted"
                                              }`}
                                              aria-expanded={runesExpanded}
                                              aria-label={runesExpanded ? "Hide runes" : "Show runes"}
                                            >
                                              {primaryRuneMeta ? (
                                                <Image
                                                  src={runeIconUrl(primaryRuneMeta.icon)}
                                                  alt={primaryRuneMeta.name}
                                                  title={primaryRuneMeta.name}
                                                  width={20}
                                                  height={20}
                                                  className="rounded-full border border-border/35 bg-black/20 p-0.5"
                                                />
                                              ) : (
                                                <span className="h-5 w-5 rounded-full border border-border/35 bg-black/20" />
                                              )}
                                              <span>{canExpandRunes ? (runesExpanded ? "Hide Runes" : "Show Runes") : "Runes Unavailable"}</span>
                                            </button>
                                          </div>
                                          {runeStatic && canExpandRunes && runesExpanded ? (
                                            <RuneSetupDisplay
                                              primaryStyleId={participant.runes?.primaryStyleId ?? 0}
                                              subStyleId={participant.runes?.subStyleId ?? 0}
                                              primarySelections={participant.runes?.primarySelections ?? []}
                                              subSelections={participant.runes?.subSelections ?? []}
                                              statShards={participant.runes?.statShards ?? []}
                                              runeById={runeStatic.runeById}
                                              styleById={runeStatic.styleById}
                                              runeSortById={runeStatic.runeSortById}
                                              iconSize={20}
                                              density="compact"
                                              className="mt-1.5"
                                            />
                                          ) : null}
                                        </div>
                                      );
                                    };

                                    return (
                                      <div className="rounded-xl border border-border/50 bg-surface/40 p-2">
                                        <div className="mb-2 flex items-center justify-between gap-2">
                                          <p className="text-[11px] uppercase tracking-wide text-fg/70">Matchup Details</p>
                                          <button
                                            type="button"
                                            onClick={() =>
                                              toggleAllRunesForMatch(m.matchId, d.participants ?? [], !allRunesExpanded)
                                            }
                                            disabled={!canToggleAllRunes}
                                            className={`inline-flex items-center gap-1 rounded-md border px-2 py-1 text-[11px] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 ${
                                              canToggleAllRunes
                                                ? "border-border/55 bg-surface/40 text-fg/85 hover:bg-surface/55"
                                                : "border-border/25 bg-surface/25 text-muted"
                                            }`}
                                          >
                                            {allRunesExpanded ? "Collapse all runes" : "Expand all runes"}
                                          </button>
                                        </div>
                                        <div className="mb-2 grid grid-cols-2 gap-2">
                                          <p className="text-xs font-semibold text-sky-300">Blue Team</p>
                                          <p className="text-xs font-semibold text-rose-300">Red Team</p>
                                        </div>
                                        <div className="grid gap-1.5">
                                          {alignedRows.map((row, rowIndex) => (
                                            <div key={`${m.matchId}-${row.roleKey}-${rowIndex}`} className="grid gap-1">
                                              <p className="px-1 text-[10px] uppercase tracking-wide text-muted">
                                                {roleDisplayLabel(row.roleKey)}
                                              </p>
                                              <div className="grid gap-2 xl:grid-cols-2">
                                                {renderParticipantCard(row.blue, 100, row.roleKey, rowIndex)}
                                                {renderParticipantCard(row.red, 200, row.roleKey, rowIndex)}
                                              </div>
                                            </div>
                                          ))}
                                        </div>
                                      </div>
                                    );
                                  })()
                                : null}
                            </div>
                          </motion.div>
                        ) : null}
                      </AnimatePresence>
                    </motion.div>
                  );
                })}
                {!historyBusy && visibleMatches.length === 0 ? (
                  <p className="rounded-xl border border-border/40 bg-surface/30 px-3 py-3 text-sm text-fg/80">
                    No matches found for the current queue/champion filters.
                  </p>
                ) : null}
              </div>

              <div className="mt-4 flex items-center justify-between">
                <Button size="sm" variant="outline" disabled={page <= 1 || historyBusy} onClick={() => setPage((p) => Math.max(1, p - 1))}>Previous</Button>
                <Button size="sm" variant="outline" disabled={historyBusy || (history ? history.page >= history.totalPages : false)} onClick={() => setPage((p) => p + 1)}>Next</Button>
              </div>

              <p className="mt-3 text-xs text-muted">
                Match history: <Link href={`/lol/summoners/${region}/${encodeRiotIdPath({ gameName, tagLine })}/matches`} className="text-primary hover:underline">/lol/summoners/.../matches</Link>
              </p>
            </Card>
          </section>
        </div>
      )}
    </div>
  );
}
