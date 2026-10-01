import { afterEach, describe, expect, it, vi } from "vitest";

import { acceptedOperation, OPERATION_TIMEOUT_MS, operationPath, pollOperation } from "@/lib/operations";
import { accepted, operationId, operationStatus } from "@/test/operationFixtures";

const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });

describe("operation completion polling", () => {
  afterEach(() => vi.useRealTimers());

  it("constructs a scoped same-origin path and rejects injected URLs", () => {
    expect(operationPath("user", operationId)).toBe(`/api/trn/user/lol/operations/${operationId}`);
    expect(() => operationPath("app", "https://example.com/steal")).toThrow(/identifier/);
    expect(() => operationPath("app", "../admin/jobs")).toThrow(/identifier/);
  });

  it("requires the new accepted contract", () => {
    expect(() => acceptedOperation({ status: "queued" })).toThrow(/invalid/);
    expect(() => acceptedOperation({ ...accepted, retryAfterSeconds: "2" })).toThrow(/invalid/);
    expect(() => acceptedOperation({ ...accepted, retryAfterSeconds: -1 })).toThrow(/invalid/);
    expect(() => acceptedOperation({ ...accepted, statusUrl: "" })).toThrow(/invalid/);
  });

  it("ignores returned absolute URLs and waits through nonterminal states", async () => {
    vi.useFakeTimers();
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(response(operationStatus({ status: "running", retryAfterSeconds: 1 })))
      .mockResolvedValueOnce(response(operationStatus()));
    vi.stubGlobal("fetch", fetchMock);
    const result = pollOperation({ ...accepted, statusUrl: "https://example.com/steal" }, "app", new AbortController().signal);
    await vi.advanceTimersByTimeAsync(1000);
    expect((await result).status).toBe("succeeded");
    expect(fetchMock.mock.calls.map((call) => call[0])).toEqual([
      `/api/trn/app/lol/operations/${operationId}`, `/api/trn/app/lol/operations/${operationId}`
    ]);
  });

  it.each(["partial", "failed"])("returns truthful terminal %s outcomes", async (status) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(operationStatus({ status }))));
    expect((await pollOperation(accepted, "app", new AbortController().signal)).status).toBe(status);
  });

  it.each([401, 403, 404, 500])("stops immediately on HTTP %s", async (status) => {
    const fetchMock = vi.fn().mockResolvedValue(response({ message: "Status unavailable" }, status));
    vi.stubGlobal("fetch", fetchMock);
    await expect(pollOperation(accepted, "user", new AbortController().signal)).rejects.toThrow(/unavailable/);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it.each([
    { operationId: "22222222-2222-2222-2222-222222222222" },
    { kind: "summoner_refresh" },
    { platformRegion: "KR" },
    { platformRegion: "UNKNOWN" },
    { gameName: "Other Player" },
    { tagLine: "OTHER" },
    { status: "unknown" },
    { retryAfterSeconds: "1" },
    { retryAfterSeconds: -1 },
    { completedAtUtc: null }
  ])("rejects invalid or mismatched status %j", async (overrides) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response(operationStatus(overrides))));
    await expect(pollOperation(accepted, "app", new AbortController().signal, undefined, "live_game_probe", {
      region: "na", gameName: "Kronic", tagLine: "NA1"
    })).rejects.toThrow(/invalid/);
  });

  it("aborts the pending retry immediately when superseded or unmounted", async () => {
    vi.useFakeTimers();
    const fetchMock = vi.fn().mockResolvedValue(response(operationStatus({ status: "running", retryAfterSeconds: 10 })));
    vi.stubGlobal("fetch", fetchMock);
    const controller = new AbortController();
    const result = pollOperation(accepted, "app", controller.signal);
    const assertion = expect(result).rejects.toMatchObject({ name: "AbortError" });
    await vi.advanceTimersByTimeAsync(0);
    controller.abort();
    await assertion;
    await vi.advanceTimersByTimeAsync(10_000);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("bounds polling and never converts a timeout to success", async () => {
    vi.useFakeTimers();
    vi.stubGlobal("fetch", vi.fn(async () => response(operationStatus({ status: "running", retryAfterSeconds: 10 }))));
    const result = pollOperation(accepted, "app", new AbortController().signal);
    const assertion = expect(result).rejects.toThrow(/completion has not been verified/);
    await vi.advanceTimersByTimeAsync(OPERATION_TIMEOUT_MS);
    await assertion;
  });
});
