import { render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { BuildResourceDetail } from "@/components/BuildResourceDetail";
import type { BuildResourceDetailResponse } from "@/lib/buildResources";

vi.mock("@/lib/analyticsFeatureFlags", () => ({
  analyticsFeatureFlags: async () => ({ buildLab: false, championRecommendations: false, buildReferenceLinks: false })
}));

function response(overrides: Partial<BuildResourceDetailResponse> = {}): BuildResourceDetailResponse {
  const championStats = Array.from({ length: 12 }, (_, index) => ({
    championId: index + 1,
    role: index < 9 ? "BOTTOM" : "MIDDLE",
    games: 1_000 - index * 10,
    wins: 520,
    winRate: 0.52,
    pickRate: 0.6,
    shareOfResourceUses: 0.08
  }));
  return {
    resourceType: "item",
    patch: "16.20",
    region: "KR",
    totalParticipantGames: 100_000,
    resource: { resourceId: 3031, name: "Infinity Edge", games: 76_008, wins: 42_000, winRate: 0.553, pickRate: 0.12, topChampions: [] },
    championStats,
    purchases: {
      kind: "ITEM",
      patch: "16.20",
      region: "ALL",
      games: 30_300,
      averageMinutes: 19.5,
      stages: [
        { stage: 2, games: 30_232, shareOfStage: 0.18, winRate: 0.531, adjustedWinRate: 0.532, stageWinRate: 0.52, confidenceLow: 0.52, confidenceHigh: 0.54, averageMinutes: 19, isLowSample: false },
        { stage: 6, games: 68, shareOfStage: 0.01, winRate: 0.58, adjustedWinRate: 0.51, stageWinRate: 0.52, confidenceLow: 0.4, confidenceHigh: 0.62, averageMinutes: 35.8, isLowSample: true }
      ]
    },
    ...overrides
  };
}

async function renderDetail(body: BuildResourceDetailResponse) {
  render(
    await BuildResourceDetail({
      kind: "items",
      response: body,
      champions: null,
      itemMap: null,
      itemDetail: null,
      runeData: null,
      activeRegionLabel: "Korea"
    })
  );
}

describe("BuildResourceDetail", () => {
  it("shows when the item is finished, with the gold-adjusted edge per slot", async () => {
    await renderDetail(response());

    const section = screen.getByRole("region", { name: "When it's bought" });
    expect(within(section).getByText(/Finished at 19:30 on average · 30,300 completions · all regions/)).toBeTruthy();
    const rows = within(section).getAllByRole("row").slice(1);
    expect(within(rows[0]).getByText("2nd item")).toBeTruthy();
    expect(within(rows[0]).getByText("19:00")).toBeTruthy();
    expect(within(rows[0]).getByText("+1.2 pp")).toBeTruthy();
    expect(within(rows[1]).getByText("6th item")).toBeTruthy();
    expect(within(section).getByText(/Faded rows have under 100 games/)).toBeTruthy();
  });

  it("splits uses by role, folds the long champion tail, and hides timing when none was counted", async () => {
    await renderDetail(response({ purchases: null }));

    expect(screen.queryByRole("region", { name: "When it's bought" })).toBeNull();
    const roles = screen.getByRole("region", { name: "By role" });
    expect(within(roles).getAllByRole("listitem").map((item) => item.textContent)).toEqual([
      expect.stringContaining("Bottom"),
      expect.stringContaining("Middle")
    ]);
    expect(screen.getByText("2 more")).toBeTruthy();
  });
});
