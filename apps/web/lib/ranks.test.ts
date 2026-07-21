import { describe, expect, it } from "vitest";

import {
  normalizeRankTierParam,
  rankEmblemUrl,
  rankToLadderPoints,
  rankTierDisplayLabel,
  resolveDefaultedRankTier
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

describe("resolveDefaultedRankTier", () => {
  it("defaults to Emerald+ when the param is absent", () => {
    expect(resolveDefaultedRankTier(undefined)).toBe("EMERALD_PLUS");
    expect(resolveDefaultedRankTier(null)).toBe("EMERALD_PLUS");
    expect(resolveDefaultedRankTier("")).toBe("EMERALD_PLUS");
    expect(resolveDefaultedRankTier("   ")).toBe("EMERALD_PLUS");
  });

  it("treats an explicit 'all' as all-ranks (null), not the default", () => {
    expect(resolveDefaultedRankTier("all")).toBeNull();
    expect(resolveDefaultedRankTier("ALL")).toBeNull();
  });

  it("passes through valid tiers", () => {
    expect(resolveDefaultedRankTier("diamond")).toBe("DIAMOND");
    expect(resolveDefaultedRankTier("Emerald+")).toBe("EMERALD_PLUS");
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

describe("rankToLadderPoints", () => {
  it("stays monotonic across division and tier promotions", () => {
    expect(rankToLadderPoints("GOLD", "I", 90)).toBeLessThan(rankToLadderPoints("PLATINUM", "IV", 0)!);
    expect(rankToLadderPoints("PLATINUM", "IV", 80)).toBeLessThan(rankToLadderPoints("PLATINUM", "III", 0)!);
  });

  it("uses apex LP within the tier band and rejects unknown tiers", () => {
    expect(rankToLadderPoints("MASTER", null, 250)).toBeGreaterThan(rankToLadderPoints("MASTER", null, 100)!);
    expect(rankToLadderPoints("UNRANKED", null, 0)).toBeNull();
  });
});
