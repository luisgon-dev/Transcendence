import { describe, expect, it } from "vitest";

import { leaderboardSearchParams, normalizeLeaderboardFilters } from "./leaderboards";

describe("leaderboard filters", () => {
  it("normalizes supported filters", () => {
    expect(normalizeLeaderboardFilters({ region: "EUW", queue: "FLEX", championId: "157", role: "midDle" }))
      .toEqual({ region: "euw", queue: "flex", championId: 157, role: "MIDDLE" });
  });

  it("drops champion-only filters when no valid champion is selected", () => {
    expect(normalizeLeaderboardFilters({ championId: "0", role: "TOP" }))
      .toEqual({ region: "na", queue: "solo", championId: null, role: null });
  });

  it("serializes a stable API query", () => {
    const query = leaderboardSearchParams({ region: "kr", queue: "solo", championId: 103, role: "MIDDLE" });
    expect(query.toString()).toBe("region=kr&queue=solo&championId=103&role=MIDDLE");
  });
});
