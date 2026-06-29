import { describe, expect, it } from "vitest";

import { deriveMatchTakeaways } from "@/lib/matchInsights";
import type { MatchDetail, MatchParticipant, MatchTimeline } from "@/components/lol-profile/shared";

const ME_NAME = "Tester";
const ME_TAG = "EUW";
const ROLES = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"] as const;

function makeParticipant(over: Partial<MatchParticipant> & { teamId: number; teamPosition: string }): MatchParticipant {
  return {
    puuid: null,
    gameName: null,
    tagLine: null,
    championId: 1,
    win: over.teamId === 100,
    kills: 2,
    deaths: 5,
    assists: 3,
    champLevel: 18,
    goldEarned: 12000,
    totalDamageDealtToChampions: 10000,
    visionScore: 20,
    totalMinionsKilled: 150,
    neutralMinionsKilled: 0,
    summonerSpell1Id: 4,
    summonerSpell2Id: 7,
    items: [],
    runes: { primaryStyleId: 0, subStyleId: 0, primarySelections: [], subSelections: [], statShards: [] },
    ...over
  };
}

// Baseline 5v5 (30 min) tuned so NOTHING fires: lane CS even, KP 0.5, deaths even,
// one teammate out-damages "me", and one enemy out-visions "me". Each test mutates
// the viewing player to trip exactly one candidate.
function baselineParticipants(): MatchParticipant[] {
  const list: MatchParticipant[] = [];
  for (const teamId of [100, 200] as const) {
    ROLES.forEach((role) => {
      const isMe = teamId === 100 && role === "BOTTOM";
      list.push(
        makeParticipant({
          teamId,
          teamPosition: role,
          gameName: isMe ? ME_NAME : `P${teamId}${role}`,
          tagLine: isMe ? ME_TAG : "EUW",
          // A blue teammate out-damages me so top-team-damage stays silent by default.
          totalDamageDealtToChampions: teamId === 100 && role === "TOP" ? 12000 : 10000,
          // An enemy out-visions me so top-vision stays silent by default.
          visionScore: teamId === 200 && role === "JUNGLE" ? 25 : 20
        })
      );
    });
  }
  return list;
}

function makeDetail(participants: MatchParticipant[], over: Partial<MatchDetail> = {}): MatchDetail {
  return {
    matchId: "M1",
    matchDate: 0,
    duration: 1800,
    queueId: 420,
    queueType: "RANKED_SOLO_5x5",
    participants,
    ...over
  };
}

function me(participants: MatchParticipant[]): MatchParticipant {
  return participants.find((p) => p.gameName === ME_NAME)!;
}

function derive(participants: MatchParticipant[], timeline?: MatchTimeline | null, limit?: number) {
  return deriveMatchTakeaways({
    detail: makeDetail(participants),
    gameName: ME_NAME,
    tagLine: ME_TAG,
    timeline,
    limit
  });
}

