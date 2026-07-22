"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";

import {
  matchKdaRatio,
  normalizeInitialQueue,
  normalizeInitialSort,
  pickApiError,
  type AcceptedResponse,
  type ApiErrorResponse,
  type ChampionStatic,
  type ItemStatic,
  type MatchDetail,
  type MatchSortOption,
  type MatchSummary,
  type PagedResultDto,
  type QueueOption,
  type RankHistoryEntry,
  type RuneStatic,
  type SpellStatic,
  type SummonerLookupResponse,
  type SummonerProfileResponse
} from "@/components/lol-profile/shared";
import { computeNextPollDelayMs } from "@/lib/polling";
import { formatQueueLabel } from "@/lib/queues";
import {
  buildLolPublicSummonerByIdPath,
  buildLolPublicSummonerByRiotIdPath,
  buildLolPublicSummonerRankHistoryPath,
  buildLolUserSummonerRefreshPath
} from "@/lib/lolPublicApi";

const MAX_POLL_ATTEMPTS = 24;

export function useProfileStaticData(initial: {
  championStatic: ChampionStatic | null;
  itemStatic: ItemStatic | null;
  spellStatic: SpellStatic | null;
  runeStatic: RuneStatic | null;
}) {
  const [championStatic, setChampionStatic] = useState(initial.championStatic);
  const [itemStatic, setItemStatic] = useState(initial.itemStatic);
  const [spellStatic, setSpellStatic] = useState(initial.spellStatic);
  const [runeStatic, setRuneStatic] = useState(initial.runeStatic);

  useEffect(() => {
    let cancelled = false;
    async function loadStatic() {
      try {
        const [champRes, itemRes, spellRes, runeRes] = await Promise.all([
          championStatic ? null : fetch("/api/static/champions"),
          itemStatic ? null : fetch("/api/static/items"),
          spellStatic ? null : fetch("/api/static/spells"),
          runeStatic ? null : fetch("/api/static/runes")
        ]);
        if (cancelled) return;
        if (champRes?.ok) setChampionStatic((await champRes.json()) as ChampionStatic);
        if (itemRes?.ok) setItemStatic((await itemRes.json()) as ItemStatic);
        if (spellRes?.ok) setSpellStatic((await spellRes.json()) as SpellStatic);
        if (runeRes?.ok) setRuneStatic((await runeRes.json()) as RuneStatic);
      } catch {
        // Static decoration is optional; retain the profile shell when a map cannot load.
      }
    }
    void loadStatic();
    return () => {
      cancelled = true;
    };
  }, [championStatic, itemStatic, runeStatic, spellStatic]);

  return useMemo(
    () => ({ championStatic, itemStatic, spellStatic, runeStatic }),
    [championStatic, itemStatic, runeStatic, spellStatic]
  );
}

