import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { MatchupsTable, type MatchupRow } from "./MatchupsTable";

const rows: MatchupRow[] = [
  {
    opponentChampionId: 1,
    opponentSlug: "Annie",
    opponentName: "Annie",
    winRate: 55,
    games: 20,
    verdict: "Favored"
  },
  {
    opponentChampionId: 2,
    opponentSlug: "Olaf",
    opponentName: "Olaf",
    winRate: 45,
    games: 80,
    verdict: "Unfavored"
  }
];

describe("MatchupsTable", () => {
  it("announces the active direction and defaults to toughest first", async () => {
    const user = userEvent.setup();
    render(
      <MatchupsTable
        title="Matchups"
        subtitle="Lane opponents"
        rows={rows}
        version="16.14.1"
        linkQuery=""
      />
    );

    expect(screen.getByRole("columnheader", { name: "Win Rate" }).getAttribute("aria-sort")).toBe(
      "ascending"
    );
    const bodyRows = within(screen.getByRole("table")).getAllByRole("row").slice(1);
    expect(within(bodyRows[0]).getByText("Olaf")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Sort by games, most played first" }));

    expect(screen.getByRole("columnheader", { name: "Games" }).getAttribute("aria-sort")).toBe(
      "descending"
    );
    expect(
      screen.getByRole("button", { name: "Sort by games, most played first" }).getAttribute("aria-pressed")
    ).toBe("true");
  });
});
