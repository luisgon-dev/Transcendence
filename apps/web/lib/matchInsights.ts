import { formatDurationSeconds } from "@/lib/format";
import {
  isCurrentProfilePlayer,
  normalizeRoleKey,
  type MatchDetail,
  type MatchParticipant,
  type MatchTimeline,
  type ProfileOverviewStats
} from "@/components/lol-profile/shared";

export type TakeawayTone = "good" | "bad" | "neutral";
export type Takeaway = { tone: TakeawayTone; text: string };

// Salience is an internal ranking weight only — never surfaced to the UI.
type ScoredTakeaway = Takeaway & { salience: number };

function csPerMinute(participant: MatchParticipant, minutes: number): number {
  return (participant.totalMinionsKilled + participant.neutralMinionsKilled) / minutes;
}

// Pure, deterministic post-game summary. Builds a ranked set of plain-language
// takeaways for the viewing player and returns the top `limit` by salience.
export function deriveMatchTakeaways(input: {
  detail: MatchDetail;
  gameName: string;
  tagLine: string;
  timeline?: MatchTimeline | null;
  overviewStats?: ProfileOverviewStats | null;
  limit?: number;
}): Takeaway[] {
  const { detail, gameName, tagLine, timeline, overviewStats, limit = 4 } = input;

  const me = detail.participants.find((p) => isCurrentProfilePlayer(p, gameName, tagLine));
  if (!me) return [];

  const minutes = Math.max(1, detail.duration / 60);
  const team = detail.participants.filter((p) => p.teamId === me.teamId);
  const enemies = detail.participants.filter((p) => p.teamId !== me.teamId);

  const myCsPerMin = csPerMinute(me, minutes);
  const myRole = normalizeRoleKey(me.teamPosition);
  const laneOpp =
    myRole !== "UNKNOWN" ? enemies.find((p) => normalizeRoleKey(p.teamPosition) === myRole) : undefined;

  const teamKills = team.reduce((sum, p) => sum + p.kills, 0);
  const kp = teamKills > 0 ? (me.kills + me.assists) / teamKills : 0;

  const teamDamage = team.reduce((sum, p) => sum + p.totalDamageDealtToChampions, 0);
  const myDmgShare = teamDamage > 0 ? me.totalDamageDealtToChampions / teamDamage : 0;
  const maxTeamDamage = Math.max(...team.map((p) => p.totalDamageDealtToChampions));
  const isTopTeamDamage = me.totalDamageDealtToChampions === maxTeamDamage && team.length > 1;

  const teamAvgDeaths = team.reduce((sum, p) => sum + p.deaths, 0) / team.length;
  const lobbyMaxVision = Math.max(...detail.participants.map((p) => p.visionScore));

  const candidates: ScoredTakeaway[] = [];

  // 1. Lane CS differential vs the role-aligned enemy.
  if (laneOpp) {
    const d = myCsPerMin - csPerMinute(laneOpp, minutes);
    if (d >= 1) {
      candidates.push({ tone: "good", text: `+${d.toFixed(1)} CS/min on your lane opponent`, salience: Math.abs(d) });
    } else if (d <= -1) {
      candidates.push({ tone: "bad", text: `${d.toFixed(1)} CS/min behind your lane opponent`, salience: Math.abs(d) });
    }
  }

  // 2. CS vs the player's own running average.
  if (overviewStats?.avgCsPerMin && overviewStats.avgCsPerMin > 0) {
    const avg = overviewStats.avgCsPerMin;
    const d = myCsPerMin - avg;
    if (d >= 1) {
      candidates.push({
        tone: "good",
        text: `${myCsPerMin.toFixed(1)} CS/min, above your ${avg.toFixed(1)} average`,
        salience: Math.abs(d) * 0.8
      });
    } else if (d <= -1) {
      candidates.push({
        tone: "bad",
        text: `${myCsPerMin.toFixed(1)} CS/min, below your ${avg.toFixed(1)} average`,
        salience: Math.abs(d) * 0.8
      });
    }
  }

  // 3. Kill participation.
  if (kp >= 0.65) {
    candidates.push({ tone: "good", text: `${Math.round(kp * 100)}% kill participation`, salience: (kp - 0.5) * 4 });
  } else if (kp <= 0.4 && teamKills > 0) {
    candidates.push({ tone: "neutral", text: `${Math.round(kp * 100)}% kill participation`, salience: (0.5 - kp) * 3 });
  }

  // 4. Death count outlier (vs team, then vs own average).
  if (me.deaths >= teamAvgDeaths + 2) {
    candidates.push({
      tone: "bad",
      text: `Died ${me.deaths} times, above your team average`,
      salience: me.deaths - teamAvgDeaths
    });
  } else if (overviewStats?.avgDeaths && me.deaths >= overviewStats.avgDeaths + 2) {
    candidates.push({
      tone: "bad",
      text: `Died ${me.deaths} times vs your ${overviewStats.avgDeaths.toFixed(1)} average`,
      salience: me.deaths - overviewStats.avgDeaths
    });
  }

  // 5. Team damage lead.
  if (isTopTeamDamage) {
    candidates.push({
      tone: "good",
      text: `Led your team in damage (${Math.round(myDmgShare * 100)}%)`,
      salience: myDmgShare * 3
    });
  }

  // 6. Lobby-leading vision.
  if (lobbyMaxVision > 0 && me.visionScore === lobbyMaxVision) {
    candidates.push({ tone: "good", text: `Top vision score in the lobby (${me.visionScore})`, salience: 2 });
  }

  // 7. Gold-curve inflection (deepest deficit, or a closed-out lead in a win).
  if (timeline && timeline.frames.length >= 2) {
    const teamDiff = (frame: { blueGold: number; redGold: number }) =>
      me.teamId === 100 ? frame.blueGold - frame.redGold : frame.redGold - frame.blueGold;

    let minFrame = timeline.frames[0];
    let minDiff = teamDiff(minFrame);
    for (const frame of timeline.frames) {
      const diff = teamDiff(frame);
      if (diff < minDiff) {
        minDiff = diff;
        minFrame = frame;
      }
    }
    const endDiff = teamDiff(timeline.frames[timeline.frames.length - 1]);

    if (minDiff <= -2500) {
      candidates.push({
        tone: "bad",
        text: `Down ${(Math.abs(minDiff) / 1000).toFixed(1)}k gold at ${formatDurationSeconds(minFrame.minuteMark * 60)}`,
        salience: Math.abs(minDiff) / 2000
      });
    } else if (endDiff >= 2500 && me.win) {
      candidates.push({
        tone: "good",
        text: `Closed out a ${(endDiff / 1000).toFixed(1)}k gold lead`,
        salience: endDiff / 2000
      });
    }
  }

  return candidates
    .sort((a, b) => b.salience - a.salience)
    .slice(0, limit)
    .map(({ tone, text }) => ({ tone, text }));
}
