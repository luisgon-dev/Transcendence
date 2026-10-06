import { render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { DatasetStats } from "@/lib/datasetStats";

const fetchDatasetStats = vi.fn<() => Promise<DatasetStats | null>>();

vi.mock("@/lib/datasetStatsServer", () => ({
  fetchDatasetStats: () => fetchDatasetStats()
}));

const { default: AboutPage } = await import("./page");

function stats(overrides: Partial<DatasetStats> = {}): DatasetStats {
  return {
    matchesStored: 1_000,
    matchesLast24Hours: 40,
    matchesPerDayLast7Days: 35,
    activePatch: "16.19",
    activePatchMatches: 412,
    playersIndexedEstimate: 9_800,
    databaseSizeBytes: 324_270_000_000,
    crawledPlatforms: ["NA1", "KR"],
    platforms: [
      { platform: "NA1", label: "North America", matchesStored: 750, matchesLast24Hours: 30 },
      { platform: "KR", label: "Korea", matchesStored: 250, matchesLast24Hours: 10 }
    ],
    lastMatchIngestedAtUtc: "2026-10-06T06:48:00Z",
    computedAtUtc: "2026-10-06T06:50:00Z",
    ...overrides
  };
}

describe("About the data page", () => {
  beforeEach(() => {
    fetchDatasetStats.mockReset();
  });

  it("shows the coverage figures and a per-platform table from the snapshot", async () => {
    fetchDatasetStats.mockResolvedValue(stats());

    render(await AboutPage());

    expect(screen.getByRole("heading", { level: 1, name: "The data behind Transcendence" })).toBeTruthy();
    expect(screen.getByText("Patch 16.19")).toBeTruthy();
    expect(screen.getByText("412").parentElement?.textContent).toContain(
      "412 matches on the current patch, 16.19."
    );
    expect(screen.getByText("302 GB")).toBeTruthy();

    const table = screen.getByRole("table");
    const rows = within(table).getAllByRole("row").slice(1);
    expect(rows.map((row) => within(row).getAllByRole("cell").map((cell) => cell.textContent))).toEqual([
      ["North America NA1", "750", "75%", "30"],
      ["Korea KR", "250", "25%", "10"]
    ]);

    // The pipeline copy names the crawled platform count from the same snapshot.
    expect(screen.getByText(/ladders on 2 platforms/)).toBeTruthy();
  });

  it("explains the gap instead of rendering zeros before the first snapshot exists", async () => {
    fetchDatasetStats.mockResolvedValue(null);

    render(await AboutPage());

    expect(screen.getByText("Dataset figures are not available yet")).toBeTruthy();
    expect(screen.queryByRole("table")).toBeNull();
    expect(screen.queryByText("Matches stored")).toBeNull();
    expect(screen.getByText(/ladders on every crawled platform/)).toBeTruthy();
  });
});
