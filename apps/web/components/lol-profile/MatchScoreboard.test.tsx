import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { MatchScoreboard } from "./MatchScoreboard";
import { TooltipProvider } from "@/components/ui/Tooltip";
import type { MatchDetail, MatchParticipant } from "./shared";

function participant(overrides: Partial<MatchParticipant>): MatchParticipant {
  return {
    puuid: "puuid",
    gameName: "Player",
    tagLine: "NA1",
    teamId: 100,
    championId: 1,
    teamPosition: "TOP",
    win: false,
    kills: 2,
    deaths: 4,
    assists: 6,
    champLevel: 16,
    goldEarned: 12_000,
    totalDamageDealtToChampions: 20_000,
    physicalDamageDealtToChampions: 10_000,
    magicDamageDealtToChampions: 9_000,
    trueDamageDealtToChampions: 1_000,
    visionScore: 18,
    totalMinionsKilled: 180,
    neutralMinionsKilled: 12,
    summonerSpell1Id: 4,
    summonerSpell2Id: 12,
    items: [3078, 3053, 3065, 3111, 0, 0, 3340],
    runes: {
      primaryStyleId: 8000,
      subStyleId: 8400,
      primarySelections: [8005, 9111, 9104, 8014],
      subSelections: [8444, 8451],
      statShards: [5005, 5008, 5001]
    },
    ...overrides
  };
}

const DETAIL: MatchDetail = {
  matchId: "NA1_123",
  matchDate: Date.UTC(2026, 6, 22, 12, 0, 0),
  duration: 1800,
  queueId: 420,
  queueType: "RANKED_SOLO_5x5",
  patch: "16.14",
  participants: [
    participant({
      puuid: "kronic",
      gameName: "Kronic",
      tagLine: "NA1",
      performance: {
        score: 8.7,
        teamRank: 1,
        teamSize: 5,
        label: "ACE",
        killParticipation: 0.72,
        damageShare: 0.31,
        goldShare: 0.24,
        visionShare: 0.17,
        csPerMin: 6.4
      }
    }),
    participant({
      puuid: "opponent",
      gameName: "Opponent",
      tagLine: "NA1",
      teamId: 200,
      win: true,
      kills: 7,
      deaths: 2,
      assists: 5,
      performance: {
        score: 9.2,
        teamRank: 1,
        teamSize: 5,
        label: "MVP",
        killParticipation: 0.78,
        damageShare: 0.34,
        goldShare: 0.27,
        visionShare: 0.13,
        csPerMin: 7.1
      }
    })
  ],
  bans: [],
  objectives: []
};

describe("MatchScoreboard", () => {
  it("always renders the detailed scoreboard without a density choice", () => {
    render(
      <TooltipProvider>
        <MatchScoreboard
          detail={DETAIL}
          summonerId=""
          region="na"
          gameName="Kronic"
          tagLine="NA1"
        />
      </TooltipProvider>
    );

    expect(screen.getByRole("group", { name: "Match detail view" })).toBeTruthy();
    expect(screen.queryByRole("group", { name: "Scoreboard density" })).toBeNull();
    expect(screen.queryByText("Compact")).toBeNull();
    expect(screen.queryByText("Detailed")).toBeNull();
    expect(screen.getAllByRole("columnheader", { name: "Vision" })).toHaveLength(2);
    expect(screen.getAllByRole("columnheader", { name: "Gold" })).toHaveLength(2);
    expect(screen.getAllByLabelText("Summoner spells")).toHaveLength(2);
    expect(screen.getByText("ACE")).toBeTruthy();
    expect(screen.getByText("MVP")).toBeTruthy();
  });
});
