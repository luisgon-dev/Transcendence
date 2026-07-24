import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { MatchHistorySection } from "./MatchHistorySection";
import type { MatchSummary } from "./shared";

type TestOverrides = {
  queue?: string;
  historyBusy?: boolean;
  historyError?: string | null;
  visibleMatches?: MatchSummary[];
  onQueueChange?: (value: string) => void;
  onNextPage?: () => void;
  onToggleExpanded?: (matchId: string) => void;
};

const MATCH: MatchSummary = {
  matchId: "NA1_123",
  matchDate: Date.UTC(2026, 6, 22, 12, 0, 0),
  durationSeconds: 2032,
  queueId: 420,
  queueType: "RANKED_SOLO_5x5",
  win: true,
  championId: 236,
  teamPosition: "BOTTOM",
  kills: 24,
  deaths: 10,
  assists: 10,
  visionScore: 23,
  damageToChamps: 53_985,
  csPerMin: 8.4,
  summonerSpell1Id: 4,
  summonerSpell2Id: 21,
  items: [6675, 3508, 3031, 3026, 3036, 3156, 3363],
  runesDetail: {
    primaryStyleId: 8000,
    subStyleId: 8300,
    primarySelections: [8005, 8009, 9103, 8014],
    subSelections: [8345, 8347],
    statShards: [5005, 5008, 5001]
  }
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
      visibleMatches: overrides.visibleMatches ?? [],
      onPreviousPage: vi.fn(),
      onNextPage: overrides.onNextPage ?? vi.fn()
    },
    expansion: {
      expandedMatchId: null,
      details: {},
      detailBusy: {},
      onToggleExpanded: overrides.onToggleExpanded ?? vi.fn()
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

  it("groups champion loadout, items, core stats, and expansion in one snapshot", async () => {
    const user = userEvent.setup();
    const onToggleExpanded = vi.fn();
    render(
      <MatchHistorySection
        {...props({ visibleMatches: [MATCH], onToggleExpanded })}
      />
    );

    const matchButton = screen.getByRole("button", {
      name: "Victory on Unknown champion. KDA 24/10/10. 33:52."
    });
    expect(screen.getByLabelText("Summoner spells")).toBeTruthy();
    expect(screen.getByLabelText("Rune preview")).toBeTruthy();
    expect(screen.getByLabelText("Item build preview")).toBeTruthy();
    expect(screen.getByText("8.4 CS/min")).toBeTruthy();
    expect(screen.getByText("3.40 KDA")).toBeTruthy();
    expect(screen.getByText("Details")).toBeTruthy();

    await user.click(matchButton);
    expect(onToggleExpanded).toHaveBeenCalledWith("NA1_123");
  });
});
