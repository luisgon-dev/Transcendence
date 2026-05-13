import { describe, expect, it } from "vitest";

import {
  buildPatchPreservingParams,
  getVisibleAnalyticsPatches,
  normalizeAnalyticsPatch
} from "./lolPatchFilters";

describe("normalizeAnalyticsPatch", () => {
  it("trims patch values", () => {
    expect(normalizeAnalyticsPatch(" 15.1 ")).toBe("15.1");
  });

  it("treats blank patches as absent", () => {
    expect(normalizeAnalyticsPatch("")).toBeNull();
    expect(normalizeAnalyticsPatch("   ")).toBeNull();
    expect(normalizeAnalyticsPatch(null)).toBeNull();
  });
});

describe("buildPatchPreservingParams", () => {
  it("keeps meaningful query params and drops ALL-like defaults", () => {
    const params = buildPatchPreservingParams({
      role: "MIDDLE",
      rankTier: "all",
      region: "KR",
      patch: "15.1",
      empty: ""
    });

    expect(params.toString()).toBe("role=MIDDLE&region=KR&patch=15.1");
  });
});

describe("getVisibleAnalyticsPatches", () => {
  it("keeps the active patch and the three most recent historical patches", () => {
    const patches = [
      buildPatch("15.4", true),
      buildPatch("15.3"),
      buildPatch("15.2"),
      buildPatch("15.1"),
      buildPatch("14.24"),
      buildPatch("14.23")
    ];

    expect(getVisibleAnalyticsPatches(patches).map((patch) => patch.patch)).toEqual([
      "15.4",
      "15.3",
      "15.2",
      "15.1"
    ]);
  });

  it("preserves active patches even when they are not first", () => {
    const patches = [
      buildPatch("15.3"),
      buildPatch("15.2"),
      buildPatch("15.1"),
      buildPatch("14.24"),
      buildPatch("15.4", true)
    ];

    expect(getVisibleAnalyticsPatches(patches).map((patch) => patch.patch)).toEqual([
      "15.3",
      "15.2",
      "15.1",
      "15.4"
    ]);
  });
});

function buildPatch(patch: string, isActive = false) {
  return {
    patch,
    releasedAtUtc: null,
    detectedAtUtc: null,
    isActive,
    rankedSoloDuoMatchCount: 0
  };
}
