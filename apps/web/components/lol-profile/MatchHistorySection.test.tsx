import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { MatchHistorySection } from "./MatchHistorySection";

function props(overrides: Partial<React.ComponentProps<typeof MatchHistorySection>> = {}) {
  return {
    region: "na",
    gameName: "Kronic",
    tagLine: "NA1",
    summonerId: "summoner-1",
    page: 1,
    queue: "ALL",
    championFilter: "",
    sort: "DATE_DESC" as const,
    history: null,
    historyBusy: false,
    historyError: null,
    visibleMatches: [],
    queueOptions: [
      { value: "ALL", label: "All" },
      { value: "RANKED_SOLO_DUO", label: "Solo/Duo" }
    ],
    championOptions: [],
    sortOptions: [{ value: "DATE_DESC" as const, label: "Most recent" }],
    expandedMatchId: null,
    details: {},
    detailBusy: {},
    championStatic: null,
    itemStatic: null,
    spellStatic: null,
    runeStatic: null,
    prefersReducedMotion: true,
    onQueueChange: vi.fn(),
    onChampionFilterChange: vi.fn(),
    onSortChange: vi.fn(),
    onToggleExpanded: vi.fn(),
    onPreviousPage: vi.fn(),
    onNextPage: vi.fn(),
    ...overrides
  };
}

describe("MatchHistorySection", () => {
  it("distinguishes loading from an empty filtered result", () => {
    const { rerender } = render(<MatchHistorySection {...props({ historyBusy: true })} />);
    expect(screen.queryByText("No matches found for the current queue/champion filters.")).toBeNull();

    rerender(<MatchHistorySection {...props()} />);
    expect(screen.getByText("No matches found for the current queue/champion filters.")).toBeTruthy();
  });

  it("renders errors and forwards pagination/filter actions", async () => {
    const user = userEvent.setup();
    const onQueueChange = vi.fn();
    const onNextPage = vi.fn();
    render(
      <MatchHistorySection
        {...props({ historyError: "Match history is temporarily unavailable.", onQueueChange, onNextPage })}
      />
    );

    expect(screen.getByText("Match history is temporarily unavailable.")).toBeTruthy();
    await user.click(screen.getByRole("radio", { name: "Solo/Duo" }));
    expect(onQueueChange).toHaveBeenCalledWith("RANKED_SOLO_DUO");
    await user.click(screen.getByRole("button", { name: "Next" }));
    expect(onNextPage).toHaveBeenCalledOnce();
  });
});
