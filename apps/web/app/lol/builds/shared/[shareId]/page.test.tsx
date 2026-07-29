import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { PublicSavedBuild } from "@/lib/buildLab";

import SharedBuildPage from "./page";

const fetchBackendJson = vi.fn<(url: string, init?: RequestInit) => Promise<unknown>>();
const requestHeaders = vi.fn<() => Headers>();

vi.mock("@/lib/backendCall", () => ({
  fetchBackendJson: (url: string, init?: RequestInit) => fetchBackendJson(url, init)
}));
vi.mock("next/headers", () => ({
  headers: () => Promise.resolve(requestHeaders())
}));
vi.mock("@/lib/analyticsFeatureFlags", () => ({
  analyticsFeatureFlags: () =>
    Promise.resolve({
      buildLab: true,
      championRecommendations: true,
      buildReferenceLinks: true
    })
}));
vi.mock("@/lib/staticData", () => ({
  fetchItemMap: () =>
    Promise.resolve({ version: "16.14.1", items: { "6672": { name: "Kraken Slayer" } } }),
  fetchRunesReforged: () => Promise.resolve({ runeById: {} }),
  fetchSummonerSpellMap: () => Promise.resolve({ version: "16.14.1", spells: {} }),
  itemIconUrl: (version: string, itemId: number) => `/items/${version}/${itemId}.png`,
  runeIconUrl: (icon: string) => `/runes/${icon}`,
  summonerSpellIconUrl: (version: string, id: string) => `/spells/${version}/${id}.png`
}));

function build(overrides: Partial<PublicSavedBuild> = {}): PublicSavedBuild {
  return {
    name: "Kraken first",
    championId: 103,
    role: "MIDDLE",
    opponentChampionId: null,
    patch: "26.13",
    region: "NA1",
    rankingMode: "SUPPORTED",
    itemPath: [6672],
    runeSelections: [],
    spell1Id: null,
    spell2Id: null,
    sourceGenerationId: "gen-1",
    currentGenerationId: "gen-1",
    analyticsChanged: false,
    compatibilityStatus: "ITEMS_RETIRED",
    unavailableItemIds: [6672],
    unavailableItems: [{ itemId: 6672, reason: "RETIRED" }],
    updatedAtUtc: "2026-07-01T00:00:00Z",
    ...overrides
  };
}

async function renderPage(body: PublicSavedBuild) {
  fetchBackendJson.mockResolvedValue({
    requestId: "test",
    url: "/api/lol/saved-builds/share-1",
    durationMs: 1,
    status: 200,
    ok: true,
    body
  });
  render(await SharedBuildPage({ params: Promise.resolve({ shareId: "share-1" }) }));
}

describe("SharedBuildPage", () => {
  beforeEach(() => {
    fetchBackendJson.mockReset();
    requestHeaders.mockReturnValue(new Headers({ "x-real-ip": "203.0.113.7" }));
  });

  it("names the patch the availability check ran against, not the patch the build was saved on", async () => {
    await renderPage(build());

    expect(
      screen.getByText("This setup contains items that cannot be built on the current active patch:")
    ).toBeTruthy();
    // The saved patch is the one patch these items are known to have existed on, so the notice must
    // never claim they are missing from it.
    expect(screen.queryByText(/no longer exist on patch 26\.13/)).toBeNull();
    expect(screen.getByText(/Kraken Slayer/)).toBeTruthy();
  });

  it("still discloses the saved patch in the header, where it is the accurate statement", async () => {
    await renderPage(build());

    expect(screen.getByText(/saved on patch 26\.13/)).toBeTruthy();
  });

  it("forwards the client identity so the anonymous share read is rate limited per client", async () => {
    await renderPage(build());

    const init = fetchBackendJson.mock.calls[0]?.[1];
    expect(init?.headers).toEqual({ "x-forwarded-for": "203.0.113.7" });
    expect(init?.cache).toBe("no-store");
  });

  it("forwards no address when the edge vouched for none, rather than inventing one", async () => {
    requestHeaders.mockReturnValue(new Headers());
    await renderPage(build());

    const init = fetchBackendJson.mock.calls[0]?.[1];
    expect(init?.headers).toBeUndefined();
  });
});
