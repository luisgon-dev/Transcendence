import { describe, expect, it } from "vitest";

import { selectStarterPicks } from "@/lib/homeGuidance";
import type { ChampionMap } from "@/lib/staticData";
import type { UITierListEntry } from "@/lib/tierlist";

function entry(
  championId: number,
  role: string,
  strengthScore: number,
  overrides: Partial<UITierListEntry> = {}
): UITierListEntry {
  return {
    championId,
    role,
    tier: "A",
    winRate: 0.52,
    pickRate: 0.1,
    banRate: 0.02,
    games: 1_000,
    movement: "SAME",
    previousTier: "A",
    strengthScore,
    contestedScore: 0.1,
    isLowSample: false,
    ...overrides
  };
}

describe("selectStarterPicks", () => {
  const champions: ChampionMap["champions"] = {
    "1": { id: "Annie", name: "Annie", difficulty: 1 },
    "2": { id: "Olaf", name: "Olaf", difficulty: 4 },
    "3": { id: "Galio", name: "Galio", difficulty: 5 },
    "4": { id: "TwistedFate", name: "Twisted Fate", difficulty: 4 }
  };

  it("returns the strongest stable low-complexity pick for each available lane", () => {
    const picks = selectStarterPicks(
      [
        entry(1, "MIDDLE", 0.02),
        entry(4, "MIDDLE", 0.03),
        entry(2, "JUNGLE", 0.01),
        entry(3, "TOP", 0.05)
      ],
      champions
    );

    expect(picks.map((pick) => [pick.role, pick.champion.name])).toEqual([
      ["JUNGLE", "Olaf"],
      ["MIDDLE", "Twisted Fate"]
    ]);
  });

  it("excludes low-sample and missing-difficulty candidates", () => {
    const picks = selectStarterPicks(
      [
        entry(1, "MIDDLE", 0.03, { isLowSample: true }),
        entry(99, "BOTTOM", 0.04)
      ],
      champions
    );

    expect(picks).toEqual([]);
  });
});
