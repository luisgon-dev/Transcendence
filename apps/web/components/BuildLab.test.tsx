import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { BuildLab } from "./BuildLab";
import { buildLabPermalink, type BuildLabOption, type BuildLabResponse, type BuildLabStage, type BuildLabState } from "@/lib/buildLab";

const router = vi.hoisted(() => ({
  push: vi.fn(),
  prefetch: vi.fn()
}));

vi.mock("next/navigation", async () => {
  const { useSyncExternalStore } = await import("react");
  function subscribe(update: () => void) {
    window.addEventListener("popstate", update);
    return () => window.removeEventListener("popstate", update);
  }
  return {
    useRouter: () => router,
    useSearchParams: () => new URLSearchParams(useSyncExternalStore(subscribe, () => window.location.search))
  };
});

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
    skillPriority: [],
    skillStart: [],
    ...state
  };
  const initialResponse = { ...baseResponse, ...response };
  window.history.replaceState(null, "", buildLabPermalink(103, initialState));
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
      initialResponse={initialResponse}
    />
  );
}

function row(name: string) {
  const cell = screen.getAllByText(name).find((element) => element.closest('[role="row"]'));
  const tableRow = cell?.closest<HTMLElement>('[role="row"]');
  if (!tableRow) throw new Error(`No table row for ${name}.`);
  return tableRow;
}

function requestedUrls() {
  return vi.mocked(fetch).mock.calls.map(([url]) => String(url));
}

