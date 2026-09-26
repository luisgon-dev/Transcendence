import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { BuildLab } from "./BuildLab";
import type { BuildLabOption, BuildLabResponse, BuildLabStage, BuildLabState } from "@/lib/buildLab";

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
  { championId: 238, slug: "Zed", name: "Zed" }
];

const items: Record<string, { name: string }> = {
  "1056": { name: "Doran's Ring" },
  "3020": { name: "Sorcerer's Shoes" },
  "6655": { name: "Luden's Companion" },
  "3118": { name: "Malignance" }
};
const runes = {
  "8112": { name: "Electrocute", icon: "perk-images/Styles/Domination/Electrocute.png" },
  "8139": { name: "Taste of Blood", icon: "perk-images/Styles/Domination/TasteOfBlood.png" },
  "8143": { name: "Sudden Impact", icon: "perk-images/Styles/Domination/SuddenImpact.png" },
  "8226": { name: "Manaflow Band", icon: "perk-images/Styles/Sorcery/ManaflowBand.png" },
  "8237": { name: "Scorch", icon: "perk-images/Styles/Sorcery/Scorch.png" }
};
const items2003 = { "2003": { name: "Health Potion" } };
const spells = { "4": { id: "SummonerFlash", name: "Flash" }, "14": { id: "SummonerDot", name: "Ignite" } };

function option(overrides: Partial<BuildLabOption>): BuildLabOption {
  return {
    actionKey: "6655",
    actionIds: [6655],
    games: 4200,
    pickRate: 0.61,
    winRate: 0.532,
    adjustedWinRate: 0.518,
    lift: 0.012,
    confidenceLow: 0.503,
    confidenceHigh: 0.533,
    averageTimingMinutes: 11.8,
    isLowSample: false,
    ...overrides
  };
}

function stage(overrides: Partial<BuildLabStage>): BuildLabStage {
  return {
    family: "ITEM",
    stage: 1,
    label: "First item",
    games: 6900,
    winRate: 0.506,
    scope: "ALL",
    isFallback: false,
    options: [
      option({}),
      option({
        actionKey: "3118",
        actionIds: [3118],
        games: 60,
        pickRate: 0.01,
        winRate: 0.6,
        adjustedWinRate: 0.55,
        lift: 0.044,
        confidenceLow: 0.43,
        confidenceHigh: 0.67,
        isLowSample: true
      })
    ],
    ...overrides
  };
}

const baseResponse: BuildLabResponse = {
  available: true,
  context: {
    championId: 103,
    role: "MIDDLE",
    opponentChampionId: null,
    requestedPatch: null,
    requestedRegion: "ALL",
    section: "ITEMS",
    mode: "SUPPORTED"
  },
  coverage: {
    includedPatches: ["16.19", "16.18"],
    patchWeights: [1, 0.6],
    countedMatches: 128_000,
    lastCountedAtUtc: "2026-09-25T10:00:00Z",
    includedRegions: ["KR", "NA1"],
    rankScope: "ALL_TRACKED"
  },
  selectedPath: [],
  stages: [
    stage({
      family: "STARTER",
      stage: 0,
      label: "Starting items",
      options: [option({ actionKey: "1056", actionIds: [1056], averageTimingMinutes: null })]
    }),
    stage({}),
    stage({
      family: "BOOTS",
      stage: 1,
      label: "Boots",
      options: [option({ actionKey: "3020", actionIds: [3020] })]
    })
  ],
  unavailableReason: null
};

function renderLab({
  state = {},
  response = {}
}: { state?: Partial<BuildLabState>; response?: Partial<BuildLabResponse> } = {}) {
  const initialState: BuildLabState = {
    role: "MIDDLE",
    section: "items",
    mode: "supported",
    itemPath: [],
    starter: [],
    runePage: [],
    spellPair: [],
    ...state
  };
  const initialResponse = { ...baseResponse, ...response };
  vi.mocked(fetch).mockImplementation(
    async () =>
      new Response(JSON.stringify(initialResponse), {
        status: 200,
        headers: { "content-type": "application/json" }
      })
  );
  return render(
    <BuildLab
      championId={103}
      championSlug="Ahri"
      championName="Ahri"
      champions={champions}
      version="16.19.1"
      itemVersion="16.19.1"
      items={items}
      runes={runes}
      spellVersion="16.19.1"
      spells={spells}
      initialState={initialState}
      initialResponse={initialResponse}
    />
  );
}

function row(name: string) {
  const cell = screen.getAllByText(name).find((element) => element.closest("tr"));
  const tableRow = cell?.closest("tr");
  if (!tableRow) throw new Error(`No table row for ${name}.`);
  return tableRow;
}

function requestedUrls() {
  return vi.mocked(fetch).mock.calls.map(([url]) => String(url));
}

