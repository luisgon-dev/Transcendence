"use client";

import Image from "next/image";
import Link from "next/link";
import { AnimatePresence, motion } from "framer-motion";
import { useCallback, useEffect, useMemo, useState } from "react";
import { usePathname, useRouter } from "next/navigation";

import { FavoriteButton } from "@/components/FavoriteButton";
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

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null;
}

function pickApiError(status: number, json: unknown): ApiErrorResponse {
  if (!isRecord(json)) return { message: `Request failed (${status}).` };
  return {
    message:
      typeof json.message === "string" ? (json.message as string) : `Request failed (${status}).`,
    requestId: typeof json.requestId === "string" ? (json.requestId as string) : undefined,
    detail: typeof json.detail === "string" ? (json.detail as string) : undefined
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
  initialExpandMatchId = null
}: {
  region: string;
  gameName: string;
  tagLine: string;
  initialStatus: number;
  initialBody: unknown;
  initialPage?: number;
  initialQueue?: string;
  initialExpandMatchId?: string | null;
}) {
  const router = useRouter();
  const pathname = usePathname();
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

  const visibleMatches = useMemo(() => {
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

  useEffect(() => {
    const params = new URLSearchParams();
    if (page > 1) params.set("page", String(page));
    if (queue !== "ALL") params.set("queue", queue);
    if (expandedMatchId) params.set("expandMatchId", expandedMatchId);
    const next = params.toString();
    router.replace(next ? `${pathname}?${next}` : pathname, { scroll: false });
  }, [expandedMatchId, page, pathname, queue, router]);

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

  const rankedEntries = useMemo(() => {
    const entries: Array<{ label: string; rank: RankInfo }> = [];
    if (profile?.soloRank) entries.push({ label: "Solo/Duo", rank: profile.soloRank });
    if (profile?.flexRank) entries.push({ label: "Flex", rank: profile.flexRank });
    return entries;
  }, [profile?.flexRank, profile?.soloRank]);

  const dataAge = profile?.profileAge?.ageDescription ?? "updated recently";

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
              <p className="text-sm text-muted">{profile ? `Level ${profile.summonerLevel} · ${dataAge}` : region.toUpperCase()}</p>
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
        <div className="grid gap-6 lg:grid-cols-12">
          <aside className="grid gap-4 lg:col-span-3 xl:col-span-3">
            <Card className="p-4">
              <div className="flex items-center justify-between gap-2">
                <h2 className="font-[var(--font-sora)] text-lg font-semibold">Ranked</h2>
                <Badge>{profile.rankAge?.ageDescription ?? "updated recently"}</Badge>
              </div>
              {rankedEntries.length === 0 ? (
                <p className="mt-3 text-sm text-muted">No ranked data available.</p>
              ) : (
                <div
                  className={`mt-3 grid gap-2 text-sm ${
                    rankedEntries.length > 1 ? "grid-cols-1 sm:grid-cols-2 lg:grid-cols-1" : "grid-cols-1"
                  }`}
                >
                  {rankedEntries.map(({ label, rank }) => {
                    const emblem = rankEmblemUrl(rank.tier);
                    const totalGames = rank.wins + rank.losses;
                    const wr = totalGames > 0 ? (rank.wins / totalGames) * 100 : null;
                    return (
                      <div
                        key={label}
                        className="grid grid-cols-[78px_minmax(0,1fr)] items-center gap-2.5 rounded-xl border border-border/60 bg-surface/50 px-2.5 py-2"
                      >
                        {emblem ? (
                          <div className="relative h-[72px] w-[72px] overflow-hidden rounded-lg">
                            <Image
                              src={emblem}
                              alt={`${rankTierDisplayLabel(rank.tier)} emblem`}
                              width={1280}
                              height={720}
                              unoptimized
                              sizes="220px"
                              className="absolute left-1/2 top-1/2 h-[220px] w-auto max-w-none -translate-x-1/2 -translate-y-[47%] select-none"
                            />
                          </div>
                        ) : (
                          <div className="h-[72px] w-[72px] rounded-full border border-border/60 bg-surface/70" />
                        )}
                        <div className="min-w-0">
                          <p className="text-[11px] uppercase tracking-wide text-muted">{label}</p>
                          <p className={`truncate font-semibold ${rankColorClass(rank?.tier)}`}>
                            {rankTierDisplayLabel(rank.tier)} {rank.division} · {rank.leaguePoints} LP
                          </p>
                          <p className="text-xs text-muted">
                            {rank.wins}W {rank.losses}L
                            {wr != null ? ` · ${formatPercent(wr, { input: "percent", decimals: 1 })}` : ""}
                          </p>
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </Card>
            <Card className="p-4">
              <h2 className="font-[var(--font-sora)] text-lg font-semibold">Top Champions</h2>
              <div className="mt-3 grid gap-2">
                {(profile.topChampions ?? []).slice(0, 6).map((c) => {
                  const champ = championStatic?.champions[String(c.championId)];
                  return (
                    <Link key={c.championId} href={`/champions/${c.championId}`} className="rounded-lg border border-border/60 bg-surface/50 px-2.5 py-2 text-sm hover:bg-surface/70">
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
              <div className="flex flex-wrap items-center justify-between gap-3">
                <h2 className="font-[var(--font-sora)] text-xl font-semibold">Match History</h2>
                <div className="flex flex-wrap gap-2">
                  {queueOptions.map((option) => (
                    <button
                      key={option.value}
                      className={`rounded-full border px-3 py-1 text-xs ${
                        option.value === queue
                          ? "border-primary/45 bg-primary/15 text-primary"
                          : "border-border/70 bg-surface/50 text-fg/80"
                      }`}
                      onClick={() => setQueue(option.value)}
                    >
                      {option.label}
                    </button>
                  ))}
                </div>
              </div>

              <div className="mt-3 flex items-center gap-2">
                <Badge>Page {history?.page ?? page}/{history?.totalPages ?? 1}</Badge>
                <Badge>{history?.totalCount?.toLocaleString() ?? 0} total</Badge>
              </div>

              {historyError ? <p className="mt-3 text-sm text-danger">{historyError}</p> : null}
              {historyBusy && !history ? <Skeleton className="mt-3 h-16 w-full" /> : null}

              <div className="mt-4 grid gap-3">
                {visibleMatches.map((m) => {
                  const expanded = expandedMatchId === m.matchId;
                  const d = details[m.matchId];
                  const queueLabel = formatQueueLabel(m.queueType, m.queueId);
                  return (
                    <motion.div key={m.matchId} layout className={`rounded-2xl border p-3 ${m.win ? "border-success/35 bg-success/10" : "border-danger/35 bg-danger/10"}`}>
                      <button className="w-full text-left" onClick={() => void toggleExpanded(m.matchId)}>
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <p className={`text-xs font-semibold ${m.win ? "text-success" : "text-danger"}`}>{m.win ? "VICTORY" : "DEFEAT"}</p>
                          <p className="text-xs text-muted">{queueLabel} · {formatDurationSeconds(m.durationSeconds)} · {formatRelativeTime(m.matchDate)}</p>
                        </div>
                        <div className="mt-2 flex flex-wrap items-center justify-between gap-3">
                          <div className="flex items-center gap-2">
                            {championStatic?.champions[String(m.championId)] ? (
                              <Image
                                src={championIconUrl(championStatic.version, championStatic.champions[String(m.championId)]!.id)}
                                alt={championStatic.champions[String(m.championId)]!.name}
                                width={42}
                                height={42}
                                className="rounded-lg border border-border/60"
                              />
                            ) : null}
                            <div>
                              <p className="text-sm font-semibold">{championStatic?.champions[String(m.championId)]?.name ?? `Champion ${m.championId}`}</p>
                              <p className="text-xs text-muted">{m.teamPosition ? roleDisplayLabel(m.teamPosition) : "Unknown"} · {formatDateTimeMs(m.matchDate)}</p>
                            </div>
                          </div>
                          <p className="text-sm text-fg/90">{m.kills}/{m.deaths}/{m.assists} · {m.csPerMin.toFixed(1)} CS/min</p>
                        </div>
                      </button>
                      <AnimatePresence>
                        {expanded ? (
                          <motion.div initial={{ height: 0, opacity: 0 }} animate={{ height: "auto", opacity: 1 }} exit={{ height: 0, opacity: 0 }} className="overflow-hidden">
                            <div className="mt-3 border-t border-border/35 pt-3">
                              {detailBusy[m.matchId] ? <Skeleton className="h-12 w-full" /> : null}
                              {!detailBusy[m.matchId] && !d ? <p className="text-sm text-muted">Detailed rows are unavailable for this match.</p> : null}
                              {d
                                ? (() => {
                                    const alignedRows = buildAlignedParticipantRows(d.participants ?? []);

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
                                      const runeRowKey = `${m.matchId}:${teamId}:${rowIndex}:${participant.puuid ?? participant.gameName ?? "unknown"}:${participant.championId}`;
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
                                      const hasRunes =
                                        (participant.runes?.primarySelections?.length ?? 0) > 0 ||
                                        (participant.runes?.subSelections?.length ?? 0) > 0 ||
                                        (participant.runes?.statShards?.length ?? 0) > 0;
                                      const primaryRuneId = orderedPrimarySelections[0] ?? 0;
                                      const primaryRuneMeta = runeStatic?.runeById[String(primaryRuneId)];
                                      const canExpandRunes = Boolean(runeStatic && hasRunes);

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
                                              className={`inline-flex items-center gap-1.5 rounded-md border px-2 py-1 text-[11px] ${
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
              </div>

              <div className="mt-4 flex items-center justify-between">
                <Button size="sm" variant="outline" disabled={page <= 1 || historyBusy} onClick={() => setPage((p) => Math.max(1, p - 1))}>Previous</Button>
                <Button size="sm" variant="outline" disabled={historyBusy || (history ? history.page >= history.totalPages : false)} onClick={() => setPage((p) => p + 1)}>Next</Button>
              </div>

              <p className="mt-3 text-xs text-muted">
                Legacy routes redirect here: <Link href={`/summoners/${region}/${encodeRiotIdPath({ gameName, tagLine })}/matches`} className="text-primary hover:underline">/matches</Link>
              </p>
            </Card>
          </section>
        </div>
      )}
    </div>
  );
}
