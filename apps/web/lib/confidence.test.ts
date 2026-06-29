import { describe, expect, it } from "vitest";

import {
  adjustedWinRatePercent,
  confidenceLevel,
  confidenceSampleLabel,
  DEFAULT_MIN_GAMES,
  formatEbStory,
  winRateIntervalPP
} from "@/lib/confidence";

describe("confidenceLevel", () => {
  it("is low when explicitly flagged low sample, regardless of game count", () => {
    expect(confidenceLevel({ games: 100_000, isLowSample: true })).toBe("low");
  });

  it("is low at zero or negative games", () => {
    expect(confidenceLevel({ games: 0 })).toBe("low");
    expect(confidenceLevel({ games: -5 })).toBe("low");
  });

  it("is high at or above the minimum", () => {
    expect(confidenceLevel({ games: 300, minGames: 300 })).toBe("high");
    expect(confidenceLevel({ games: 301, minGames: 300 })).toBe("high");
  });

  it("is moderate in the band between 40% of min and the min", () => {
    // min 300 -> moderate floor is max(20, 120) = 120
    expect(confidenceLevel({ games: 120, minGames: 300 })).toBe("moderate");
    expect(confidenceLevel({ games: 299, minGames: 300 })).toBe("moderate");
    expect(confidenceLevel({ games: 119, minGames: 300 })).toBe("low");
  });

  it("falls back to DEFAULT_MIN_GAMES when minGames is missing or non-positive", () => {
    expect(confidenceLevel({ games: DEFAULT_MIN_GAMES })).toBe("high");
    expect(confidenceLevel({ games: DEFAULT_MIN_GAMES - 1 })).toBe("moderate");
    // moderate floor = max(20, 0.4 * 200) = 80
    expect(confidenceLevel({ games: 80 })).toBe("moderate");
    expect(confidenceLevel({ games: 79 })).toBe("low");
    // non-positive minGames is ignored in favor of the default
    expect(confidenceLevel({ games: DEFAULT_MIN_GAMES, minGames: 0 })).toBe("high");
  });

  it("honors a small minimum via the max(20, ...) moderate floor", () => {
    // min 30 -> moderate floor is max(20, 12) = 20
    expect(confidenceLevel({ games: 30, minGames: 30 })).toBe("high");
    expect(confidenceLevel({ games: 20, minGames: 30 })).toBe("moderate");
    expect(confidenceLevel({ games: 19, minGames: 30 })).toBe("low");
  });
});

describe("winRateIntervalPP", () => {
  it("is zero with no games", () => {
    expect(winRateIntervalPP(50, 0)).toBe(0);
    expect(winRateIntervalPP(50, -10)).toBe(0);
  });

  it("narrows as the sample grows", () => {
    const small = winRateIntervalPP(50, 100);
    const large = winRateIntervalPP(50, 1000);
    expect(large).toBeLessThan(small);
    expect(small).toBeGreaterThan(0);
  });

  it("is widest near a coin-flip and narrower at the extremes", () => {
    const middle = winRateIntervalPP(50, 200);
    const skewed = winRateIntervalPP(90, 200);
    expect(middle).toBeGreaterThan(skewed);
  });

  it("normalizes ratio and percent inputs identically", () => {
    expect(winRateIntervalPP(0.5, 200)).toBeCloseTo(winRateIntervalPP(50, 200), 10);
  });

  it("clamps to a 25pp ceiling on tiny samples", () => {
    expect(winRateIntervalPP(50, 1)).toBeLessThanOrEqual(25);
    expect(winRateIntervalPP(50, 1)).toBe(25);
  });
});

describe("adjustedWinRatePercent", () => {
  it("adds a fractional strength delta to a ratio baseline (percent output)", () => {
    expect(adjustedWinRatePercent({ roleBaseline: 0.504, strengthScore: 0.029 })).toBeCloseTo(
      53.3,
      10
    );
  });

  it("adds a points-scaled strength delta to a percent baseline", () => {
    expect(adjustedWinRatePercent({ roleBaseline: 50.4, strengthScore: 2.9 })).toBeCloseTo(
      53.3,
      10
    );
  });

  it("handles negative deltas", () => {
    expect(adjustedWinRatePercent({ roleBaseline: 0.5, strengthScore: -0.02 })).toBeCloseTo(
      48,
      10
    );
  });
});

describe("confidenceSampleLabel", () => {
  it("maps levels onto the analytics sample vocabulary", () => {
    expect(confidenceSampleLabel("high")).toBe("Stable");
    expect(confidenceSampleLabel("moderate")).toBe("Light");
    expect(confidenceSampleLabel("low")).toBe("Early");
  });
});

describe("formatEbStory", () => {
  it("renders the one-line EB trust story with the graded (shrunk) win rate", () => {
    expect(
      formatEbStory({ winRate: 0.58, roleBaseline: 0.504, strengthScore: 0.029, games: 120 })
    ).toBe("Observed 58.0% over 120 games · role avg 50.4% · graded 53.3% (+2.9% vs role avg)");
  });

  it("omits the role-average clause when no baseline is on the wire", () => {
    expect(
      formatEbStory({ winRate: 0.58, roleBaseline: 0, strengthScore: 0.029, games: 120 })
    ).toBe("Observed 58.0% over 120 games · graded +2.9% vs role average");
  });
});
