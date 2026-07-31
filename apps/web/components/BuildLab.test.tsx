import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { BuildLab } from "./BuildLab";
import type {
  AdjustedActionEstimate,
  BuildLabResponse,
  BuildLabState
} from "@/lib/buildLab";

const router = vi.hoisted(() => ({
  push: vi.fn(),
  replace: vi.fn(),
  prefetch: vi.fn()
}));

vi.mock("next/navigation", () => ({
  useRouter: () => router
}));

const champions = [
  { championId: 103, slug: "Ahri", name: "Ahri" },
  { championId: 64, slug: "LeeSin", name: "Lee Sin" }
];

const items = {
  "1055": { name: "Doran's Blade" },
  "2003": { name: "Health Potion" },
  "3006": { name: "Berserker's Greaves" },
  "3153": { name: "Blade of the Ruined King" },
  "6672": { name: "Kraken Slayer" }
};

const runes = { "8005": { name: "Press the Attack", icon: "perk-images/Styles/precision.png" } };
const spells = { "4": { id: "SummonerFlash", name: "Flash" } };

const gatedCandidate: AdjustedActionEstimate = {
  actionKey: "ITEM:3153",
  actionIds: [3153],
  adjustedWpa: null,
  confidenceLow: null,
  confidenceHigh: null,
  rawWinRate: null,
  pickRate: null,
  observedCount: 42,
  effectiveSampleSize: 31,
  averageTimingMinutes: null,
  evidenceQuality: "INSUFFICIENT_SAMPLE",
  fallbackScope: "NONE",
  regionScope: "NA1",
  baselineDefinition: "Other first legendary items bought in the same decision.",
  evidenceTier: "DESCRIPTIVE",
  evidenceBucket: null,
  isPublishable: false,
  unavailableReason: "Withheld: 42 observed games is below the publication gate for this cell."
};

const publishableCandidate: AdjustedActionEstimate = {
  actionKey: "ITEM:6672",
  actionIds: [6672],
  adjustedWpa: 0.0184,
  confidenceLow: 0.009,
  confidenceHigh: 0.0278,
  rawWinRate: 0.531,
  pickRate: 0.376,
  observedCount: 1240,
  effectiveSampleSize: 980,
  averageTimingMinutes: 14.2,
  evidenceQuality: "STRONG",
  fallbackScope: "GLOBAL_FALLBACK",
  regionScope: "GLOBAL",
  baselineDefinition: "Other first legendary items bought in the same decision.",
  evidenceTier: "NUMERIC",
  evidenceBucket: null,
  isPublishable: true,
  unavailableReason: null
};

const bootsCandidate: AdjustedActionEstimate = {
  ...publishableCandidate,
  actionKey: "BOOTS:3006",
  actionIds: [3006],
  adjustedWpa: -0.0072,
  confidenceLow: -0.014,
  confidenceHigh: -0.0005,
  rawWinRate: 0.492,
  pickRate: 0.211,
  averageTimingMinutes: 11.4,
  fallbackScope: "NONE",
  regionScope: "NA1"
};

const response: BuildLabResponse = {
  available: true,
  context: {
    championId: 103,
    role: "MIDDLE",
    opponentChampionId: null,
    requestedPatch: "26.14",
    effectivePatch: "26.14",
    requestedRegion: "NA1",
    effectiveRegion: "NA1",
    section: "items",
    mode: "supported"
  },
  provenance: {
    generationId: "gen-1",
    datasetVersion: "ds-26.14",
    modelVersion: "wpa-3",
    staticDataVersion: "16.14.1",
    sourceCutoffUtc: null,
    generatedAtUtc: null,
    matchCount: 412_000,
    rankScope: "EMERALD_PLUS",
    includedPatches: ["26.14"],
    includedRegions: ["NA1", "KR", "GLOBAL"]
  },
  selectedPath: [],
  pathEstimate: null,
  stages: [
    {
      family: "ITEM",
      stage: 1,
      label: "First legendary",
      candidates: [publishableCandidate, gatedCandidate]
    },
    { family: "BOOTS", stage: 2, label: "Boots", candidates: [bootsCandidate] }
  ],
  unavailableReason: null
};

const baseState: BuildLabState = {
  role: "MIDDLE",
  section: "items",
  mode: "supported",
  region: "NA1",
  itemPath: [],
  itemLocks: [],
  runeSelections: [],
  runePage: [],
  spellPair: []
};

function renderLab(
  overrides: { state?: Partial<BuildLabState>; response?: Partial<BuildLabResponse> } = {}
) {
  const labResponse = { ...response, ...overrides.response };
  return render(
    <BuildLab
      championId={103}
      championSlug="Ahri"
      championName="Ahri"
      champions={champions}
      version="16.14.1"
      itemVersion="16.14.1"
      items={items}
      runes={runes}
      spellVersion="16.14.1"
      spells={spells}
      initialState={{ ...baseState, ...overrides.state }}
      initialResponse={labResponse}
    />
  );
}

