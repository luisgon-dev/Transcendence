import { describe, expect, it } from "vitest";

import { deriveRecentForm } from "@/lib/matchPerformance";
import type { MatchSummary } from "@/components/lol-profile/shared";

function match(matchDate: number, score: number): Pick<MatchSummary, "matchDate" | "performance"> {
  return {
    matchDate,
    performance: {
      score,
      teamRank: 1,
      teamSize: 5,
      label: null,
      killParticipation: 0.5,
      damageShare: 0.2,
      goldShare: 0.2,
      visionShare: 0.2,
      csPerMin: 6
    }
  };
}

describe("deriveRecentForm", () => {
  it("requires at least eight scored games", () => {
    expect(deriveRecentForm(Array.from({ length: 7 }, (_, index) => match(index, 7)))).toBeNull();
  });

  it("ignores matches without enough teammates for a comparison", () => {
    const matches = Array.from({ length: 10 }, (_, index) => {
      const value = match(index, 7);
      value.performance = { ...value.performance!, teamSize: 1 };
      return value;
    });

    expect(deriveRecentForm(matches)).toBeNull();
  });

  it("compares the latest five games with the previous sample by date", () => {
    const matches = [
      ...Array.from({ length: 5 }, (_, index) => match(10 - index, 8)),
      ...Array.from({ length: 5 }, (_, index) => match(5 - index, 6))
    ];

    expect(deriveRecentForm(matches)).toMatchObject({
      tone: "up",
      label: "Trending up",
      recentAverage: 8,
      previousAverage: 6,
      delta: 2,
      recentGames: 5,
      previousGames: 5
    });
  });

  it("treats changes under half a point as steady", () => {
    const matches = [
      ...Array.from({ length: 5 }, (_, index) => match(10 - index, 7.2)),
      ...Array.from({ length: 5 }, (_, index) => match(5 - index, 7))
    ];

    expect(deriveRecentForm(matches)?.tone).toBe("steady");
  });

  it("reports a downward trajectory", () => {
    const matches = [
      ...Array.from({ length: 5 }, (_, index) => match(10 - index, 5.5)),
      ...Array.from({ length: 5 }, (_, index) => match(5 - index, 7.5))
    ];

    expect(deriveRecentForm(matches)?.tone).toBe("down");
  });
});
