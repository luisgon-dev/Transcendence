import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";

import { ChampionTrendChart, type ChampionTrend } from "./ChampionTrendChart";

const trend: ChampionTrend = {
  queueFamily: "ARAM",
  role: "ALL",
  rankScope: "EMERALD_PLUS",
  region: "ALL",
  points: [
    {
      patch: "15.1",
      releasedAtUtc: "2026-01-01T00:00:00Z",
      tier: 1,
      games: 800,
      winRate: 0.51,
      pickRate: 0.08,
      banRate: 0.01,
      strengthScore: 0.01,
      isLowSample: false
    },
    {
      patch: "15.2",
      releasedAtUtc: "2026-01-15T00:00:00Z",
      tier: 0,
      games: 920,
      winRate: 0.54,
      pickRate: 0.1,
      banRate: 0.02,
      strengthScore: 0.04,
      isLowSample: false
    }
  ]
};

describe("ChampionTrendChart", () => {
  it("renders an accessible patch-over-patch win-rate chart", () => {
    const html = renderToStaticMarkup(<ChampionTrendChart championName="Ahri" trend={trend} />);

    expect(html).toContain("Win-rate trend");
    expect(html).toContain("+3.0 pp since 15.1");
    expect(html).toContain("Ahri win rate moved from 51.0% on patch 15.1 to 54.0% on patch 15.2");
    expect(html).toContain("Patch 15.2: 54.00% across 920 games");
  });

  it("does not imply a trend from a single point", () => {
    const html = renderToStaticMarkup(
      <ChampionTrendChart championName="Ahri" trend={{ ...trend, points: trend.points.slice(0, 1) }} />
    );

    expect(html).toBe("");
  });
});
