import { describe, expect, it } from "vitest";

import {
  normalizeRankTierParam,
  rankEmblemUrl,
  rankTierDisplayLabel
} from "@/lib/ranks";

describe("normalizeRankTierParam", () => {
  it("normalizes supported values", () => {
    expect(normalizeRankTierParam("emerald_plus")).toBe("EMERALD_PLUS");
    expect(normalizeRankTierParam("Emerald+")).toBe("EMERALD_PLUS");
    expect(normalizeRankTierParam("diamond")).toBe("DIAMOND");
  });

  it("treats all/empty as no filter", () => {
    expect(normalizeRankTierParam("all")).toBeNull();
    expect(normalizeRankTierParam(undefined)).toBeNull();
  });
});

describe("rankTierDisplayLabel", () => {
  it("maps display labels", () => {
    expect(rankTierDisplayLabel("EMERALD_PLUS")).toBe("Emerald+");
    expect(rankTierDisplayLabel("GRANDMASTER")).toBe("Grandmaster");
    expect(rankTierDisplayLabel("all")).toBe("All Ranks");
  });
});

describe("rankEmblemUrl", () => {
  it("builds cdragon URLs for ranked tiers", () => {
    expect(rankEmblemUrl("DIAMOND")).toContain("/ranked-emblem/emblem-diamond.png");
  });

  it("returns null for non-ranked scope tokens", () => {
    expect(rankEmblemUrl("EMERALD_PLUS")).toBeNull();
    expect(rankEmblemUrl("UNRANKED")).toBeNull();
  });
});