export function useSummonerRefreshPolling({
  region,
  gameName,
  tagLine,
  initialLookup,
  initialError
}: {
  region: string;
  gameName: string;
  tagLine: string;
  initialLookup: SummonerLookupResponse | null;
  initialError: ApiErrorResponse | null;
}) {
  const initialProfile = initialLookup?.status === "ready" ? initialLookup.profile ?? null : null;
  const initialAccepted =
    initialLookup && initialLookup.status !== "ready"
      ? {
          message: initialLookup.message ?? undefined,
          poll: initialLookup.poll ?? undefined,
          retryAfterSeconds: initialLookup.retryAfterSeconds ?? undefined
        }
      : null;
  const [profile, setProfile] = useState<SummonerProfileResponse | null>(initialProfile);
  const [accepted, setAccepted] = useState<AcceptedResponse | null>(initialAccepted);
  const [error, setError] = useState<ApiErrorResponse | null>(initialError);
  const [busy, setBusy] = useState(false);
  const [polling, setPolling] = useState(initialLookup?.status === "refreshing");
  const [pollDelayMs, setPollDelayMs] = useState(2000);
  const [pollAttempts, setPollAttempts] = useState(0);

  const fetchProfileOnce = useCallback(async () => {
    const res = await fetch(buildLolPublicSummonerByRiotIdPath(region, gameName, tagLine), {
      cache: "no-store"
    });
    const json = (await res.json().catch(() => null)) as unknown;
    if (!res.ok) {
      setAccepted(null);
      setPolling(false);
      setError(pickApiError(res.status, json));
      return;
    }
    const lookup = json as SummonerLookupResponse | null;
    if (lookup?.status === "ready" && lookup.profile) {
      setProfile(lookup.profile);
      setAccepted(null);
      setPolling(false);
      return;
    }
    if (lookup?.status === "refreshing" || lookup?.status === "missing") {
      setAccepted({
        message: lookup.message ?? undefined,
        poll: lookup.poll ?? undefined,
        retryAfterSeconds: lookup.retryAfterSeconds ?? undefined
      });
      return;
    }
    setAccepted(null);
    setPolling(false);
    setError({ message: "The player lookup returned an invalid response.", code: "INVALID_RESPONSE" });
  }, [gameName, region, tagLine]);

  useEffect(() => {
    if (!polling) return;
    if (pollAttempts >= MAX_POLL_ATTEMPTS) {
      setPolling(false);
      setAccepted({
        message: "This update is taking longer than expected — it'll keep processing in the background. Use Update Now to check again."
      });
      return;
    }
    const timeout = setTimeout(async () => {
      try {
        await fetchProfileOnce();
      } finally {
        setPollAttempts((value) => value + 1);
        setPollDelayMs((value) => computeNextPollDelayMs(value));
      }
    }, pollDelayMs);
    return () => clearTimeout(timeout);
  }, [fetchProfileOnce, pollAttempts, pollDelayMs, polling]);

  const queueRefresh = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      const res = await fetch(buildLolUserSummonerRefreshPath(region, gameName, tagLine), {
        method: "POST"
      });
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
    } catch (errorValue) {
      setAccepted(null);
      setError({
        message: errorValue instanceof Error ? errorValue.message : "Request failed.",
        code: "CLIENT_FETCH_FAILED"
      });
    } finally {
      setBusy(false);
    }
  }, [gameName, region, tagLine]);

  return { profile, accepted, error, busy, polling, queueRefresh };
}

