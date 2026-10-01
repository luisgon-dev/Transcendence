"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";

import {
  matchKdaRatio,
  normalizeInitialQueue,
  normalizeInitialSort,
  pickApiError,
  type RefreshNotice,
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
import { championDisplayName } from "@/lib/gameDisplay";
import { acceptedOperation, operationFailure, operationRequestSignal, pollOperation } from "@/lib/operations";
import { formatQueueLabel } from "@/lib/queues";
import {
  buildLolPublicSummonerByIdPath,
  buildLolPublicSummonerByRiotIdPath,
  buildLolPublicSummonerRankHistoryPath,
  buildLolUserSummonerRefreshPath
} from "@/lib/lolPublicApi";

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
          message: initialLookup.message ?? undefined
        }
      : null;
  const [profile, setProfile] = useState<SummonerProfileResponse | null>(initialProfile);
  const [accepted, setAccepted] = useState<RefreshNotice | null>(initialAccepted);
  const [error, setError] = useState<ApiErrorResponse | null>(initialError);
  const [busy, setBusy] = useState(false);
  const [polling, setPolling] = useState(false);
  const [refreshRevision, setRefreshRevision] = useState(0);
  const request = useRef<AbortController | null>(null);

  const fetchProfileOnce = useCallback(async (signal: AbortSignal) => {
    const res = await fetch(buildLolPublicSummonerByRiotIdPath(region, gameName, tagLine), {
      cache: "no-store", signal: operationRequestSignal(signal)
    });
    const json = (await res.json().catch(() => null)) as unknown;
    if (!res.ok) {
      throw new Error(pickApiError(res.status, json).message ?? "Profile reload failed.");
    }
    const lookup = json as SummonerLookupResponse | null;
    if (lookup?.status === "ready" && lookup.profile) {
      signal.throwIfAborted();
      setProfile(lookup.profile);
      setRefreshRevision((value) => value + 1);
      return;
    }
    throw new Error("The operation finished, but its stored profile could not be loaded.");
  }, [gameName, region, tagLine]);

  useEffect(() => {
    setProfile(initialLookup?.status === "ready" ? initialLookup.profile ?? null : null);
    setAccepted(initialLookup?.status !== "ready" ? { message: initialLookup?.message ?? undefined } : null);
    setError(initialError);
    setPolling(false);
    setBusy(false);
    setRefreshRevision(0);
    return () => {
      request.current?.abort();
      request.current = null;
    };
  }, [region, gameName, tagLine, initialLookup, initialError]);

  const queueRefresh = useCallback(async () => {
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    setPolling(true);
    setError(null);
    try {
      const res = await fetch(buildLolUserSummonerRefreshPath(region, gameName, tagLine), {
        method: "POST", signal: operationRequestSignal(controller.signal)
      });
      const json: unknown = await res.json().catch(() => null);
      controller.signal.throwIfAborted();
      if (!res.ok) {
        setAccepted(null);
        setError(pickApiError(res.status, json));
        return;
      }
      const operation = acceptedOperation(json);
      setBusy(false);
      setAccepted({ message: "Refresh queued. Waiting for the profile and recent imports to finish." });
      const completed = await pollOperation(operation, "user", controller.signal, (progress) => {
        const phase = progress.phases.find((entry) => entry.status === "running" || entry.status === "retrying");
        setAccepted({ message: phase ? `Refresh ${progress.status}: ${phase.name}.` : `Refresh ${progress.status}.` });
      }, "summoner_refresh", { region, gameName, tagLine });
      if (completed.status === "failed") throw new Error(operationFailure(completed));
      if (completed.status === "succeeded" &&
          (!Number.isFinite(Date.parse(completed.result.profileUpdatedAtUtc ?? "")) ||
           !Number.isFinite(Date.parse(completed.result.recentImportCompletedAtUtc ?? "")))) {
        throw new Error("The refresh finished without profile and recent-import completion evidence.");
      }
      await fetchProfileOnce(controller.signal);
      const missingMatches = (completed.result.deferredMatchCount ?? 0) + (completed.result.failedMatchCount ?? 0);
      const recentMessage = completed.status === "partial"
        ? missingMatches > 0
          ? "Recent refresh finished with incomplete imports. Some matches may still be missing."
          : "Recent refresh finished with warnings. Some requested data could not be updated."
        : `Profile and recent-match refresh complete.${completed.result.warningCodes?.length ? ` Optional enrichment warnings: ${completed.result.warningCodes.join(", ")}.` : ""}`;
      const child = completed.phases.find((phase) => phase.name === "fullHistory" && phase.operationId);
      setAccepted({ message: child ? `${recentMessage} Full history is importing separately.` : recentMessage });
      if (child?.operationId) {
        try {
          const history = await pollOperation({
            operationId: child.operationId,
            statusUrl: `/api/lol/operations/${child.operationId}`,
            retryAfterSeconds: completed.retryAfterSeconds
          }, "user", controller.signal, undefined, "full_history", { region, gameName, tagLine });
          if (history.status === "failed") {
            setAccepted({ message: `${recentMessage} Full-history import failed${history.errorCode ? ` (${history.errorCode})` : ""}.` });
          } else {
            await fetchProfileOnce(controller.signal);
            setAccepted({ message: `${recentMessage} Full-history import ${history.status === "partial" ? "finished with missing matches" : "complete"}.` });
          }
        } catch (childError) {
          if (controller.signal.aborted) return;
          setAccepted({ message: `${recentMessage} Full-history completion has not been verified: ${childError instanceof Error ? childError.message : "check again later"}` });
        }
      }
    } catch (errorValue) {
      if (controller.signal.aborted) return;
      setAccepted(null);
      setError({
        message: errorValue instanceof Error ? errorValue.message : "Request failed.",
        code: "CLIENT_FETCH_FAILED"
      });
    } finally {
      if (request.current === controller) {
        request.current = null;
        setBusy(false);
        setPolling(false);
      }
    }
  }, [gameName, region, tagLine, fetchProfileOnce]);

  return { profile, accepted, error, busy, polling, queueRefresh, refreshRevision };
}

