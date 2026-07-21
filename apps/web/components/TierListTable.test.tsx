import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import type { UITierListEntry } from "@/lib/tierlist";

import { TierListTable } from "./TierListTable";

const entries: UITierListEntry[] = [
  {
    championId: 103,
    role: "MIDDLE",
    tier: "S",
    winRate: 0.54,
    pickRate: 0.1,
    banRate: 0.08,
    games: 1_200,
    movement: "UP",
    previousTier: "A",
    strengthScore: 0.04,
    contestedScore: 0.18,
    isLowSample: false
  },
  {
    championId: 86,
    role: "TOP",
    tier: "B",
    winRate: 0.49,
    pickRate: 0.05,
    banRate: 0.02,
    games: 800,
    movement: "SAME",
    previousTier: "B",
    strengthScore: -0.01,
    contestedScore: 0.07,
    isLowSample: false
  },
  {
    championId: 17,
    role: "TOP",
    tier: "B",
    winRate: 0.52,
    pickRate: 0.01,
    banRate: 0.01,
    games: 90,
    movement: "NEW",
    previousTier: null,
    strengthScore: 0.02,
    contestedScore: 0.02,
    isLowSample: true
  }
];

const champions = {
  "103": { id: "Ahri", name: "Ahri", title: "the Nine-Tailed Fox" },
  "86": { id: "Garen", name: "Garen", title: "The Might of Demacia" },
  "17": { id: "Teemo", name: "Teemo", title: "the Swift Scout" }
};

describe("TierListTable", () => {
  it("filters by champion and renders a useful empty state", async () => {
    const user = userEvent.setup();
    render(
      <TierListTable
        entries={entries}
        champions={champions}
        version="16.14.1"
        rankTierValue="EMERALD_PLUS"
        activeRegion="ALL"
        minGames={500}
      />
    );

    expect(screen.getByText("Ahri")).toBeTruthy();
    expect(screen.getByText("Garen")).toBeTruthy();
    expect(screen.queryByText("Teemo")).toBeNull();

    const input = screen.getByRole("searchbox", { name: "Find a champion" });
    await user.type(input, "Nocturne");
    expect(await screen.findByText("No champion matches “Nocturne”.")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Clear search" }));
    await waitFor(() => expect(screen.getByText("Ahri")).toBeTruthy());
  });

  it("reveals low-sample rows and exposes the active sort direction", async () => {
    const user = userEvent.setup();
    render(
      <TierListTable
        entries={entries}
        champions={champions}
        version="16.14.1"
        rankTierValue="EMERALD_PLUS"
        activeRegion="ALL"
        minGames={500}
      />
    );

    await user.click(screen.getByRole("button", { name: "Show low-sample (1)" }));
    expect(await screen.findByText("Teemo")).toBeTruthy();

    const winRateSort = screen.getAllByRole("button", { name: /^Sort by Win Rate/ })[0];
    await user.click(winRateSort);
    const activeSort = await screen.findByRole("button", {
      name: "Sort by Win Rate, currently descending"
    });
    const table = activeSort.closest("table");
    expect(table).not.toBeNull();
    expect(within(table!).getByRole("columnheader", { name: /Win Rate/ }).getAttribute("aria-sort"))
      .toBe("descending");
  });
});
