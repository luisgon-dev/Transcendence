import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { MatchHistorySection } from "./MatchHistorySection";

type TestOverrides = {
  queue?: string;
  historyBusy?: boolean;
  historyError?: string | null;
  onQueueChange?: (value: string) => void;
  onNextPage?: () => void;
};

function props(overrides: TestOverrides = {}): React.ComponentProps<typeof MatchHistorySection> {
  return {
    identity: {
      region: "na",
      gameName: "Kronic",
      tagLine: "NA1",
      summonerId: "summoner-1"
    },
    filters: {
      queue: overrides.queue ?? "ALL",
      championFilter: "",
      sort: "DATE_DESC",
      queueOptions: [
        { value: "ALL", label: "All" },
        { value: "RANKED_SOLO_DUO", label: "Solo/Duo" }
      ],
      championOptions: [],
      sortOptions: [{ value: "DATE_DESC", label: "Most recent" }],
      onQueueChange: overrides.onQueueChange ?? vi.fn(),
      onChampionFilterChange: vi.fn(),
      onSortChange: vi.fn()
    },
    pageState: {
      page: 1,
      history: null,
      historyBusy: overrides.historyBusy ?? false,
      historyError: overrides.historyError ?? null,
      visibleMatches: [],
      onPreviousPage: vi.fn(),
      onNextPage: overrides.onNextPage ?? vi.fn()
    },
    expansion: {
      expandedMatchId: null,
      details: {},
      detailBusy: {},
      onToggleExpanded: vi.fn()
    },
    prefersReducedMotion: true,
  };
}

describe("MatchHistorySection", () => {
  it("distinguishes loading, a genuinely empty history, and an empty filtered result", () => {
    const { rerender } = render(<MatchHistorySection {...props({ historyBusy: true })} />);
    expect(screen.queryByText("No matches found for the current queue/champion filters.")).toBeNull();

    rerender(<MatchHistorySection {...props()} />);
    expect(
      screen.getByText("No ranked matches are recorded yet. Use Update Now to fetch the latest history.")
    ).toBeTruthy();

    rerender(<MatchHistorySection {...props({ queue: "RANKED_SOLO_DUO" })} />);
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
