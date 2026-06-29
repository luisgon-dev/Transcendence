import { formatGames, toWinPercent } from "@/lib/format";
import { formatStrengthDelta } from "@/lib/tierlist";

// Confidence layer: turns a raw game count (and the analytics low-sample flag)
// into a small, trustworthy "how much should I believe this?" signal. Pure
// helpers — the components/tests build on these. Win rates may arrive as a 0-1
// ratio OR a 0-100 percent; everything routes through toWinPercent so callers
// never have to guess the scale.

export type ConfidenceLevel = "high" | "moderate" | "low";

/** Default sample size at which a read is considered "Stable" when callers don't supply one. */
export const DEFAULT_MIN_GAMES = 200;

/**
 * Bucket a sample into a confidence level. An explicit low-sample flag (from the
 * analytics layer) or a non-positive game count always collapses to "low".
 * Otherwise the count is compared against `minGames` (falling back to
 * DEFAULT_MIN_GAMES), with a moderate band down to max(20, 40% of the minimum).
 */
export function confidenceLevel(input: {
  games: number;
  isLowSample?: boolean;
  minGames?: number;
}): ConfidenceLevel {
  const { games, isLowSample, minGames } = input;

  if (isLowSample === true || !(games > 0)) return "low";

  const min = minGames && minGames > 0 ? minGames : DEFAULT_MIN_GAMES;
  if (games >= min) return "high";
  if (games >= Math.max(20, min * 0.4)) return "moderate";
  return "low";
}

/**
 * 95% Wald confidence half-width for a win rate, expressed in PERCENTAGE POINTS
 * (so it can be drawn directly against a percent-scaled bar). Returns 0 with no
 * games; widest near p=0.5 and clamped to 25pp to stay sane on tiny samples.
 */
export function winRateIntervalPP(winRatePercent: number, games: number): number {
  if (!(games > 0)) return 0;

  const pct = toWinPercent(winRatePercent) ?? 0;
  const p = Math.min(1, Math.max(0, pct / 100));
  const half = 1.96 * Math.sqrt((p * (1 - p)) / games) * 100;

  return Math.min(25, Math.max(0, half));
}

/**
 * The graded ("adjusted") win rate as a PERCENT number: the role baseline shifted
 * by the signed strength delta. `strengthScore` may be a fraction (e.g. 0.029) or
 * already in points (e.g. 2.9) — small magnitudes are treated as fractions.
 */
export function adjustedWinRatePercent(input: {
  roleBaseline: number;
  strengthScore: number;
}): number {
  const base = toWinPercent(input.roleBaseline) ?? 0;
  const delta =
    Math.abs(input.strengthScore) < 1.5
      ? input.strengthScore * 100
      : input.strengthScore;

  return base + delta;
}

/** Map a confidence level onto the AnalyticsSampleBanner sample vocabulary. */
export function confidenceSampleLabel(
  level: ConfidenceLevel
): "Stable" | "Light" | "Early" {
  if (level === "high") return "Stable";
  if (level === "moderate") return "Light";
  return "Early";
}

/**
 * One-line empirical-Bayes trust story for a tooltip, e.g.
 * "Observed 58.0% over 120 games · role avg 50.4% · graded +2.9% vs role average".
 */
export function formatEbStory(input: {
  winRate: number;
  roleBaseline: number;
  strengthScore: number;
  games: number;
}): string {
  const observed = toWinPercent(input.winRate) ?? 0;
  const delta = formatStrengthDelta(input.strengthScore);
  const head = `Observed ${observed.toFixed(1)}% over ${formatGames(input.games)} games`;

  const roleAvg = toWinPercent(input.roleBaseline);
  if (roleAvg == null || !(roleAvg > 0)) {
    // No role baseline on the wire — only assert what we can defend.
    return `${head} · graded ${delta} vs role average`;
  }

  // Surface the empirical-Bayes shrinkage: the observed rate vs the graded
  // (baseline + shrunk delta) the tier is actually cut on.
  const adjusted = adjustedWinRatePercent({
    roleBaseline: input.roleBaseline,
    strengthScore: input.strengthScore
  });
  return `${head} · role avg ${roleAvg.toFixed(1)}% · graded ${adjusted.toFixed(
    1
  )}% (${delta} vs role avg)`;
}
