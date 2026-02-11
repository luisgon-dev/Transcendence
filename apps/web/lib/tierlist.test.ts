import { describe, expect, it } from "vitest";
import type { components } from "@transcendence/api-client/schema";

import {
  decodeTierGrade,
  decodeTierMovement,
  normalizeTierListEntries
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

describe("normalizeTierListEntries", () => {
  it("normalizes numeric enum payloads", () => {
    const payload: components["schemas"]["TierListEntry"][] = [
      {
        championId: 266,
        role: "TOP",
        tier: 0,
        compositeScore: 0.71,
        winRate: 0.52,
        pickRate: 0.13,
        games: 1240,
        movement: 1,
        previousTier: 1
      }
    ];

    expect(normalizeTierListEntries(payload)).toEqual([
      {
        championId: 266,
        role: "TOP",
        tier: "S",
        compositeScore: 0.71,
        winRate: 0.52,
        pickRate: 0.13,
        games: 1240,
        movement: "UP",
        previousTier: "A"
      }
    ]);
  });

  it("supports string enum payloads for compatibility", () => {
    const payload = [
      {
        championId: 103,
        role: "MIDDLE",
        tier: "A",
        compositeScore: 0.64,
        winRate: 0.5,
        pickRate: 0.08,
        games: 987,
        movement: "DOWN",
        previousTier: "S"
      }
    ] as unknown as components["schemas"]["TierListEntry"][];

    expect(normalizeTierListEntries(payload)).toEqual([
      {
        championId: 103,
        role: "MIDDLE",
        tier: "A",
        compositeScore: 0.64,
        winRate: 0.5,
        pickRate: 0.08,
        games: 987,
        movement: "DOWN",
        previousTier: "S"
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