export function useRankHistory(
  summonerId: string | null | undefined,
  initialRankHistory: RankHistoryEntry[] | null,
  refreshRevision = 0
) {
  const [rankHistory, setRankHistory] = useState<RankHistoryEntry[] | null>(initialRankHistory);
  const serverSummonerId = useRef(initialRankHistory && summonerId ? summonerId : null);

  useEffect(() => {
    if (!summonerId || (refreshRevision === 0 && serverSummonerId.current === summonerId)) return;
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
  }, [summonerId, refreshRevision]);

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
  initialHistory,
  refreshRevision = 0
}: {
  summonerId: string | null | undefined;
  championStatic: ChampionStatic | null;
  initialPage: number;
  initialQueue: string;
  initialSort: string;
  initialChampion: string;
  initialExpandMatchId: string | null;
  initialHistory: PagedResultDto<MatchSummary> | null;
  refreshRevision?: number;
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
  const detailRequests = useRef(new Map<string, AbortController>());
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
        label: championDisplayName(championStatic?.champions[String(id)])
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
    if (refreshRevision === 0 && serverHistoryKey.current === historyKey) return;
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
  }, [page, queue, requestFilterKey, selectedChampionId, summonerId, refreshRevision]);

  useEffect(() => {
    // Imported matches can change detail/stat enrichment without changing their IDs.
    // Preserve the user's page, filters, sort, and expanded selection.
    const requests = detailRequests.current;
    for (const controller of requests.values()) controller.abort();
    requests.clear();
    setDetails({});
    setDetailBusy({});
    return () => {
      for (const controller of requests.values()) controller.abort();
      requests.clear();
    };
  }, [refreshRevision, summonerId]);

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

  const loadDetail = useCallback(async (matchId: string) => {
    if (Object.hasOwn(details, matchId) || !summonerId || detailRequests.current.has(matchId)) return;
    const controller = new AbortController();
    detailRequests.current.set(matchId, controller);
    setDetailBusy((state) => ({ ...state, [matchId]: true }));
    try {
      const res = await fetch(
        `${buildLolPublicSummonerByIdPath(summonerId)}/matches/${encodeURIComponent(matchId)}`,
        { cache: "no-store", signal: controller.signal }
      );
      const json = (await res.json().catch(() => null)) as MatchDetail | null;
      if (!controller.signal.aborted) setDetails((state) => ({ ...state, [matchId]: res.ok && json?.participants ? json : null }));
    } catch {
      if (!controller.signal.aborted) setDetails((state) => ({ ...state, [matchId]: null }));
    } finally {
      if (detailRequests.current.get(matchId) === controller) {
        detailRequests.current.delete(matchId);
        setDetailBusy((state) => ({ ...state, [matchId]: false }));
      }
    }
  }, [details, summonerId]);

  useEffect(() => {
    if (expandedMatchId) void loadDetail(expandedMatchId);
  }, [expandedMatchId, loadDetail]);

  const toggleExpanded = useCallback((matchId: string) => {
    setExpandedMatchId((current) => current === matchId ? null : matchId);
  }, []);

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
