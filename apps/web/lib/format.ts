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

export function formatDateTimeMs(value: number | null | undefined) {
  if (value == null || !Number.isFinite(value)) return "-";
  try {
    return new Date(value).toLocaleString();
  } catch {
    return String(value);
  }
}