describe("BuildLab", () => {
  beforeEach(() => {
    router.push.mockReset();
    router.replace.mockReset();
    vi.stubGlobal("fetch", vi.fn());
  });

  it("shows the adjusted win rate, lift, interval, raw rate, pick rate and games per choice", async () => {
    renderLab();

    const cells = within(row("Luden's Companion")).getAllByRole("cell").map((cell) => cell.textContent);
    expect(cells).toContain("+1.2 pp");
    expect(cells).toContain("50.3% – 53.3%");
    expect(cells).toContain("53.2%");
    expect(cells).toContain("61.0%");
    expect(cells).toContain("4,200");
    expect(cells).toContain("11.8m");
    expect(screen.getByText(/Patches 16\.19, 16\.18/)).toBeTruthy();
    await waitFor(() => expect(fetch).toHaveBeenCalled());
  });

  it("flags a low-sample choice and does not colour its lift as a win", async () => {
    renderLab();

    const rare = row("Malignance");
    expect(within(rare).getByText("Few games")).toBeTruthy();
    expect(within(rare).getByText("+4.4 pp").className).not.toContain("text-success");
    expect(within(row("Luden's Companion")).getByText("+1.2 pp").className).toContain("text-success");
    await waitFor(() => expect(fetch).toHaveBeenCalled());
  });

  it("locks a legendary into the path and reads the next item under it", async () => {
    const user = userEvent.setup();
    renderLab();
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));

    await user.click(within(row("Luden's Companion")).getByRole("button", { name: "Lock" }));

    expect(router.replace).toHaveBeenLastCalledWith(
      expect.stringContaining("itemPath=6655"),
      { scroll: false }
    );
    await waitFor(() => expect(requestedUrls().at(-1)).toContain("itemPath=6655"));
  });

  it("records a starter pick in the build without refetching", async () => {
    const user = userEvent.setup();
    renderLab();
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));

    await user.click(within(row("Doran's Ring")).getByRole("button", { name: "Pick" }));

    expect(router.replace).toHaveBeenLastCalledWith(expect.stringContaining("starter=1056"), {
      scroll: false
    });
    expect(screen.getByText("Your build")).toBeTruthy();
    // A terminal pick conditions nothing, so the read the page already has stays valid.
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("undo removes only the last locked item", async () => {
    const user = userEvent.setup();
    renderLab({ state: { itemPath: [6655, 3118] } });

    await user.click(screen.getByRole("button", { name: "Undo" }));

    const url = router.replace.mock.calls.at(-1)?.[0] as string;
    expect(url).toContain("itemPath=6655");
    expect(url).not.toContain("3118");
  });

  it("says when a thin matchup fell back to all opponents", async () => {
    renderLab({
      state: { opponentChampionId: 238 },
      response: {
        context: { ...baseResponse.context, opponentChampionId: 238 },
        stages: [stage({ isFallback: true })]
      }
    });

    // Rendered once in the desktop list and once in the mobile single-stage view.
    expect(
      screen.getAllByText("Matchup too thin at this step — showing all opponents").length
    ).toBeGreaterThan(0);
    await waitFor(() => expect(fetch).toHaveBeenCalled());
  });

  it("explains an empty answer and offers the broader context", async () => {
    const user = userEvent.setup();
    renderLab({
      state: { opponentChampionId: 238, itemPath: [6655] },
      response: {
        available: false,
        stages: [],
        unavailableReason: "No counted games followed this exact path."
      }
    });

    expect(screen.getByText("No counted games followed this exact path.")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Show all games for this role" }));

    const url = router.replace.mock.calls.at(-1)?.[0] as string;
    expect(url).not.toContain("opponentChampionId");
    expect(url).not.toContain("itemPath");
  });
  it("counts a repeated starter item instead of listing it twice", async () => {
    Object.assign(items, items2003);
    renderLab({
      response: {
        stages: [
          stage({
            family: "STARTER",
            stage: 0,
            label: "Starting items",
            options: [option({ actionKey: "1056+2003+2003", actionIds: [1056, 2003, 2003] })]
          })
        ]
      }
    });

    expect(screen.getAllByText("Doran's Ring + 2× Health Potion").length).toBeGreaterThan(0);
    await waitFor(() => expect(fetch).toHaveBeenCalled());
  });

  it("tells same-keystone rune pages apart by what each swaps in", async () => {
    renderLab({
      state: { section: "runes" },
      response: {
        stages: [
          stage({
            family: "RUNE_PAGE",
            stage: 0,
            label: "Complete rune page",
            options: [
              option({ actionKey: "a", actionIds: [8112, 8139, 8226], games: 5000 }),
              option({ actionKey: "b", actionIds: [8112, 8139, 8237], games: 300 }),
              option({ actionKey: "c", actionIds: [8112, 8226, 8139], games: 200 })
            ]
          })
        ]
      }
    });

    expect(screen.getAllByText("Taste of Blood · Manaflow Band").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Swaps in Scorch").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Same runes, different order").length).toBeGreaterThan(0);
    await waitFor(() => expect(fetch).toHaveBeenCalled());
  });
});
