import { render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { DatasetFigures } from "@/components/DatasetFigures";
import type { DatasetStats } from "@/lib/datasetStats";

const NOW = new Date("2026-10-06T07:00:00Z");

function stats(overrides: Partial<DatasetStats> = {}): DatasetStats {
  return {
    matchesStored: 567_944,
    matchesLast24Hours: 21_325,
    matchesPerDayLast7Days: 23_482,
    activePatch: "16.19",
    activePatchMatches: 234_302,
    playersIndexedEstimate: 4_803_099,
    databaseSizeBytes: 324_270_000_000,
    crawledPlatforms: ["NA1", "EUW1", "KR", "EUN1", "BR1", "JP1", "TR1", "LA1", "LA2", "OC1"],
    platforms: [],
    lastMatchIngestedAtUtc: "2026-10-06T06:48:00Z",
    computedAtUtc: "2026-10-06T06:50:00Z",
    ...overrides
  };
}

// Each figure is a <dt>/<dd> pair; read the value that belongs to a label.
function valueFor(label: string) {
  const term = screen.getByText(label, { selector: "dt" });
  return term.nextElementSibling as HTMLElement;
}

describe("DatasetFigures", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("renders the corpus figures as a labelled description list", () => {
    render(<DatasetFigures stats={stats()} />);

    expect(document.querySelectorAll("dl dt")).toHaveLength(6);
    expect(valueFor("Matches stored").textContent).toBe("567,944");
    expect(valueFor("Last 24 hours").textContent).toBe("21,325");
    expect(valueFor("7-day daily avg").textContent).toBe("23,482");
    expect(valueFor("Platforms").textContent).toBe("10");
  });

  it("marks the players figure as an estimate, visually and for screen readers", () => {
    render(<DatasetFigures stats={stats()} />);

    const players = valueFor("Players indexed");
    expect(players.textContent).toBe("≈about 4.8M");
    expect(within(players).getByText("≈").getAttribute("aria-hidden")).toBe("true");
    expect(within(players).getByText("about").className).toContain("sr-only");
  });

  it("shows how long ago the last match was ingested", () => {
    render(<DatasetFigures stats={stats()} />);

    expect(valueFor("Last ingest").textContent).toBe("12 min ago");
  });

  it("leaves out figures the backend could not determine instead of showing zero", () => {
    render(
      <DatasetFigures
        stats={stats({ playersIndexedEstimate: null, lastMatchIngestedAtUtc: null })}
      />
    );

    expect(screen.queryByText("Players indexed")).toBeNull();
    expect(screen.queryByText("Last ingest")).toBeNull();
    expect(document.querySelectorAll("dt")).toHaveLength(4);
  });
});