describe("deriveMatchTakeaways", () => {
  it("returns nothing when the baseline is neutral", () => {
    expect(derive(baselineParticipants())).toEqual([]);
  });

  it("returns empty when the viewing player is not in the lobby", () => {
    const result = deriveMatchTakeaways({
      detail: makeDetail(baselineParticipants()),
      gameName: "Nobody",
      tagLine: "NA1"
    });
    expect(result).toEqual([]);
  });

  it("flags a CS lead over the lane opponent", () => {
    const players = baselineParticipants();
    me(players).totalMinionsKilled = 450; // 15 CS/min vs 5
    const result = derive(players);
    expect(result).toEqual([{ tone: "good", text: "+10.0 CS/min on your lane opponent" }]);
  });

  it("flags a CS deficit vs the lane opponent", () => {
    const players = baselineParticipants();
    me(players).totalMinionsKilled = 60; // 2 CS/min vs 5
    const result = derive(players);
    expect(result).toEqual([{ tone: "bad", text: "-3.0 CS/min behind your lane opponent" }]);
  });

  it("flags high kill participation", () => {
    const players = baselineParticipants();
    me(players).assists = 6; // (2 + 6) / 10 team kills = 0.8
    const result = derive(players);
    expect(result).toEqual([{ tone: "good", text: "80% kill participation" }]);
  });

  it("flags low kill participation as neutral", () => {
    const players = baselineParticipants();
    me(players).kills = 1;
    me(players).assists = 1; // (1 + 1) / 9 team kills = 0.22
    const result = derive(players);
    expect(result).toEqual([{ tone: "neutral", text: "22% kill participation" }]);
  });

  it("flags deaths above the team average", () => {
    const players = baselineParticipants();
    me(players).deaths = 9; // team avg = (9 + 5*4) / 5 = 5.8
    const result = derive(players);
    expect(result).toEqual([{ tone: "bad", text: "Died 9 times, above your team average" }]);
  });

  it("flags leading the team in damage", () => {
    const players = baselineParticipants();
    me(players).totalDamageDealtToChampions = 20000; // top of 62k team total = 32%
    const result = derive(players);
    expect(result).toEqual([{ tone: "good", text: "Led your team in damage (32%)" }]);
  });

  it("flags the top vision score in the lobby", () => {
    const players = baselineParticipants();
    me(players).visionScore = 30; // beats the enemy jungle's 25
    const result = derive(players);
    expect(result).toEqual([{ tone: "good", text: "Top vision score in the lobby (30)" }]);
  });

  it("flags the deepest gold deficit from the timeline", () => {
    const timeline: MatchTimeline = {
      matchId: "M1",
      duration: 1800,
      frames: [
        { minuteMark: 0, blueGold: 1000, redGold: 1000, blueXp: 0, redXp: 0 },
        { minuteMark: 10, blueGold: 1000, redGold: 5000, blueXp: 0, redXp: 0 },
        { minuteMark: 20, blueGold: 8000, redGold: 5000, blueXp: 0, redXp: 0 }
      ]
    };
    const result = derive(baselineParticipants(), timeline);
    expect(result).toEqual([{ tone: "bad", text: "Down 4.0k gold at 10:00" }]);
  });

  it("flags closing out a gold lead in a win", () => {
    const timeline: MatchTimeline = {
      matchId: "M1",
      duration: 1800,
      frames: [
        { minuteMark: 0, blueGold: 1000, redGold: 1000, blueXp: 0, redXp: 0 },
        { minuteMark: 25, blueGold: 6000, redGold: 1000, blueXp: 0, redXp: 0 }
      ]
    };
    const result = derive(baselineParticipants(), timeline);
    expect(result).toEqual([{ tone: "good", text: "Closed out a 5.0k gold lead" }]);
  });

  it("caps the output at the limit and sorts by salience", () => {
    const players = baselineParticipants();
    const player = me(players);
    player.totalMinionsKilled = 450; // lane CS good, salience 10
    player.assists = 6; // KP 0.8, salience 1.2
    player.totalDamageDealtToChampions = 20000; // top damage, salience ~0.97
    player.visionScore = 30; // top vision, salience 2
    const timeline: MatchTimeline = {
      matchId: "M1",
      duration: 1800,
      frames: [
        { minuteMark: 0, blueGold: 1000, redGold: 1000, blueXp: 0, redXp: 0 },
        { minuteMark: 25, blueGold: 6000, redGold: 1000, blueXp: 0, redXp: 0 } // closed lead, salience 2.5
      ]
    };

    const result = derive(players, timeline);
    // 5 candidates fire; default limit 4 drops the lowest-salience (damage).
    expect(result).toHaveLength(4);
    expect(result.map((t) => t.text)).toEqual([
      "+10.0 CS/min on your lane opponent",
      "Closed out a 5.0k gold lead",
      "Top vision score in the lobby (30)",
      "80% kill participation"
    ]);
    expect(result.some((t) => t.text.startsWith("Led your team in damage"))).toBe(false);

    // Explicit limit is respected.
    const top2 = derive(players, timeline, 2);
    expect(top2.map((t) => t.text)).toEqual([
      "+10.0 CS/min on your lane opponent",
      "Closed out a 5.0k gold lead"
    ]);
  });
});
