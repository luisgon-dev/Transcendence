export type PercentInput = "auto" | "ratio" | "percent";

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

export function winRateColorClass(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return "";
  const pct = Math.abs(value) >= 1.5 ? value : value * 100;
  if (pct >= 52) return "text-wr-high";
  if (pct < 48) return "text-wr-low";
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

