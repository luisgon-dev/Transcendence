import { afterEach, describe, expect, it, vi } from "vitest";
import { fetchAndConsumeWithTimeout } from "./fetchWithTimeout";
import { fetchBackendJson } from "./backendCall";

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

describe("backend response deadlines", () => {
  it("times out after headers when the body never completes", async () => {
    vi.useFakeTimers();
    let upstreamSignal: AbortSignal | undefined;
    vi.stubGlobal("fetch", vi.fn(async (_url, init) => {
      upstreamSignal = init.signal;
      return { json: () => new Promise(() => {}) };
    }));
    const pending = fetchAndConsumeWithTimeout("https://example.test", {}, res => res.json(), { timeoutMs: 50 });
    const assertion = expect(pending).rejects.toMatchObject({ name: "AbortError" });
    await vi.advanceTimersByTimeAsync(50);
    await assertion;
    expect(upstreamSignal?.aborted).toBe(true);
    expect(vi.getTimerCount()).toBe(0);
  });

  it("reports a structured timeout for a stalled JSON body", async () => {
    vi.useFakeTimers();
    vi.stubGlobal("fetch", vi.fn(async () => new Response(new ReadableStream())));
    const pending = fetchBackendJson("https://example.test", {}, { timeoutMs: 50 });
    await vi.advanceTimersByTimeAsync(50);
    expect(await pending).toMatchObject({ status: 504, ok: false, errorKind: "timeout" });
  });

  it("preserves caller cancellation and clears a completed deadline", async () => {
    vi.useFakeTimers();
    const caller = new AbortController();
    vi.stubGlobal("fetch", vi.fn(async (_url, init) => {
      expect(init.signal.aborted).toBe(true);
      throw init.signal.reason;
    }));
    caller.abort(new DOMException("Cancelled", "AbortError"));
    await expect(fetchAndConsumeWithTimeout("https://example.test", { signal: caller.signal }, res => res.json(),
      { timeoutMs: 50 })).rejects.toMatchObject({ name: "AbortError" });
    expect(vi.getTimerCount()).toBe(0);
    vi.stubGlobal("fetch", vi.fn(async () => new Response('{"ready":true}')));
    expect(await fetchAndConsumeWithTimeout("https://example.test", {}, res => res.json(), { timeoutMs: 50 }))
      .toEqual({ ready: true });
    expect(vi.getTimerCount()).toBe(0);
  });
});
