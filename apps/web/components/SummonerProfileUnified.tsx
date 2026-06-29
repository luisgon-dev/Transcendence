"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import { useReducedMotion } from "framer-motion";

import { MatchHistorySection } from "@/components/lol-profile/MatchHistorySection";
import { PerformanceCard } from "@/components/lol-profile/PerformanceCard";
import { ProfileHeroCard } from "@/components/lol-profile/ProfileHeroCard";
import { ProfileSidebar } from "@/components/lol-profile/ProfileSidebar";
import {
  matchKdaRatio,
  normalizeInitialQueue,
  normalizeInitialSort,
  pickApiError,
  queueValueForMatch,
  type AcceptedResponse,
  type ChampionStatic,
  type ItemStatic,
  type MatchDetail,
  type MatchSortOption,
  type MatchSummary,
  type PagedResultDto,
  type QueueOption,
  type RankHistoryEntry,
  type RankInfo,
  type RuneStatic,
  type SpellStatic,
  type SummonerProfileResponse
} from "@/components/lol-profile/shared";
import { Card } from "@/components/ui/Card";
import { Skeleton } from "@/components/ui/Skeleton";
import { computeNextPollDelayMs } from "@/lib/polling";
import { formatQueueLabel } from "@/lib/queues";
import {
  buildLolPublicSummonerByIdPath,
  buildLolPublicSummonerByRiotIdPath,
  buildLolPublicSummonerRankHistoryPath
} from "@/lib/lolPublicApi";

export type { SummonerProfileResponse } from "@/components/lol-profile/shared";