/** The first rendered stage table is the desktop one; the small-screen stage repeats it. */
function candidateRow(name: string) {
  const table = screen.getAllByRole("table")[0];
  const row = within(table)
    .getAllByRole("row")
    .find((candidate) => candidate.textContent?.includes(name));
  if (!row) throw new Error(`No candidate row for ${name}.`);
  return row;
}

function evidenceValue(row: HTMLElement, term: string) {
  return within(row).getByText(term).nextElementSibling?.textContent ?? "";
}

/** The locked-prefix strip, scoped away from the candidate tables that repeat the same names. */
function lockedPrefix() {
  const label = screen.getByText("Locked prefix").parentElement;
  if (!label) throw new Error("The locked prefix strip is not rendered.");
  return label;
}

describe("BuildLab", () => {
  beforeEach(() => {
    router.push.mockReset();
    router.replace.mockReset();
    vi.stubGlobal(
      "fetch",
      vi.fn(
        async () =>
          new Response(JSON.stringify(response), {
            status: 200,
            headers: { "content-type": "application/json" }
          })
      )
    );
  });

  it("withholds a gated cell's win rate and pick rate instead of printing a zero", async () => {
    renderLab();
    const row = candidateRow("Blade of the Ruined King");

    const cells = within(row).getAllByRole("cell");
    expect(cells[4].textContent).toBe("—");
    expect(evidenceValue(row, "Raw observed win rate:")).toBe("—");
    expect(row.textContent).not.toContain("0.0%");

    // The publishable neighbour still reports both, so the dash is a withheld value, not a bug.
    const publishable = candidateRow("Kraken Slayer");
    expect(within(publishable).getAllByRole("cell")[4].textContent).toBe("37.6%");
    expect(evidenceValue(publishable, "Raw observed win rate:")).toBe("53.1%");

    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalled());
  });

  it("withholds the whole-path headline when the conditioned path fails the gates", async () => {
    renderLab({
      state: { itemPath: [1055, 2003], itemLocks: [2] },
      response: {
        pathEstimate: {
          itemPath: [1055, 2003],
          estimatedWinProbability: null,
          adjustedLift: null,
          confidenceLow: null,
          confidenceHigh: null,
          observedCount: 18,
          effectiveSampleSize: 12,
          isPublishable: false,
          unavailableReason: "This exact path was observed 18 times."
        }
      }
    });

    const lift = screen.getByText("Complete path lift").nextElementSibling;
    expect(lift?.textContent).toBe("Insufficient evidence");
    expect(lift?.className).toContain("text-muted");
    expect(screen.getByText("Estimated win probability").nextElementSibling?.textContent).toBe("—");

    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalled());
  });

  it("publishes a direction for a bucketed candidate instead of withholding everything", async () => {
    // A fortnightly patch rarely earns a <=3pp interval in time. A cell whose posterior still
    // concentrates on one side of "typical" says so rather than going dark.
    renderLab({
      response: {
        stages: [
          {
            family: "ITEM",
            stage: 1,
            label: "First item",
            candidates: [
              {
                ...gatedCandidate,
                actionKey: "ITEM:3153",
                evidenceTier: "BUCKETED",
                evidenceBucket: "ABOVE_AVERAGE",
                unavailableReason: "The confidence interval is too wide."
              }
            ]
          }
        ]
      }
    });
    const row = candidateRow("Blade of the Ruined King");
    const cells = within(row).getAllByRole("cell");

    expect(cells[1].textContent).toBe("Above average");
    expect(cells[1].textContent).not.toContain("Insufficient evidence");
    // The number is still withheld: only the direction was earned.
    expect(cells[2].textContent).toBe("Direction only");
  });

  it("renders a below-average bucket in the loss tone, not the action accent", async () => {
    renderLab({
      response: {
        stages: [
          {
            family: "ITEM",
            stage: 1,
            label: "First item",
            candidates: [
              {
                ...gatedCandidate,
                evidenceTier: "BUCKETED",
                evidenceBucket: "BELOW_AVERAGE"
              }
            ]
          }
        ]
      }
    });
    const cells = within(candidateRow("Blade of the Ruined King")).getAllByRole("cell");

    expect(cells[1].textContent).toBe("Below average");
    expect(cells[1].className).toContain("text-danger");
    expect(cells[1].className).not.toContain("text-primary");
  });

  it("states why a gated candidate is unavailable and shows no headline estimate for it", async () => {
    renderLab();
    const row = candidateRow("Blade of the Ruined King");

    expect(within(row).getAllByRole("cell")[1].textContent).toBe("Insufficient evidence");
    expect(
      within(row).getByText(
        "Withheld: 42 observed games is below the publication gate for this cell."
      )
    ).toBeTruthy();
    expect(row.textContent).not.toContain("pp");

    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalled());
  });

  it("discloses the per-candidate global fallback while the section itself is regional", async () => {
    renderLab();

    const fallback = candidateRow("Kraken Slayer");
    expect(within(fallback).getByText("Global cell")).toBeTruthy();
    expect(evidenceValue(fallback, "Estimated in:")).toBe(
      "Global baseline (no publishable NA1 cell for this choice)"
    );

    // A candidate estimated in the requested region must not wear the disclosure.
    const regional = candidateRow("Blade of the Ruined King");
    expect(within(regional).queryByText("Global cell")).toBeNull();
    expect(evidenceValue(regional, "Estimated in:")).toBe("North America");

    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalled());
  });

  it("names the comparison set and the effective sample in the evidence drilldown", async () => {
    renderLab();
    const row = candidateRow("Kraken Slayer");

    expect(evidenceValue(row, "Compared against:")).toBe(
      "Other first legendary items bought in the same decision."
    );
    expect(evidenceValue(row, "Observed games / effective sample:")).toBe("1,240 / 980");
    expect(evidenceValue(row, "Evidence quality:")).toBe("strong");

    await waitFor(() => expect(vi.mocked(fetch)).toHaveBeenCalled());
  });

  it("undoes a whole composite lock, never a single id out of one", async () => {
    renderLab({ state: { itemPath: [1055, 2003, 3006], itemLocks: [2, 1] } });

    expect(within(lockedPrefix()).getByText("Berserker's Greaves")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Undo last selection" }));

    await waitFor(() => expect(router.replace).toHaveBeenCalled());
    const afterBoots = new URL(String(router.replace.mock.calls.at(-1)?.[0]), "https://trn.test");
    expect(afterBoots.searchParams.getAll("itemPath")).toEqual(["1055", "2003"]);
    expect(within(lockedPrefix()).getByText("Doran's Blade")).toBeTruthy();
    expect(within(lockedPrefix()).getByText("Health Potion")).toBeTruthy();
    expect(within(lockedPrefix()).queryByText("Berserker's Greaves")).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Undo last selection" }));

    await waitFor(() => expect(screen.queryByText("Locked prefix")).toBeNull());
    const afterStarter = new URL(String(router.replace.mock.calls.at(-1)?.[0]), "https://trn.test");
    // The starter set left together: a half-removed set would hash to a prefix nothing stored.
    expect(afterStarter.searchParams.getAll("itemPath")).toEqual([]);
    expect(screen.queryByText("Doran's Blade")).toBeNull();
    expect(screen.queryByText("Health Potion")).toBeNull();
  });

  it("refuses a selection that would overflow the conditioned path and keeps the prefix", async () => {
    renderLab({
      state: {
        itemPath: Array.from({ length: 12 }, (_, index) => 1000 + index),
        itemLocks: Array.from({ length: 12 }, () => 1)
      }
    });

    fireEvent.click(within(candidateRow("Kraken Slayer")).getByRole("button", { name: "Lock" }));

    expect(screen.getByRole("alert").textContent).toContain("nothing was discarded");
    expect(router.replace).not.toHaveBeenCalled();
  });

  it("switches small-screen stages through a keyboard-operable stage control", async () => {
    const user = userEvent.setup();
    renderLab();

    const stageControl = screen.getByRole("group", { name: "Decision stage" });
    const stageOptions = within(stageControl).getAllByRole("radio");
    expect(stageOptions.map((option) => option.textContent)).toEqual([
      "First legendary",
      "Boots"
    ]);
    expect(stageOptions[0].getAttribute("aria-checked")).toBe("true");

    // The small-screen stage repeats the active stage's heading, so the active stage's heading
    // is the one rendered twice.
    expect(screen.getAllByRole("heading", { name: "First legendary" })).toHaveLength(2);
    expect(screen.getAllByRole("heading", { name: "Boots" })).toHaveLength(1);

    stageOptions[0].focus();
    await user.keyboard("{ArrowRight}");
    await user.keyboard("{Enter}");

    expect(within(stageControl).getAllByRole("radio")[1].getAttribute("aria-checked")).toBe("true");
    expect(screen.getAllByRole("heading", { name: "Boots" })).toHaveLength(2);
    expect(screen.getAllByRole("heading", { name: "First legendary" })).toHaveLength(1);

    // Every breakpoint gets the same dense table, so the small screen never loses the timing
    // column the compressed cards used to drop.
    const tables = screen.getAllByRole("table");
    expect(tables).toHaveLength(3);
    for (const table of tables) {
      expect(within(table).getByRole("columnheader", { name: "Timing" })).toBeTruthy();
    }
  });
});
