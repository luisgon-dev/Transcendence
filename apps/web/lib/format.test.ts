import { describe, expect, it } from "vitest";

import {
  formatCompactNumber,
  formatDurationSeconds,
  formatPercent,
  matchupVerdict,
  winRateColorClass
} from "@/lib/format";

describe("formatPercent", () => {
  it("formats ratio inputs as percent", () => {
    expect(formatPercent(0.5234)).toBe("52.3%");
    expect(formatPercent(1)).toBe("100.0%");
  });

  it("formats percent inputs as percent (auto)", () => {
    expect(formatPercent(52.34)).toBe("52.3%");
    expect(formatPercent(0)).toBe("0.0%");
  });

  it("handles invalid inputs", () => {
    expect(formatPercent(undefined)).toBe("-");
    expect(formatPercent(Number.NaN)).toBe("-");
    expect(formatPercent(Number.POSITIVE_INFINITY)).toBe("-");
  });
});

describe("formatDurationSeconds", () => {
  it("formats mm:ss", () => {
    expect(formatDurationSeconds(0)).toBe("0:00");
    expect(formatDurationSeconds(59)).toBe("0:59");
    expect(formatDurationSeconds(60)).toBe("1:00");
    expect(formatDurationSeconds(61)).toBe("1:01");
  });

  it("formats hh:mm:ss", () => {
    expect(formatDurationSeconds(3600)).toBe("1:00:00");
    expect(formatDurationSeconds(3661)).toBe("1:01:01");
  });
});

describe("formatCompactNumber", () => {
  it("uses a stable thousands suffix", () => {
    expect(formatCompactNumber(999)).toBe("999");
    expect(formatCompactNumber(1_000)).toBe("1.0k");
    expect(formatCompactNumber(12_345)).toBe("12.3k");
    expect(formatCompactNumber(-2_500)).toBe("-2.5k");
  });

  it("supports caller-specific invalid fallbacks", () => {
    expect(formatCompactNumber(undefined)).toBe("—");
    expect(formatCompactNumber(Number.NaN, { fallback: "-" })).toBe("-");
  });
});

describe("shared win-rate thresholds", () => {
  it("keeps verdicts and colors aligned at the boundaries", () => {
    expect(matchupVerdict(0.52)).toBe("Favored");
    expect(winRateColorClass(0.52)).toBe("text-wr-high");
    expect(matchupVerdict(0.48)).toBe("Even");
    expect(winRateColorClass(0.48)).toBe("");
    expect(matchupVerdict(47.9)).toBe("Unfavored");
    expect(winRateColorClass(47.9)).toBe("text-wr-low");
  });
});
