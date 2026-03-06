import { describe, expect, it } from "vitest";

import {
  shouldUseFallbackAnalyticsRegions,
  type AnalyticsRegionOption
} from "@/lib/analyticsRegionShared";

describe("shouldUseFallbackAnalyticsRegions", () => {
  it("uses fallback when no options are available", () => {
    expect(shouldUseFallbackAnalyticsRegions([])).toBe(true);
  });

  it("uses fallback when the backend only returns the global region", () => {
    const options: AnalyticsRegionOption[] = [{ code: "ALL", label: "Global", isDefault: true }];

    expect(shouldUseFallbackAnalyticsRegions(options)).toBe(true);
  });

  it("keeps backend regions when at least one non-global region is available", () => {
    const options: AnalyticsRegionOption[] = [
      { code: "ALL", label: "Global", isDefault: true },
      { code: "NA1", label: "North America" }
    ];

    expect(shouldUseFallbackAnalyticsRegions(options)).toBe(false);
  });
});
