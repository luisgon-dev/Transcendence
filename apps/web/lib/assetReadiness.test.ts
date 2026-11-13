import { afterEach, describe, expect, it, vi } from "vitest";
import { AssetReadinessProbe } from "@/lib/assetReadiness";

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });

function healthyFetch() {
  return vi.fn(async (input: RequestInfo | URL) => String(input).includes("versions.json")
    ? Response.json(["16.22.1"])
    : new Response("image-content", { headers: { "content-type": "image/png" } }));
}

describe("asset content readiness", () => {
  it("checks real optimizer responses once for concurrent callers and caches a success", async () => {
    const fetch = healthyFetch(); vi.stubGlobal("fetch", fetch);
    const probe = new AssetReadinessProbe("http://127.0.0.1:3000");
    const [first, second] = await Promise.all([probe.get(), probe.get()]);
    expect(first).toBe(second); expect(first.ready).toBe(true);
    expect(Object.values(first.checks)).toEqual([true, true, true, true]);
    expect(fetch).toHaveBeenCalledTimes(5);
    await probe.get(); expect(fetch).toHaveBeenCalledTimes(5);
    expect(probe.renderPrometheus()).toContain('transcendence_image_probe_ready{kind="splash"} 1');
    expect(probe.renderPrometheus()).not.toContain("ddragon");
  });

  it("retries after an upstream outage and rejects non-image successes", async () => {
    vi.useFakeTimers();
    const fetch = healthyFetch().mockResolvedValueOnce(new Response("offline", { status: 503 }));
    vi.stubGlobal("fetch", fetch);
    const probe = new AssetReadinessProbe();
    expect((await probe.get()).ready).toBe(false);
    await vi.advanceTimersByTimeAsync(5001);
    expect((await probe.get()).ready).toBe(true);
    const invalid = new AssetReadinessProbe();
    fetch.mockImplementation(async input => String(input).includes("versions.json")
      ? Response.json(["16.22.1"]) : new Response("error page"));
    expect((await invalid.get()).ready).toBe(false);
  });

  it("bounds a stalled response body and never accepts a metadata URL as a version", async () => {
    vi.useFakeTimers();
    const fetch = vi.fn(async () => new Response(new ReadableStream({ start() {} })));
    vi.stubGlobal("fetch", fetch);
    const result = new AssetReadinessProbe().get();
    await vi.advanceTimersByTimeAsync(8001);
    expect((await result).ready).toBe(false);
    fetch.mockResolvedValue(Response.json(["https://internal.invalid"]));
    expect((await new AssetReadinessProbe().get()).ready).toBe(false);
    expect(fetch).toHaveBeenCalledTimes(2);
  });
});