// Stop polling the 202→200 refresh loop after this many attempts (~2-3 min with the
// 1-10s backoff) so a stuck worker degrades to a retry prompt instead of an endless spinner.
const MAX_POLL_ATTEMPTS = 24;

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
  const [error, setError] = useState(
    initialStatus !== 200 && initialStatus !== 202 ? pickApiError(initialStatus, initialBody) : null
  );
  const [busy, setBusy] = useState(false);
  const [polling, setPolling] = useState(initialStatus === 202);
  const [pollDelayMs, setPollDelayMs] = useState(2000);
  const [pollAttempts, setPollAttempts] = useState(0);
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
  const [rankHistory, setRankHistory] = useState<RankHistoryEntry[] | null>(null);

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
      return history.items.filter((match) => match.queueId === queueId);
    }

    if (queue.startsWith("type:")) {
      const queueType = queue.slice(5);
      return history.items.filter((match) => (match.queueType || "UNKNOWN") === queueType);
    }

    if (queue.startsWith("label:")) {
      const label = queue.slice(6);
      return history.items.filter((match) => formatQueueLabel(match.queueType, match.queueId) === label);
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

  const quickStats = useMemo(() => {
    const matches = history?.items ?? [];
    if (matches.length === 0) return null;

    const total = matches.length;
    const wins = matches.filter((match) => match.win).length;
    const avgKda = matches.reduce((sum, match) => sum + matchKdaRatio(match), 0) / total;

    return {
      total,
      winRate: wins / total,
      avgKda
    };
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
    const res = await fetch(buildLolPublicSummonerByRiotIdPath(region, gameName, tagLine), {
      cache: "no-store"
    });
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
    setPolling(false);
    setError(pickApiError(res.status, json));
  }, [gameName, region, tagLine]);

  useEffect(() => {
    if (!polling) return;
    if (pollAttempts >= MAX_POLL_ATTEMPTS) {
      setPolling(false);
      setAccepted({
        message: "This update is taking longer than expected — it'll keep processing in the background. Tap Update Now to check again."
      });
      return;
    }
    const timeout = setTimeout(async () => {
      try {
        await fetchProfileOnce();
      } finally {
        setPollAttempts((n) => n + 1);
        setPollDelayMs((delay) => computeNextPollDelayMs(delay));
      }
    }, pollDelayMs);

    return () => clearTimeout(timeout);
  }, [fetchProfileOnce, pollAttempts, pollDelayMs, polling]);

  useEffect(() => {
    const summonerId = profile?.summonerId;
    if (!summonerId) return;
    let cancelled = false;

    async function load(id: string) {
      setHistoryBusy(true);
      setHistoryError(null);
      try {
        const res = await fetch(
          `${buildLolPublicSummonerByIdPath(id)}/matches/recent?page=${page}&pageSize=20`,
          { cache: "no-store" }
        );
        const json = (await res.json().catch(() => null)) as
          | PagedResultDto<MatchSummary>
          | { message?: string }
          | null;

        if (!res.ok) {
          if (!cancelled) {
            setHistoryError(
              json && "message" in json ? json.message ?? "Failed to load matches." : "Failed to load matches."
            );
          }
          return;
        }

        if (!cancelled) setHistory(json as PagedResultDto<MatchSummary>);
      } catch (e) {
        if (!cancelled) {
          setHistoryError(e instanceof Error ? e.message : "Failed to load matches.");
        }
      } finally {
        if (!cancelled) setHistoryBusy(false);
      }
    }

    void load(summonerId);
    return () => {
      cancelled = true;
    };
  }, [page, profile?.summonerId]);

  useEffect(() => {
    const summonerId = profile?.summonerId;
    if (!summonerId) return;
    let cancelled = false;

    (async () => {
      try {
        const res = await fetch(buildLolPublicSummonerRankHistoryPath(summonerId), { cache: "no-store" });
        if (!res.ok || cancelled) return;
        const json = (await res.json().catch(() => null)) as RankHistoryEntry[] | null;
        if (!cancelled && Array.isArray(json)) setRankHistory(json);
      } catch {
        // Rank progression is optional decoration — ignore fetch errors.
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [profile?.summonerId]);

  async function queueRefresh() {
    setBusy(true);
    setError(null);
    try {
      const res = await fetch(
        `${buildLolPublicSummonerByRiotIdPath(region, gameName, tagLine)}/refresh`,
        { method: "POST" }
      );
      const json = (await res.json().catch(() => null)) as AcceptedResponse | null;
      if (!res.ok) {
        setAccepted(null);
        setError(pickApiError(res.status, json));
        return;
      }
      setAccepted(json ?? { message: "Update started." });
      setPollAttempts(0);
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

    setDetailBusy((state) => ({ ...state, [next]: true }));
    try {
      const res = await fetch(
        `${buildLolPublicSummonerByIdPath(profile.summonerId)}/matches/${encodeURIComponent(next)}`,
        { cache: "no-store" }
      );
      const json = (await res.json().catch(() => null)) as MatchDetail | null;
      if (res.ok && json?.participants) setDetails((state) => ({ ...state, [next]: json }));
      else setDetails((state) => ({ ...state, [next]: null }));
    } finally {
      setDetailBusy((state) => ({ ...state, [next]: false }));
    }
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
    <div className="grid grid-cols-1 gap-8">
      <ProfileHeroCard
        title={title}
        region={region}
        gameName={gameName}
        tagLine={tagLine}
        profile={profile}
        championStatic={championStatic}
        dataAge={dataAge}
        rankedEntries={rankedEntries}
        quickStats={quickStats}
        recentForm={recentForm}
        accepted={accepted}
        error={error}
        busy={busy}
        onRefresh={queueRefresh}
      />

      {!profile ? (
        <Card className="profile-section-card p-5">
          <Skeleton className="h-16 w-full" />
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-6 xl:grid-cols-[minmax(280px,0.32fr)_minmax(0,1fr)] xl:items-start">
          <ProfileSidebar
            profile={profile}
            championStatic={championStatic}
            rankedEntries={rankedEntries}
            unrankedQueues={unrankedQueues}
            rankHistory={rankHistory}
            region={region}
            gameName={gameName}
            tagLine={tagLine}
          />
          <div className="grid gap-6">
            <PerformanceCard
              matches={history?.items ?? []}
              overviewStats={profile.overviewStats}
              topChampions={profile.topChampions}
            />
            <MatchHistorySection
              region={region}
              gameName={gameName}
              tagLine={tagLine}
              summonerId={profile.summonerId ?? ""}
              page={page}
              queue={queue}
              championFilter={championFilter}
              sort={sort}
              history={history}
              historyBusy={historyBusy}
              historyError={historyError}
              visibleMatches={visibleMatches}
              queueOptions={queueOptions}
              championOptions={championOptions}
              sortOptions={sortOptions}
              expandedMatchId={expandedMatchId}
              details={details}
              detailBusy={detailBusy}
              championStatic={championStatic}
              itemStatic={itemStatic}
              spellStatic={spellStatic}
              runeStatic={runeStatic}
              prefersReducedMotion={Boolean(prefersReducedMotion)}
              onQueueChange={(value) => {
                setQueue(value);
                setPage(1);
              }}
              onChampionFilterChange={(value) => {
                setChampionFilter(value);
                setPage(1);
              }}
              onSortChange={(value) => {
                setSort(value);
                setPage(1);
              }}
              onToggleExpanded={toggleExpanded}
              onPreviousPage={() => setPage((current) => Math.max(1, current - 1))}
              onNextPage={() => setPage((current) => current + 1)}
            />
          </div>
        </div>
      )}
    </div>
  );
}
