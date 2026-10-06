import { describe, expect, it } from "vitest";

import {
  crawledPlatformCount,
  formatBytes,
  formatCompactCount,
  formatCount,
  type DatasetStats
} from "@/lib/datasetStats";

function stats(overrides: Partial<DatasetStats> = {}): DatasetStats {
  return {
    matchesStored: 567_944,
    matchesLast24Hours: 21_325,
    matchesPerDayLast7Days: 23_482,
    activePatch: "16.19",
    activePatchMatches: 234_302,
    playersIndexedEstimate: 4_803_099,
    databaseSizeBytes: 324_270_000_000,
    crawledPlatforms: ["NA1", "EUW1", "KR"],
    platforms: [],
    lastMatchIngestedAtUtc: null,
    computedAtUtc: "2026-10-06T07:00:00Z",
    ...overrides
  };
}

describe("dataset stats formatting", () => {
  it("groups exact counts with fixed en-US separators", () => {
    expect(formatCount(567_944)).toBe("567,944");
    expect(formatCount(0)).toBe("0");
    expect(formatCount(null)).toBe("—");
    expect(formatCount(Number.NaN)).toBe("—");
  });

  it("compacts estimates so they do not claim false precision", () => {
    expect(formatCompactCount(4_803_099)).toBe("4.8M");
    expect(formatCompactCount(950)).toBe("950");
    expect(formatCompactCount(undefined)).toBe("—");
  });

  it("formats sizes in binary units the way pg_size_pretty labels them", () => {
    // pg_size_pretty(pg_database_size(...)) reported "302 GB" for this many bytes.
    expect(formatBytes(324_270_000_000)).toBe("302 GB");
    expect(formatBytes(1536)).toBe("1.5 KB");
    expect(formatBytes(512)).toBe("512 B");
    expect(formatBytes(-1)).toBe("—");
    expect(formatBytes(null)).toBe("—");
  });

  it("counts crawled platforms, falling back to platforms seen in the data", () => {
    expect(crawledPlatformCount(stats())).toBe(3);
    expect(
      crawledPlatformCount(
        stats({
          crawledPlatforms: [],
          platforms: [
            { platform: "NA1", label: "North America", matchesStored: 2, matchesLast24Hours: 1 },
            { platform: "KR", label: "Korea", matchesStored: 1, matchesLast24Hours: 0 }
          ]
        })
      )
    ).toBe(2);
  });
});