export function useRankHistory(
  summonerId: string | null | undefined,
  initialRankHistory: RankHistoryEntry[] | null
) {
  const [rankHistory, setRankHistory] = useState<RankHistoryEntry[] | null>(initialRankHistory);
  const serverSummonerId = useRef(initialRankHistory && summonerId ? summonerId : null);

  useEffect(() => {
    if (!summonerId || serverSummonerId.current === summonerId) return;
    let cancelled = false;
    void (async () => {
      try {
        const res = await fetch(buildLolPublicSummonerRankHistoryPath(summonerId), { cache: "no-store" });
        if (!res.ok || cancelled) return;
        const json = (await res.json().catch(() => null)) as RankHistoryEntry[] | null;
        if (!cancelled && Array.isArray(json)) setRankHistory(json);
      } catch {
        // Rank progression is optional decoration.
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [summonerId]);

  return rankHistory;
}

export function useMatchHistory({
  summonerId,
  championStatic,
  initialPage,
  initialQueue,
  initialSort,
  initialChampion,
  initialExpandMatchId,
  initialHistory
}: {
  summonerId: string | null | undefined;
  championStatic: ChampionStatic | null;
  initialPage: number;
  initialQueue: string;
  initialSort: string;
  initialChampion: string;
  initialExpandMatchId: string | null;
  initialHistory: PagedResultDto<MatchSummary> | null;
}) {
  const [page, setPage] = useState(Math.max(1, initialPage));
  const [queue, setQueue] = useState(normalizeInitialQueue(initialQueue));
  const [sort, setSort] = useState<MatchSortOption>(normalizeInitialSort(initialSort));
  const [championFilter, setChampionFilter] = useState(initialChampion.trim());
  const [history, setHistory] = useState<PagedResultDto<MatchSummary> | null>(initialHistory);
  const [historyBusy, setHistoryBusy] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [expandedMatchId, setExpandedMatchId] = useState<string | null>(initialExpandMatchId);
  const [details, setDetails] = useState<Record<string, MatchDetail | null>>({});
  const [detailBusy, setDetailBusy] = useState<Record<string, boolean>>({});
  const initialIsUnfiltered = normalizeInitialQueue(initialQueue) === "ALL" && !initialChampion.trim();
  const serverHistoryKey = useRef(
    initialHistory && summonerId && initialIsUnfiltered
      ? `${summonerId}:${initialHistory.page}:ALL:-`
      : null
  );

  const queueOptions = useMemo<QueueOption[]>(() => {
    const options = new Map<string, QueueOption>();
    options.set("ALL", { value: "ALL", label: "All Queues" });
    for (const facet of history?.facets?.queues ?? []) {
      const value = `family:${facet.queueFamily}`;
      if (!options.has(value)) {
        options.set(value, {
          value,
          label: formatQueueLabel(facet.queueType, facet.queueId)
        });
      }
    }
    return [...options.values()];
  }, [history?.facets?.queues]);

  const championOptions = useMemo(() => {
    return (history?.facets?.championIds ?? [])
      .map((id) => ({
        id,
        label: championStatic?.champions[String(id)]?.name ?? `Champion ${id}`
      }))
      .sort((a, b) => a.label.localeCompare(b.label));
  }, [championStatic?.champions, history?.facets?.championIds]);

  const selectedChampionId = useMemo(() => {
    const token = championFilter.trim().toLowerCase();
    if (!token) return null;
    const numeric = Number(token);
    if (Number.isInteger(numeric) && numeric > 0) return numeric;
    return championOptions.find((option) => option.label.toLowerCase() === token)?.id ?? null;
  }, [championFilter, championOptions]);

  const requestFilterKey = `${queue}:${selectedChampionId ?? "-"}`;
  useEffect(() => {
    if (!summonerId) return;
    const historyKey = `${summonerId}:${page}:${requestFilterKey}`;
    if (serverHistoryKey.current === historyKey) return;
    serverHistoryKey.current = null;
    let cancelled = false;
    void (async () => {
      setHistoryBusy(true);
      setHistoryError(null);
      try {
        const params = new URLSearchParams({ page: String(page), pageSize: "20" });
        if (queue.startsWith("family:")) params.set("queueFamily", queue.slice(7));
        else if (queue.startsWith("id:")) params.append("queueIds", queue.slice(3));
        else if (queue.startsWith("type:")) params.set("queueFamily", queue.slice(5));
        if (selectedChampionId) params.set("championId", String(selectedChampionId));
        const res = await fetch(
          `${buildLolPublicSummonerByIdPath(summonerId)}/matches/recent?${params}`,
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
      } catch (errorValue) {
        if (!cancelled) {
          setHistoryError(errorValue instanceof Error ? errorValue.message : "Failed to load matches.");
        }
      } finally {
        if (!cancelled) setHistoryBusy(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [page, queue, requestFilterKey, selectedChampionId, summonerId]);

  const visibleMatches = useMemo(() => {
    const sorted = [...(history?.items ?? [])];
    if (sort === "KDA_DESC") sorted.sort((a, b) => matchKdaRatio(b) - matchKdaRatio(a));
    else if (sort === "DMG_DESC") sorted.sort((a, b) => b.damageToChamps - a.damageToChamps);
    else sorted.sort((a, b) => b.matchDate - a.matchDate);
    return sorted;
  }, [history?.items, sort]);

  useEffect(() => {
    if (!history || !expandedMatchId) return;
    if (visibleMatches.some((match) => match.matchId === expandedMatchId)) return;
    setExpandedMatchId(null);
  }, [expandedMatchId, history, visibleMatches]);

  const toggleExpanded = useCallback(async (matchId: string) => {
    const next = expandedMatchId === matchId ? null : matchId;
    setExpandedMatchId(next);
    if (!next || details[next] || !summonerId) return;
    setDetailBusy((state) => ({ ...state, [next]: true }));
    try {
      const res = await fetch(
        `${buildLolPublicSummonerByIdPath(summonerId)}/matches/${encodeURIComponent(next)}`,
        { cache: "no-store" }
      );
      const json = (await res.json().catch(() => null)) as MatchDetail | null;
      setDetails((state) => ({ ...state, [next]: res.ok && json?.participants ? json : null }));
    } finally {
      setDetailBusy((state) => ({ ...state, [next]: false }));
    }
  }, [details, expandedMatchId, summonerId]);

  return {
    page,
    queue,
    sort,
    championFilter,
    history,
    historyBusy,
    historyError,
    visibleMatches,
    queueOptions,
    championOptions,
    expandedMatchId,
    details,
    detailBusy,
    toggleExpanded,
    setQueue: (value: string) => { setQueue(value); setPage(1); },
    setChampionFilter: (value: string) => { setChampionFilter(value); setPage(1); },
    setSort: (value: MatchSortOption) => { setSort(value); setPage(1); },
    previousPage: () => setPage((value) => Math.max(1, value - 1)),
    nextPage: () => setPage((value) => value + 1)
  };
}
