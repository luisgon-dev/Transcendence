import { describe, expect, it } from "vitest";
import type { components } from "@transcendence/api-client";

import {
  decodeGrade,
  decodeTierScopeConfidence,
  decodeTierGrade,
  decodeTierMovement,
  deriveTierScopeConfidence,
  filterTierListEntries,
  formatStrengthDelta,
  normalizeTierListEntries,
  summarizeTierListEntries
} from "@/lib/tierlist";

describe("decodeTierGrade", () => {
  it("maps numeric enum values", () => {
    expect(decodeTierGrade(0)).toBe("S");
    expect(decodeTierGrade(1)).toBe("A");
    expect(decodeTierGrade(2)).toBe("B");
    expect(decodeTierGrade(3)).toBe("C");
    expect(decodeTierGrade(4)).toBe("D");
  });

  it("maps string values", () => {
    expect(decodeTierGrade("S")).toBe("S");
    expect(decodeTierGrade("A")).toBe("A");
    expect(decodeTierGrade("B")).toBe("B");
    expect(decodeTierGrade("C")).toBe("C");
    expect(decodeTierGrade("D")).toBe("D");
    expect(decodeTierGrade("s")).toBe("S");
  });

  it("returns null for unknown values", () => {
    expect(decodeTierGrade(99 as unknown as components["schemas"]["TierGrade"])).toBeNull();
    expect(decodeTierGrade("X")).toBeNull();
    expect(decodeTierGrade(undefined)).toBeNull();
  });
});

describe("decodeTierMovement", () => {
  it("maps numeric enum values", () => {
    expect(decodeTierMovement(0)).toBe("NEW");
    expect(decodeTierMovement(1)).toBe("UP");
    expect(decodeTierMovement(2)).toBe("DOWN");
    expect(decodeTierMovement(3)).toBe("SAME");
  });

  it("maps string values", () => {
    expect(decodeTierMovement("NEW")).toBe("NEW");
    expect(decodeTierMovement("UP")).toBe("UP");
    expect(decodeTierMovement("DOWN")).toBe("DOWN");
    expect(decodeTierMovement("SAME")).toBe("SAME");
    expect(decodeTierMovement("up")).toBe("UP");
  });

  it("falls back to SAME for unknown values", () => {
    expect(decodeTierMovement(99 as unknown as components["schemas"]["TierMovement"])).toBe(
      "SAME"
    );
    expect(decodeTierMovement("SIDEWAYS")).toBe("SAME");
    expect(decodeTierMovement(undefined)).toBe("SAME");
  });
});

describe("tier scope confidence", () => {
  it("decodes numeric and string API values", () => {
    expect(decodeTierScopeConfidence(0)).toBe("RESOLVED");
    expect(decodeTierScopeConfidence(1)).toBe("FLAT");
    expect(decodeTierScopeConfidence(2)).toBe("INSUFFICIENT");
    expect(decodeTierScopeConfidence("flat")).toBe("FLAT");
    expect(decodeTierScopeConfidence(99)).toBeNull();
  });

  it("derives compatibility confidence from normalized entries", () => {
    expect(deriveTierScopeConfidence([])).toBe("INSUFFICIENT");
    expect(deriveTierScopeConfidence([{ tier: "B", isLowSample: true }])).toBe("INSUFFICIENT");
    expect(
      deriveTierScopeConfidence([
        { tier: "B", isLowSample: false },
        { tier: "B", isLowSample: false }
      ])
    ).toBe("FLAT");
    expect(
      deriveTierScopeConfidence([
        { tier: "A", isLowSample: false },
        { tier: "B", isLowSample: false }
      ])
    ).toBe("RESOLVED");
  });
});

describe("normalizeTierListEntries", () => {
  it("normalizes numeric enum payloads", () => {
    const payload: components["schemas"]["TierListEntry"][] = [
      {
        championId: 266,
        role: "TOP",
        tier: 0,
        winRate: 0.52,
        pickRate: 0.13,
        banRate: 0.05,
        games: 1240,
        movement: 1,
        previousTier: 1,
        strengthScore: 0.032,
        contestedScore: 0.21,
        roleBaseline: 0.5,
        isLowSample: false
      }
    ];

    expect(normalizeTierListEntries(payload)).toEqual([
      {
        championId: 266,
        role: "TOP",
        tier: "S",
        winRate: 0.52,
        pickRate: 0.13,
        banRate: 0.05,
        games: 1240,
        movement: "UP",
        previousTier: "A",
        strengthScore: 0.032,
        contestedScore: 0.21,
        isLowSample: false
      }
    ]);
  });

  it("supports string enum payloads for compatibility and defaults missing fields", () => {
    const payload = [
      {
        championId: 103,
        role: "MIDDLE",
        tier: "A",
        winRate: 0.5,
        pickRate: 0.08,
        movement: "DOWN",
        previousTier: "S"
      }
    ] as unknown as components["schemas"]["TierListEntry"][];

    expect(normalizeTierListEntries(payload)).toEqual([
      {
        championId: 103,
        role: "MIDDLE",
        tier: "A",
        winRate: 0.5,
        pickRate: 0.08,
        banRate: 0,
        games: 0,
        movement: "DOWN",
        previousTier: "S",
        strengthScore: 0,
        contestedScore: 0,
        isLowSample: false
      }
    ]);
  });

  it("drops entries with unknown tier and avoids throw", () => {
    const payload = [
      {
        championId: 55,
        role: "MIDDLE",
        tier: 99,
        movement: 0
      }
    ] as unknown as components["schemas"]["TierListEntry"][];

    expect(normalizeTierListEntries(payload)).toEqual([]);
  });

  it("handles null or missing entries", () => {
    expect(normalizeTierListEntries(null)).toEqual([]);
    expect(normalizeTierListEntries(undefined)).toEqual([]);
  });
});

