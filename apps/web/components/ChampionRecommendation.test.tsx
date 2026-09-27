import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { ChampionRecommendation, type ChampionRecommendationSummary } from "./ChampionRecommendation";
import type { BuildLabOption } from "@/lib/buildLab";

const items = { "6672": { name: "Kraken Slayer" }, "3031": { name: "Infinity Edge" } };
const runeById = {
  "8005": { name: "Press the Attack", icon: "perk-images/Styles/precision.png" },
  "9111": { name: "Triumph", icon: "perk-images/Styles/triumph.png" }
};
const spells = { "4": { id: "SummonerFlash", name: "Flash" }, "7": { id: "SummonerHeal", name: "Heal" } };

function option(overrides: Partial<BuildLabOption> = {}): BuildLabOption {
  return {
    actionKey: "6672",
    actionIds: [6672],
    games: 1240,
    pickRate: 0.36,
    winRate: 0.524,
    adjustedWinRate: 0.517,
    lift: 0.014,
    confidenceLow: 0.49,
    confidenceHigh: 0.544,
    averageTimingMinutes: 13.5,
    isLowSample: false,
    ...overrides
  };
}

function summary(overrides: Partial<ChampionRecommendationSummary> = {}): ChampionRecommendationSummary {
  return {
    available: true,
    coverage: {
      includedPatches: ["26.14", "26.13"],
      patchWeights: [1, 0.6],
      countedMatches: 412_000,
      lastCountedAtUtc: null,
      includedRegions: ["NA1"],
      rankScope: "ALL_TRACKED"
    },
    summary: {
      items: [option(), option({ actionKey: "3031", actionIds: [3031] })],
      runePage: option({ actionKey: "8005+9111", actionIds: [8005, 9111] }),
      spellPair: option({ actionKey: "4+7", actionIds: [4, 7] }),
      skillPriority: option({ actionKey: "1+3+2", actionIds: [1, 3, 2] })
    },
    unavailableReason: null,
    ...overrides
  };
}

function renderRecommendation(recommendation: ChampionRecommendationSummary, pageRankTier?: string | null) {
  return render(
    <ChampionRecommendation
      recommendation={recommendation}
      championId={103}
      role="MIDDLE"
      patch="26.14"
      region="NA1"
      pageRankTier={pageRankTier}
      itemVersion="16.14.1"
      items={items}
      runeById={runeById}
      spellVersion="16.14.1"
      spells={spells}
    />
  );
}

describe("ChampionRecommendation", () => {
  it("shows each choice with its adjusted win rate, lift, pick rate and games", () => {
    renderRecommendation(summary());

    // The core build is the item path, read in order; its numbers are the first item's.
    expect(screen.getByText("Core build")).toBeTruthy();
    expect(screen.getByText("Kraken Slayer → Infinity Edge")).toBeTruthy();
    expect(screen.getAllByText("51.7%")).toHaveLength(4);
    expect(screen.getAllByText("+1.4 pp")).toHaveLength(4);
    expect(screen.getAllByText("1,240 games")).toHaveLength(4);
    expect(screen.getByText("Q › E › W")).toBeTruthy();
    // A rune page is named for its keystone, not a list of every rune on it.
    expect(screen.getByText("Press the Attack")).toBeTruthy();
    expect(screen.getByText("Flash + Heal")).toBeTruthy();
  });

  it("names every pooled patch rather than implying the newest alone", () => {
    renderRecommendation(summary());

    expect(screen.getByText("Patches 26.14, 26.13")).toBeTruthy();
  });

  it("states that a rank filter on the page does not narrow the counts", () => {
    renderRecommendation(summary(), "GOLD");

    expect(screen.getByText("All tracked ranks")).toBeTruthy();
    expect(screen.getByText(/the Gold filter above does not change it/)).toBeTruthy();
  });

  it("says nothing about rank when the page is not filtered", () => {
    renderRecommendation(summary(), "ALL");

    expect(screen.queryByText(/filter above/)).toBeNull();
  });

  it("marks a missing choice instead of leaving the slot blank", () => {
    renderRecommendation(summary({ summary: { ...summary().summary!, spellPair: null } }));

    expect(screen.getByText("Not enough games yet")).toBeTruthy();
  });

  it("links into Build Lab with the same role, patch and region", () => {
    renderRecommendation(summary());

    expect(screen.getByRole("link", { name: "Explore in Build Lab" }).getAttribute("href")).toBe(
      "/lol/builds/103?role=MIDDLE&patch=26.14&region=NA1"
    );
  });
});
