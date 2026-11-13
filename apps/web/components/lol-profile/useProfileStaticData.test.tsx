import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { useProfileStaticData } from "./useSummonerProfileData";

const empty = { championStatic: null, itemStatic: null, spellStatic: null, runeStatic: null };
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });

it("keeps successful maps when one fails and recovers on explicit retry", async () => {
  let broken = true;
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) =>
    String(input).endsWith("/items") && broken ? new Response("unavailable", { status: 502 })
      : Response.json({ version: "16.22.1", champions: {}, items: {}, spells: {}, runeById: {} })));
  const { result } = renderHook(() => useProfileStaticData(empty));
  await waitFor(() => expect(result.current.staticUnavailable).toBe(true));
  expect(result.current.championStatic).not.toBeNull();
  expect(result.current.itemStatic).toBeNull();
  broken = false;
  act(() => result.current.retryStaticData());
  await waitFor(() => expect(result.current.staticUnavailable).toBe(false));
  expect(result.current.itemStatic).not.toBeNull();
  expect(result.current.staticBusy).toBe(false);
});

it("bounds a client response body and cancels requests on unmount", async () => {
  vi.useFakeTimers();
  const signals: AbortSignal[] = [];
  vi.stubGlobal("fetch", vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
    signals.push(init!.signal!);
    return new Response(new ReadableStream({ start() {} }));
  }));
  const { result, unmount } = renderHook(() => useProfileStaticData(empty));
  await act(async () => vi.advanceTimersByTimeAsync(12_001));
  expect(result.current.staticUnavailable).toBe(true);
  expect(result.current.staticBusy).toBe(false);
  act(() => result.current.retryStaticData());
  unmount();
  expect(signals.every(signal => signal.aborted)).toBe(true);
});