describe("filterTierListEntries", () => {
  const entries = [
    {
      championId: 266,
      role: "TOP",
      tier: "S",
      winRate: 0.52,
      pickRate: 0.13,
      banRate: 0.05,
      games: 1240,
      movement: "UP",
      previousTier: "A",
      strengthScore: 0.032,
      contestedScore: 0.21,
      isLowSample: false
    },
    {
      championId: 103,
      role: "MIDDLE",
      tier: "A",
      winRate: 0.5,
      pickRate: 0.08,
      banRate: 0.03,
      games: 987,
      movement: "DOWN",
      previousTier: "S",
      strengthScore: 0.012,
      contestedScore: 0.14,
      isLowSample: false
    }
  ] as const;

  const champions = {
    "103": { id: "Ahri", name: "Ahri", title: "the Nine-Tailed Fox" },
    "266": { id: "Aatrox", name: "Aatrox", title: "the Darkin Blade" }
  };

  it("filters by champion name, slug, title, or champion id", () => {
    expect(filterTierListEntries(entries, champions, { query: "aatr" })).toEqual([entries[0]]);
    expect(filterTierListEntries(entries, champions, { query: "nine-tailed" })).toEqual([entries[1]]);
    expect(filterTierListEntries(entries, champions, { query: "103" })).toEqual([entries[1]]);
  });

  it("respects the focused tier when present", () => {
    expect(filterTierListEntries(entries, champions, { focusTier: "S" })).toEqual([entries[0]]);
    expect(filterTierListEntries(entries, champions, { focusTier: "D" })).toEqual([]);
  });
});

describe("summarizeTierListEntries", () => {
  it("returns aggregate table stats for the current view", () => {
    expect(
      summarizeTierListEntries([
        {
          championId: 266,
          role: "TOP",
          tier: "S",
          winRate: 0.52,
          pickRate: 0.13,
          banRate: 0.05,
          games: 1240,
          movement: "UP",
          previousTier: "A",
          strengthScore: 0.032,
          contestedScore: 0.21,
          isLowSample: false
        },
        {
          championId: 103,
          role: "MIDDLE",
          tier: "A",
          winRate: 0.5,
          pickRate: 0.08,
          banRate: 0.03,
          games: 987,
          movement: "DOWN",
          previousTier: "S",
          strengthScore: 0.012,
          contestedScore: 0.14,
          isLowSample: false
        }
      ])
    ).toEqual({
      visibleCount: 2,
      totalGames: 2227,
      averageWinRate: 0.51,
      topWinRate: 0.52,
      tierCounts: {
        S: 1,
        A: 1,
        B: 0,
        C: 0,
        D: 0
      }
    });
  });

  it("returns an empty summary shape for empty views", () => {
    expect(summarizeTierListEntries([])).toEqual({
      visibleCount: 0,
      totalGames: 0,
      averageWinRate: null,
      topWinRate: null,
      tierCounts: {
        S: 0,
        A: 0,
        B: 0,
        C: 0,
        D: 0
      }
    });
  });
});

describe("decodeGrade", () => {
  it("decodes a full grade payload", () => {
    const grade = decodeGrade({
      tier: 0,
      strengthScore: 0.034,
      winRate: 0.531,
      pickRate: 0.12,
      banRate: 0.04,
      contestedScore: 0.16,
      games: 24000,
      roleBaseline: 0.5,
      isLowSample: false,
      movement: 1,
      previousTier: 1,
      role: "BOTTOM",
      rankScope: "EMERALD_PLUS"
    });

    expect(grade).toEqual({
      tier: "S",
      strengthScore: 0.034,
      winRate: 0.531,
      pickRate: 0.12,
      banRate: 0.04,
      contestedScore: 0.16,
      games: 24000,
      roleBaseline: 0.5,
      isLowSample: false,
      movement: "UP",
      previousTier: "A",
      role: "BOTTOM"
    });
  });

  it("returns null when there is no grade or the tier won't decode", () => {
    expect(decodeGrade(null)).toBeNull();
    expect(decodeGrade(undefined)).toBeNull();
    expect(
      decodeGrade({ tier: 99 } as unknown as components["schemas"]["ChampionGradeDto"])
    ).toBeNull();
  });
});

describe("formatStrengthDelta", () => {
  it("formats a signed win-rate delta", () => {
    expect(formatStrengthDelta(0.032)).toBe("+3.2%");
    expect(formatStrengthDelta(-0.018)).toBe("−1.8%");
    expect(formatStrengthDelta(0)).toBe("0.0%");
  });
});