describe("BuildLab", () => {
  beforeEach(() => {
    router.push.mockReset();
    const replaceState = window.history.replaceState.bind(window.history);
    vi.spyOn(window.history, "replaceState").mockImplementation((...args) => {
      replaceState(...args);
      window.dispatchEvent(new PopStateEvent("popstate"));
    });
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("shows the server-selected role on automatic loads and honors the URL role", async () => {
    renderLab({ state: { role: undefined }, response: { context: { ...baseResponse.context, role: "JUNGLE" } } });

    expect(screen.getByRole("combobox", { name: "Role" }).textContent).toContain("Jungle");
    await waitFor(() => expect(fetch).toHaveBeenCalled());
    expect(new URL(requestedUrls()[0], location.origin).searchParams.has("role")).toBe(false);

    window.history.replaceState(null, "", "/lol/builds/103?role=UTILITY");
    await waitFor(() => expect(screen.getByRole("combobox", { name: "Role" }).textContent).toContain("Support"));
    expect(requestedUrls().at(-1)).toContain("role=UTILITY");
  });

  it("shows the adjusted win rate, lift, interval, raw rate, pick rate and games per choice", async () => {
    renderLab();

    // Each cell also carries the label a phone shows above its value, hence "includes".
    const cells = within(row("Luden's Companion")).getAllByRole("cell").map((cell) => cell.textContent ?? "");
    for (const value of ["+1.2 pp", "50.3%–53.3%", "53.2%", "61.0%", "4,200", "11.8m"]) {
      expect(cells.some((cell) => cell.includes(value)), value).toBe(true);
    }
    expect(screen.getByText(/Patches 16\.19, 16\.18/)).toBeTruthy();
    await waitFor(() => expect(fetch).toHaveBeenCalled());
  });

  it("flags a low-sample choice and does not colour its lift as a win", async () => {
    const user = userEvent.setup();
    renderLab();
    await user.click(screen.getByRole("button", { name: "Show 1 more · 1 with few games" }));

    const rare = row("Malignance");
    expect(within(rare).getByText("Few games")).toBeTruthy();
    const liftCell = (tableRow: HTMLElement, lift: string) =>
      within(tableRow).getAllByRole("cell").find((cell) => cell.textContent?.includes(lift))!;
    expect(liftCell(rare, "+4.4 pp").className).not.toContain("text-success");
    expect(liftCell(row("Luden's Companion"), "+1.2 pp").className).toContain("text-success");
    await waitFor(() => expect(fetch).toHaveBeenCalled());
  });

  it("locks a legendary into the path and reads the next item under it", async () => {
    const user = userEvent.setup();
    renderLab();
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));

    await user.click(within(row("Luden's Companion")).getByRole("button", { name: "Lock" }));

    expect(window.location.search).toContain("itemPath=6655");
    await waitFor(() => expect(requestedUrls().at(-1)).toContain("itemPath=6655"));
  });

  it("records a starter pick in the build without refetching", async () => {
    const user = userEvent.setup();
    renderLab();
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));

    await user.click(within(row("Doran's Ring")).getByRole("button", { name: "Pick" }));

    expect(window.location.search).toContain("starter=1056");
    expect(screen.getByText("Your build")).toBeTruthy();
    // A terminal pick conditions nothing, so the read the page already has stays valid.
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("undo removes only the last locked item", async () => {
    const user = userEvent.setup();
    renderLab({ state: { itemPath: [6655, 3118] } });

    await user.click(screen.getByRole("button", { name: "Undo" }));

    const url = window.location.search;
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
      screen.getAllByText("Too few games for this matchup. Showing all opponents.").length
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

    const url = window.location.search;
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
  it("shows the top eight choices of a long stage and expands to the rest on request", async () => {
    const user = userEvent.setup();
    const many = Array.from({ length: 11 }, (_, index) =>
      option({ actionKey: `x${index}`, actionIds: [6655], games: 1000 - index })
    );
    renderLab({ response: { stages: [stage({ options: many })] } });

    expect(screen.getAllByRole("row").length).toBe(1 + 8);
    await user.click(screen.getByRole("button", { name: "Show 3 more" }));
    expect(screen.getAllByRole("row").length).toBe(1 + 11);
    expect(screen.getByRole("button", { name: "Show fewer" })).toBeTruthy();
  });
  it("hides few-games choices behind the expander while trusted ones exist", async () => {
    renderLab();

    expect(screen.queryByText("Malignance")).toBeNull();
    expect(screen.getAllByText("Luden's Companion").length).toBeGreaterThan(0);
    await waitFor(() => expect(fetch).toHaveBeenCalled());
  });

  it("shows few-games choices when they are all a stage has", async () => {
    renderLab({
      response: {
        stages: [
          stage({
            options: [
              option({ actionKey: "6655", actionIds: [6655], games: 40, isLowSample: true }),
              option({ actionKey: "3118", actionIds: [3118], games: 20, isLowSample: true })
            ]
          })
        ]
      }
    });

    expect(screen.getAllByText("Luden's Companion").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Malignance").length).toBeGreaterThan(0);
    expect(screen.queryByRole("button", { name: /Show \d+ more/ })).toBeNull();
    await waitFor(() => expect(fetch).toHaveBeenCalled());
  });
  it("leads with the recommended build and follows it into the lab", async () => {
    const user = userEvent.setup();
    renderLab({
      response: {
        summary: {
          starter: option({ actionKey: "1056", actionIds: [1056] }),
          items: [option({}), option({ actionKey: "3118", actionIds: [3118] })],
          boots: option({ actionKey: "3020", actionIds: [3020] }),
          runePage: null,
          spellPair: option({ actionKey: "4+14", actionIds: [4, 14] }),
          skillPriority: option({ actionKey: "1+3+2", actionIds: [1, 3, 2] })
        }
      }
    });

    const strip = screen.getByRole("region", { name: "Recommended build" });
    expect(within(strip).getByText("Q › E › W")).toBeTruthy();
    expect(within(strip).getByText("Flash + Ignite")).toBeTruthy();
    await user.click(within(strip).getByRole("button", { name: "Follow this build" }));

    const url = window.location.search;
    expect(url).toContain("itemPath=6655&itemPath=3118");
    expect(url).toContain("skillPriority=1&skillPriority=3&skillPriority=2");
  });

  it("shows a skill order as key caps in priority order", async () => {
    renderLab({
      state: { section: "skills" },
      response: {
        stages: [
          stage({
            family: "SKILLS",
            stage: 0,
            label: "Skill priority",
            options: [option({ actionKey: "1+3+2", actionIds: [1, 3, 2], averageTimingMinutes: null })]
          })
        ]
      }
    });

    expect(screen.getAllByText("Q › E › W").length).toBeGreaterThan(0);
    await waitFor(() => expect(fetch).toHaveBeenCalled());
  });
});
