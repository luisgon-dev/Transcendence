import type { MatchSummary } from "@/components/lol-profile/shared";

export type RecentFormTone = "up" | "steady" | "down";

export type RecentForm = {
  tone: RecentFormTone;
  label: string;
  recentAverage: number;
  previousAverage: number;
  delta: number;
  recentGames: number;
  previousGames: number;
};

function average(values: number[]): number {
  return values.reduce((sum, value) => sum + value, 0) / values.length;
}

// Compares the latest five scored games with up to five games immediately before
// them. Eight games are required so a single prior result never becomes a trend.
export function deriveRecentForm(
  matches: Array<Pick<MatchSummary, "matchDate" | "performance">>
): RecentForm | null {
  const scores = matches
    .filter((match) =>
      Number.isFinite(match.performance?.score) &&
      (match.performance?.teamSize ?? 0) >= 2
    )
    .sort((a, b) => b.matchDate - a.matchDate)
    .slice(0, 10)
    .map((match) => match.performance?.score as number);

  if (scores.length < 8) return null;

  const recent = scores.slice(0, 5);
  const previous = scores.slice(5);
  const recentAverage = average(recent);
  const previousAverage = average(previous);
  const delta = recentAverage - previousAverage;

  if (delta >= 0.5) {
    return {
      tone: "up",
      label: "Trending up",
      recentAverage,
      previousAverage,
      delta,
      recentGames: recent.length,
      previousGames: previous.length
    };
  }

  if (delta <= -0.5) {
    return {
      tone: "down",
      label: "Trending down",
      recentAverage,
      previousAverage,
      delta,
      recentGames: recent.length,
      previousGames: previous.length
    };
  }

  return {
    tone: "steady",
    label: "Holding steady",
    recentAverage,
    previousAverage,
    delta,
    recentGames: recent.length,
    previousGames: previous.length
  };
}
