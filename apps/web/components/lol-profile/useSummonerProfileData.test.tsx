import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { useMatchHistory, useRankHistory, useSummonerRefreshPolling } from "./useSummonerProfileData";
import type { MatchSummary, SummonerLookupResponse, SummonerProfileResponse } from "./shared";
import { accepted, childOperationId, observedAtUtc, operationStatus } from "@/test/operationFixtures";

const response = (value: unknown) => new Response(JSON.stringify(value), { status: 200 });
const profile: SummonerProfileResponse = {
  summonerId: "summoner-id", puuid: "puuid", gameName: "Kronic", tagLine: "NA1",
  summonerLevel: 1, profileIconId: 1, profileAge: {}, rankAge: {}
};
const initialLookup: SummonerLookupResponse = { status: "ready", profile };
const props = { region: "na", gameName: "Kronic", tagLine: "NA1", initialLookup, initialError: null };
const refreshStatus = (overrides: Record<string, unknown> = {}) => operationStatus({ kind: "summoner_refresh",
  result: { profileUpdatedAtUtc: observedAtUtc, recentImportCompletedAtUtc: observedAtUtc }, ...overrides });

describe("request-correlated summoner refresh", () => {
  it("does not reload an already-ready profile until recent imports finish", async () => {
    let resolveOperation: (value: Response) => void = () => undefined;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      if (String(input).endsWith("/refresh")) return response(accepted);
      if (String(input).includes("/operations/")) return new Promise<Response>((resolve) => { resolveOperation = resolve; });
      return response({ status: "ready", profile: { ...profile, summonerLevel: 2 } });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { result } = renderHook(() => useSummonerRefreshPolling(props));
    let pending: Promise<void>;
    act(() => { pending = result.current.queueRefresh(); });
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(result.current.profile?.summonerLevel).toBe(1);
    expect(result.current.refreshRevision).toBe(0);
    await act(async () => { resolveOperation(response(refreshStatus())); await pending; });
    expect(result.current.profile?.summonerLevel).toBe(2);
    expect(result.current.refreshRevision).toBe(1);
    expect(result.current.accepted?.message).toContain("recent-match refresh complete");
  });

  it("reports partial imports and still reloads the persisted results", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => String(input).endsWith("/refresh")
      ? response(accepted) : String(input).includes("/operations/")
        ? response(refreshStatus({ status: "partial", result: { failedMatchCount: 1 } })) : response(initialLookup)));
    const { result } = renderHook(() => useSummonerRefreshPolling(props));
    await act(async () => { await result.current.queueRefresh(); });
    expect(result.current.accepted?.message).toContain("incomplete imports");
    expect(result.current.refreshRevision).toBe(1);
  });

  it("reports optional enrichment warnings without labeling successful recent imports incomplete", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => String(input).endsWith("/refresh")
      ? response(accepted) : String(input).includes("/operations/")
        ? response(refreshStatus({ result: { profileUpdatedAtUtc: observedAtUtc, recentImportCompletedAtUtc: observedAtUtc,
          warningCodes: ["MASTERY_UNAVAILABLE"] } })) : response(initialLookup)));
    const { result } = renderHook(() => useSummonerRefreshPolling(props));
    await act(async () => { await result.current.queueRefresh(); });
    expect(result.current.accepted?.message).toContain("recent-match refresh complete");
    expect(result.current.accepted?.message).toContain("MASTERY_UNAVAILABLE");
    expect(result.current.accepted?.message).not.toContain("incomplete imports");
  });

  it("shows recent completion independently while the linked full-history child runs", async () => {
    let resolveChild: (value: Response) => void = () => undefined;
    let reads = 0;
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const path = String(input);
      if (path.endsWith("/refresh")) return response(accepted);
      if (path.endsWith(childOperationId)) return new Promise<Response>((resolve) => { resolveChild = resolve; });
      if (path.includes("/operations/")) return response(refreshStatus({ phases: [{ name: "fullHistory", status: "queued", operationId: childOperationId }] }));
      return response({ status: "ready", profile: { ...profile, summonerLevel: ++reads + 1 } });
    }));
    const { result } = renderHook(() => useSummonerRefreshPolling(props));
    let pending: Promise<void>;
    act(() => { pending = result.current.queueRefresh(); });
    await waitFor(() => expect(result.current.accepted?.message).toContain("Full history is importing separately"));
    expect(result.current.refreshRevision).toBe(1);
    await act(async () => {
      resolveChild(response(operationStatus({ operationId: childOperationId, kind: "full_history", result: {} })));
      await pending;
    });
    expect(result.current.refreshRevision).toBe(2);
    expect(result.current.accepted?.message).toContain("Full-history import complete");
  });

  it.each(["terminal", "status-request"])("preserves recent success when child history fails (%s)", async (failure) => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const path = String(input);
      if (path.endsWith("/refresh")) return response(accepted);
      if (path.endsWith(childOperationId)) return failure === "terminal"
        ? response(operationStatus({ operationId: childOperationId, kind: "full_history", status: "failed", errorCode: "HISTORY_IMPORT_FAILED", result: {} }))
        : new Response(JSON.stringify({ message: "History status unavailable" }), { status: 503 });
      if (path.includes("/operations/")) return response(refreshStatus({ phases: [{ name: "fullHistory", status: "queued", operationId: childOperationId }] }));
      return response({ status: "ready", profile: { ...profile, summonerLevel: 2 } });
    }));
    const { result } = renderHook(() => useSummonerRefreshPolling(props));
    await act(async () => { await result.current.queueRefresh(); });
    expect(result.current.accepted?.message).toContain("recent-match refresh complete");
    expect(result.current.accepted?.message).toContain(failure === "terminal" ? "Full-history import failed" : "Full-history completion has not been verified");
    expect(result.current.error).toBeNull();
    expect(result.current.profile?.summonerLevel).toBe(2);
    expect(result.current.refreshRevision).toBe(1);
  });

  it("does not mark failed refreshes complete or invalidate stored data", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => String(input).endsWith("/refresh")
      ? response(accepted) : response(refreshStatus({ status: "failed", errorCode: "RETRIES_EXHAUSTED" }))));
    const { result } = renderHook(() => useSummonerRefreshPolling(props));
    await act(async () => { await result.current.queueRefresh(); });
    expect(result.current.error?.message).toContain("RETRIES_EXHAUSTED");
    expect(result.current.profile).toEqual(profile);
    expect(result.current.refreshRevision).toBe(0);
  });

  it("does not certify success without recent-import completion evidence", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => String(input).endsWith("/refresh")
      ? response(accepted) : response(refreshStatus({ result: {} }))));
    const { result } = renderHook(() => useSummonerRefreshPolling(props));
    await act(async () => { await result.current.queueRefresh(); });
    expect(result.current.error?.message).toContain("completion evidence");
    expect(result.current.profile).toEqual(profile);
    expect(result.current.refreshRevision).toBe(0);
  });

  it("aborts superseded work so a late old result cannot overwrite a newer refresh", async () => {
    let resolveOld: (value: Response) => void = () => undefined;
    let polls = 0;
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const path = String(input);
      if (path.endsWith("/refresh")) return response(accepted);
      if (path.includes("/operations/")) return ++polls === 1
        ? new Promise<Response>((resolve) => { resolveOld = resolve; }) : response(refreshStatus());
      return response({ status: "ready", profile: { ...profile, summonerLevel: 2 } });
    }));
    const { result } = renderHook(() => useSummonerRefreshPolling(props));
    let old: Promise<void>;
    act(() => { old = result.current.queueRefresh(); });
    await waitFor(() => expect(polls).toBe(1));
    await act(async () => { await result.current.queueRefresh(); });
    await act(async () => { resolveOld(response(refreshStatus({ status: "failed" }))); await old; });
    expect(result.current.profile?.summonerLevel).toBe(2);
    expect(result.current.error).toBeNull();
    expect(result.current.refreshRevision).toBe(1);
  });

  it("aborts status requests on unmount", async () => {
    let statusSignal: AbortSignal | undefined;
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input).endsWith("/refresh")) return response(accepted);
      statusSignal = init?.signal as AbortSignal;
      return new Promise<Response>(() => undefined);
    }));
    const { result, unmount } = renderHook(() => useSummonerRefreshPolling(props));
    act(() => { void result.current.queueRefresh(); });
    await waitFor(() => expect(statusSignal).toBeDefined());
    unmount();
    expect(statusSignal?.aborted).toBe(true);
  });

  it("refetches filtered history after completion without resetting page, sort, or filters", async () => {
    const fetchMock = vi.fn().mockResolvedValue(response({ items: [], page: 3, pageSize: 20, totalCount: 0, totalPages: 3 }));
    vi.stubGlobal("fetch", fetchMock);
    const { result, rerender } = renderHook(({ revision }) => useMatchHistory({
      summonerId: "summoner-id", championStatic: null, initialPage: 3,
      initialQueue: "family:RANKED", initialSort: "KDA_DESC", initialChampion: "24",
      initialExpandMatchId: null, initialHistory: null, refreshRevision: revision
    }), { initialProps: { revision: 0 } });
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    rerender({ revision: 1 });
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(fetchMock.mock.calls[1][0]).toBe(fetchMock.mock.calls[0][0]);
    expect(fetchMock.mock.calls[1][0]).toContain("page=3");
    expect(fetchMock.mock.calls[1][0]).toContain("championId=24");
    expect(fetchMock.mock.calls[1][0]).toContain("queueFamily=RANKED");
    expect(result.current.sort).toBe("KDA_DESC");
  });

  it("invalidates rank history after imports finish for the same summoner", async () => {
    const fetchMock = vi.fn().mockResolvedValue(response([]));
    vi.stubGlobal("fetch", fetchMock);
    const { rerender } = renderHook(({ revision }) => useRankHistory("summoner-id", [], revision), {
      initialProps: { revision: 0 }
    });
    expect(fetchMock).not.toHaveBeenCalled();
    rerender({ revision: 1 });
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    expect(fetchMock.mock.calls[0][0]).toContain("/stats/rank-history");
  });

  it("reloads an expanded detail and ignores an old request after refresh invalidation", async () => {
    let resolveOld: (value: Response) => void = () => undefined;
    let detailReads = 0;
    const item = { matchId: "match-id", matchDate: 1 } as MatchSummary;
    const history = { items: [item], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 };
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      if (String(input).includes("/matches/recent")) return response(history);
      return ++detailReads === 1 ? new Promise<Response>((resolve) => { resolveOld = resolve; })
        : response({ matchId: "match-id", duration: 2, participants: [] });
    }));
    const { result, rerender } = renderHook(({ revision }) => useMatchHistory({
      summonerId: "summoner-id", championStatic: null, initialPage: 1,
      initialQueue: "ALL", initialSort: "DATE_DESC", initialChampion: "",
      initialExpandMatchId: "match-id", initialHistory: history, refreshRevision: revision
    }), { initialProps: { revision: 0 } });
    await waitFor(() => expect(detailReads).toBe(1));
    rerender({ revision: 1 });
    await waitFor(() => expect(result.current.details["match-id"]?.duration).toBe(2));
    await act(async () => { resolveOld(response({ matchId: "match-id", duration: 1, participants: [] })); });
    expect(result.current.details["match-id"]?.duration).toBe(2);
    expect(result.current.expandedMatchId).toBe("match-id");
  });
});
