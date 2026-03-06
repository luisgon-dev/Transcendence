import { describe, expect, it } from "vitest";

import {
  analyticsRegionLabel,
  normalizeAnalyticsRegionCode,
  type AnalyticsRegionOption
} from "@/lib/analyticsRegionShared";

const OPTIONS: AnalyticsRegionOption[] = [
  { code: "ALL", label: "Global", isDefault: true },
  { code: "NA1", label: "North America" },
  { code: "EUW1", label: "Europe West" }
];

describe("normalizeAnalyticsRegionCode", () => {
  it("returns the default region when no value is provided", () => {
    expect(normalizeAnalyticsRegionCode(undefined, OPTIONS)).toBe("ALL");
  });

  it("normalizes valid region values", () => {
    expect(normalizeAnalyticsRegionCode("na1", OPTIONS)).toBe("NA1");
    expect(normalizeAnalyticsRegionCode("euw1", OPTIONS)).toBe("EUW1");
  });

  it("falls back to default for unsupported regions", () => {
    expect(normalizeAnalyticsRegionCode("KR", OPTIONS)).toBe("ALL");
  });
});

describe("analyticsRegionLabel", () => {
  it("returns the configured label for known regions", () => {
    expect(analyticsRegionLabel("NA1", OPTIONS)).toBe("North America");
  });

  it("uses the default label when the region is not supported", () => {
    expect(analyticsRegionLabel("KR", OPTIONS)).toBe("Global");
  });
});
