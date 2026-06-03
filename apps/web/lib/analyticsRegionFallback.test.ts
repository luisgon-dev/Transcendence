import { describe, expect, it, vi } from "vitest";

import {
  fetchWithGlobalAnalyticsRegionFallback,
  resolveAnalyticsRegionPresentation,
} from "@/lib/analyticsRegionFallback";
import { type AnalyticsRegionOption } from "@/lib/analyticsRegionShared";

const OPTIONS: AnalyticsRegionOption[] = [
  { code: "ALL", label: "Global", isDefault: true },
  { code: "NA1", label: "North America" },
  { code: "EUW1", label: "Europe West" },
];

describe("fetchWithGlobalAnalyticsRegionFallback", () => {
  it("returns the initial result when the region fetch succeeds", async () => {
    const fetcher = vi.fn(async (_region: string) => {
      void _region;
      return {
        ok: true as const,
        body: ["ok"],
      };
    });

    const result = await fetchWithGlobalAnalyticsRegionFallback(
      "NA1",
      fetcher,
    );

    expect(result.usedGlobalFallback).toBe(false);
    expect(result.result.body).toEqual(["ok"]);
    expect(fetcher).toHaveBeenCalledTimes(1);
    expect(fetcher).toHaveBeenCalledWith("NA1");
  });

  it("retries with Global when a non-global region fails", async () => {
    const fetcher = vi
      .fn<(region: string) => Promise<{ ok: boolean; source: string }>>()
      .mockImplementation(async (region: string) =>
        region === "ALL"
          ? { ok: true, source: "global" }
          : { ok: false, source: "regional" },
      );

    const result = await fetchWithGlobalAnalyticsRegionFallback(
      "EUW1",
      fetcher,
    );

    expect(result.usedGlobalFallback).toBe(true);
    expect(result.result).toEqual({ ok: true, source: "global" });
    expect(fetcher).toHaveBeenNthCalledWith(1, "EUW1");
    expect(fetcher).toHaveBeenNthCalledWith(2, "ALL");
  });
});

describe("resolveAnalyticsRegionPresentation", () => {
  it("preserves the requested region when no fallback is used", () => {
    expect(
      resolveAnalyticsRegionPresentation(
        "NA1",
        "North America",
        OPTIONS,
        false,
      ),
    ).toEqual({
      effectiveRegion: "NA1",
      effectiveRegionLabel: "North America",
      fallbackMessage: null,
    });
  });

  it("switches labels and warning text when global fallback is used", () => {
    expect(
      resolveAnalyticsRegionPresentation(
        "EUW1",
        "Europe West",
        OPTIONS,
        true,
      ),
    ).toEqual({
      effectiveRegion: "ALL",
      effectiveRegionLabel: "Global",
      fallbackMessage:
        "Europe West data is unavailable right now. Showing Global instead.",
    });
  });
});
