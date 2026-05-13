import { describe, expect, it } from "vitest";

import { buildPatchPreservingParams, normalizeAnalyticsPatch } from "./lolPatchFilters";

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
