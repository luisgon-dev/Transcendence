"use client";

import Image from "next/image";
import Link from "next/link";
import { AnimatePresence, motion } from "framer-motion";
import { useCallback, useEffect, useMemo, useState } from "react";
import { usePathname, useRouter } from "next/navigation";

import { FavoriteButton } from "@/components/FavoriteButton";
import { LiveGameCard } from "@/components/LiveGameCard";
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
import { roleDisplayLabel } from "@/lib/roles";
import { encodeRiotIdPath } from "@/lib/riotid";
import { championIconUrl, profileIconUrl } from "@/lib/staticData";

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

type MatchSummary = {
  matchId: string;
  matchDate: number;
  durationSeconds: number;
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
  queueType: string;
  patch?: string | null;
  participants: Array<{
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
  }>;
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

  const [page, setPage] = useState(Math.max(1, initialPage));
  const [queue, setQueue] = useState(initialQueue || "ALL");
  const [expandedMatchId, setExpandedMatchId] = useState<string | null>(initialExpandMatchId);
  const [history, setHistory] = useState<PagedResultDto<MatchSummary> | null>(null);
  const [historyBusy, setHistoryBusy] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [details, setDetails] = useState<Record<string, MatchDetail | null>>({});
  const [detailBusy, setDetailBusy] = useState<Record<string, boolean>>({});

  const queueOptions = useMemo(() => {
    const set = new Set<string>(["ALL"]);
    for (const m of history?.items ?? []) set.add(m.queueType);
    return Array.from(set);
  }, [history?.items]);

  const visibleMatches = useMemo(() => {
    if (!history?.items) return [];
    if (queue === "ALL") return history.items;
    return history.items.filter((m) => m.queueType === queue);
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
    async function load() {
      const res = await fetch("/api/static/champions");
      if (!res.ok) return;
      const json = (await res.json()) as ChampionStatic;
      if (!cancelled) setChampionStatic(json);
    }
    void load();
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
          <aside className="grid gap-4 lg:col-span-4 xl:col-span-3">
            <Card className="p-4">
              <h2 className="font-[var(--font-sora)] text-lg font-semibold">Ranked</h2>
              <div className="mt-3 grid gap-2 text-sm">
                {([["Solo", profile.soloRank], ["Flex", profile.flexRank]] as const).map(([label, rank]) => (
                  <div key={label} className="rounded-xl border border-border/60 bg-surface/50 px-3 py-2">
                    <p className="text-xs text-muted">{label}</p>
                    <p className={`font-semibold ${rankColorClass(rank?.tier)}`}>{rank ? `${rank.tier} ${rank.division} · ${rank.leaguePoints} LP` : "Unranked"}</p>
                  </div>
                ))}
              </div>
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

          <section className="grid gap-4 lg:col-span-8 xl:col-span-9">
            <Card className="p-5">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <h2 className="font-[var(--font-sora)] text-xl font-semibold">Match History</h2>
                <div className="flex flex-wrap gap-2">
                  {queueOptions.map((q) => (
                    <button key={q} className={`rounded-full border px-3 py-1 text-xs ${q === queue ? "border-primary/45 bg-primary/15 text-primary" : "border-border/70 bg-surface/50 text-fg/80"}`} onClick={() => setQueue(q)}>{q}</button>
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
                  return (
                    <motion.div key={m.matchId} layout className={`rounded-2xl border p-3 ${m.win ? "border-success/35 bg-success/10" : "border-danger/35 bg-danger/10"}`}>
                      <button className="w-full text-left" onClick={() => void toggleExpanded(m.matchId)}>
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <p className={`text-xs font-semibold ${m.win ? "text-success" : "text-danger"}`}>{m.win ? "VICTORY" : "DEFEAT"}</p>
                          <p className="text-xs text-muted">{m.queueType} · {formatDurationSeconds(m.durationSeconds)} · {formatRelativeTime(m.matchDate)}</p>
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
                              <p className="text-xs text-muted">{m.teamPosition ? roleDisplayLabel(m.teamPosition) : "Unknown role"} · {formatDateTimeMs(m.matchDate)}</p>
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
                              {d ? (
                                <div className="grid gap-3">
                                  {[100, 200].map((teamId) => (
                                    <div key={`${m.matchId}-${teamId}`} className="rounded-xl border border-border/50 bg-surface/40 p-2.5">
                                      <p className="mb-1 text-xs font-semibold text-fg/85">Team {teamId}</p>
                                      {d.participants.filter((p) => p.teamId === teamId).map((p, idx) => (
                                        <div key={`${teamId}-${idx}`} className="grid grid-cols-[minmax(0,1fr)_auto_auto_auto] items-center gap-2 border-t border-border/20 py-1.5 first:border-t-0">
                                          <span className="truncate text-xs">{(p.gameName && p.tagLine) ? `${p.gameName}#${p.tagLine}` : "Unknown"}{p.teamPosition ? ` · ${roleDisplayLabel(p.teamPosition)}` : ""}</span>
                                          <span className="text-xs text-fg/80">{p.kills}/{p.deaths}/{p.assists}</span>
                                          <span className="text-xs text-muted">{(p.totalMinionsKilled + p.neutralMinionsKilled)} CS</span>
                                          <span className="text-xs text-muted">{p.goldEarned.toLocaleString()}g</span>
                                        </div>
                                      ))}
                                    </div>
                                  ))}
                                </div>
                              ) : null}
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
