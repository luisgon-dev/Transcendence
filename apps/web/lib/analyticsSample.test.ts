import { describe, expect, it } from "vitest";

import {
  analyticsSampleCoveragePercent,
  analyticsSampleShortfall,
  normalizeAnalyticsSample,
  normalizeAnalyticsSampleStatus,
  pickMostSevereAnalyticsSample
} from "@/lib/analyticsSample";

describe("analyticsSample", () => {
  it("normalizes enum-like statuses", () => {
    expect(normalizeAnalyticsSampleStatus(0)).toBe("sufficient");
    expect(normalizeAnalyticsSampleStatus(1)).toBe("low_sample");
    expect(normalizeAnalyticsSampleStatus(2)).toBe("no_data");
    expect(normalizeAnalyticsSampleStatus("LowSample")).toBe("low_sample");
    expect(normalizeAnalyticsSampleStatus("No_Data")).toBe("no_data");
  });

  it("normalizes sample payload", () => {
    const normalized = normalizeAnalyticsSample({
      sampleStatus: 1,
      sampleSize: 22.7,
      minimumRecommendedSampleSize: 40,
      patchAgeHours: 4.5,
      isEarlyPatchWindow: true,
      patchPhase: 0,
      isProvisional: true
    });

    expect(normalized).toEqual({
      status: "low_sample",
      sampleSize: 22,
      minimumRecommendedSampleSize: 40,
      patchAgeHours: 4.5,
      isEarlyPatchWindow: true,
      patchPhase: "bootstrap",
      isProvisional: true
    });
  });

  it("derives a low-sample state for legacy payloads without a minimum", () => {
    const normalized = normalizeAnalyticsSample({
      sampleStatus: "sufficient",
      sampleSize: 1
    });

    expect(normalized).toEqual({
      status: "low_sample",
      sampleSize: 1,
      minimumRecommendedSampleSize: 50,
      patchAgeHours: 0,
      isEarlyPatchWindow: false,
      patchPhase: "steady",
      isProvisional: false
    });
  });

  it("treats zero-game payloads as no data even when status says sufficient", () => {
    const normalized = normalizeAnalyticsSample({
      sampleStatus: "sufficient",
      sampleSize: 0
    });

    expect(normalized?.status).toBe("no_data");
    expect(normalized?.minimumRecommendedSampleSize).toBe(50);
  });

  it("picks the most severe sample state across responses", () => {
    const picked = pickMostSevereAnalyticsSample(
      { sampleStatus: "sufficient", sampleSize: 200, minimumRecommendedSampleSize: 100 },
      { sampleStatus: "low_sample", sampleSize: 20, minimumRecommendedSampleSize: 40 },
      { sampleStatus: "no_data", sampleSize: 0, minimumRecommendedSampleSize: 40 }
    );

    expect(picked?.status).toBe("no_data");
  });

  it("computes confidence coverage and shortfall", () => {
    expect(
      analyticsSampleCoveragePercent({
        sampleSize: 25,
        minimumRecommendedSampleSize: 40
      })
    ).toBe(63);
    expect(
      analyticsSampleCoveragePercent({
        sampleSize: 120,
        minimumRecommendedSampleSize: 100
      })
    ).toBe(100);
    expect(
      analyticsSampleShortfall({
        sampleSize: 25,
        minimumRecommendedSampleSize: 40
      })
    ).toBe(15);
  });
});
