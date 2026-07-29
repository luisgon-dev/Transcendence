import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { ChampionRecommendation, type ChampionRecommendationSummary } from "./ChampionRecommendation";
import type { AdjustedActionEstimate } from "@/lib/buildLab";

const items = { "6672": { name: "Kraken Slayer" } };
const runeById = { "8005": { name: "Press the Attack", icon: "perk-images/Styles/precision.png" } };
const spells = { "4": { id: "SummonerFlash", name: "Flash" } };

function estimate(overrides: Partial<AdjustedActionEstimate> = {}): AdjustedActionEstimate {
  return {
    actionKey: "ITEM:6672",
    actionIds: [6672],
    adjustedWpa: 0.014,
    confidenceLow: 0.006,
    confidenceHigh: 0.022,
    rawWinRate: 0.524,
    pickRate: 0.36,
    observedCount: 1240,
    effectiveSampleSize: 980,
    averageTimingMinutes: 13.5,
    evidenceQuality: "STRONG",
    fallbackScope: "NONE",
    regionScope: "NA1",
    baselineDefinition: "Other first legendary items bought in the same decision.",
    isPublishable: true,
    unavailableReason: null,
    ...overrides
  };
}

function summary(
  overrides: Partial<ChampionRecommendationSummary> = {}
): ChampionRecommendationSummary {
  return {
    available: true,
    provenance: {
      generationId: "gen-1",
      datasetVersion: "ds-26.14",
      modelVersion: "wpa-3",
      staticDataVersion: "16.14.1",
      sourceCutoffUtc: null,
      generatedAtUtc: null,
      matchCount: 412_000,
      rankScope: "EMERALD_PLUS",
      includedPatches: ["26.14", "26.13"],
      includedRegions: ["NA1", "GLOBAL"]
    },
    // Deliberately no `context`: the champion-profile payload carries provenance only, so the
    // fixture has to be the shape the backend actually sends.
    firstItem: estimate(),
    rune: null,
    spellPair: null,
    unavailableReason: null,
    ...overrides
  };
}

function renderRecommendation(
  recommendation: ChampionRecommendationSummary,
  pageRankTier?: string | null
) {
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
  it("renders a negative estimate in the muted data red, never the win green", () => {
    renderRecommendation(summary({ firstItem: estimate({ adjustedWpa: -0.0081 }) }));

    const value = screen.getByText("-0.8 pp");
    expect(value.className).toContain("text-danger");
    expect(value.className).not.toContain("text-success");
  });

  it("keeps the win green for a positive estimate so the sign is what encodes the outcome", () => {
    renderRecommendation(summary());

    const value = screen.getByText("+1.4 pp");
    expect(value.className).toContain("text-success");
    expect(value.className).not.toContain("text-danger");
  });

  it("states the modeled rank scope so it cannot be read as the page's rank filter", () => {
    renderRecommendation(summary(), "GOLD");

    expect(screen.getByText("Emerald+ scope")).toBeTruthy();
    expect(
      screen.getByText(
        /Always modeled at Emerald\+ — the Gold filter above does not change it\./
      )
    ).toBeTruthy();
  });

  it("keeps the scope chip even when the page filter already matches the modeled scope", () => {
    renderRecommendation(summary(), "EMERALD_PLUS");

    expect(screen.getByText("Emerald+ scope")).toBeTruthy();
    expect(screen.queryByText(/Always modeled at/)).toBeNull();
  });

  it("discloses the effective patch from provenance, which is all the summary payload carries", () => {
    renderRecommendation(summary());

    // The promoted generation's own patch is the first included one; a borrowed prior patch is
    // modeled but never the patch the estimates resolve against.
    expect(screen.getByText("Patch 26.14")).toBeTruthy();
    expect(screen.queryByText("Patch 26.13")).toBeNull();
  });

  it("omits the patch chip rather than an empty one when the generation lists no patches", () => {
    renderRecommendation(
      summary({
        provenance: { ...summary().provenance, includedPatches: [] }
      })
    );

    expect(screen.queryByText(/^Patch /)).toBeNull();
    expect(screen.getByText("Emerald+ scope")).toBeTruthy();
  });

  it("states that no lane opponent is scoped, since the profile summary never carries one", () => {
    renderRecommendation(summary());

    expect(screen.getByText("Any lane opponent")).toBeTruthy();
  });
});
