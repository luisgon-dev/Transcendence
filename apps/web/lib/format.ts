export type PercentInput = "auto" | "ratio" | "percent";

export const FAVORABLE_WIN_RATE_PERCENT = 52;
export const UNFAVORABLE_WIN_RATE_PERCENT = 48;

export function formatPercent(
  value: number | null | undefined,
  {
    decimals = 1,
    input = "auto"
  }: {
    decimals?: number;
    input?: PercentInput;
  } = {}
) {
  if (value == null || !Number.isFinite(value)) return "-";

  const abs = Math.abs(value);
  const asPercent =
    input === "percent" || (input === "auto" && abs >= 1.5) ? value : value * 100;

  return `${asPercent.toFixed(decimals)}%`;
}

export function formatDurationSeconds(value: number | null | undefined) {
  if (value == null || !Number.isFinite(value) || value < 0) return "-";

  const total = Math.floor(value);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;

  if (hours > 0) {
    return `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(
      2,
      "0"
    )}`;
  }
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}

// Normalize a win rate that may arrive as a 0-1 ratio or a 0-100 percent into a
// percent number (0-100). Returns null when not finite.
export function toWinPercent(value: number | null | undefined): number | null {
  if (value == null || !Number.isFinite(value)) return null;
  return Math.abs(value) >= 1.5 ? value : value * 100;
}

export function matchupVerdict(value: number | null | undefined): "Favored" | "Even" | "Unfavored" {
  const pct = toWinPercent(value);
  if (pct != null && pct >= FAVORABLE_WIN_RATE_PERCENT) return "Favored";
  if (pct != null && pct < UNFAVORABLE_WIN_RATE_PERCENT) return "Unfavored";
  return "Even";
}

export function winRateColorClass(value: number | null | undefined): string {
  const pct = toWinPercent(value);
  if (pct == null) return "";
  if (pct >= FAVORABLE_WIN_RATE_PERCENT) return "text-wr-high";
  if (pct < UNFAVORABLE_WIN_RATE_PERCENT) return "text-wr-low";
  return "";
}

export function kdaColorClass(kda: number | null | undefined): string {
  if (kda == null || !Number.isFinite(kda)) return "";
  if (kda >= 3) return "text-wr-high";
  if (kda < 2) return "text-wr-low";
  return "";
}

export function formatGames(count: number | null | undefined): string {
  if (count == null || !Number.isFinite(count)) return "-";
  return Math.floor(count).toLocaleString();
}

export function formatCompactNumber(
  value: number | null | undefined,
  { fallback = "—", decimals = 1 }: { fallback?: string; decimals?: number } = {}
): string {
  if (value == null || !Number.isFinite(value)) return fallback;
  if (Math.abs(value) >= 1000) return `${(value / 1000).toFixed(decimals)}k`;
  return Math.round(value).toLocaleString();
}

// Count-agreeing noun: plural(1, "game") → "game", plural(3, "game") → "games".
// Pair with the caller's own number formatting, e.g. `{formatGames(n)} {plural(n, "game")}`.
export function plural(count: number | null | undefined, noun: string): string {
  return count === 1 ? noun : `${noun}s`;
}

export function formatRelativeTime(timestamp: number | null | undefined): string {
  if (timestamp == null || !Number.isFinite(timestamp)) return "";
  const now = Date.now();
  const diffMs = now - timestamp;
  if (diffMs < 0) return "just now";

  const minutes = Math.floor(diffMs / 60_000);
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes}m ago`;

  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;

  const days = Math.floor(hours / 24);
  if (days < 30) return `${days}d ago`;

  return `${Math.floor(days / 30)}mo ago`;
}

export function formatDateTimeMs(value: number | null | undefined) {
  if (value == null || !Number.isFinite(value)) return "-";
  try {
    return new Date(value).toLocaleString();
  } catch {
    return String(value);
  }
}
