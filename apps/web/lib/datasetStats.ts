import type { components } from "@transcendence/api-client/schema";

export type DatasetStats = components["schemas"]["DatasetStatsDto"];
export type DatasetPlatformStats = components["schemas"]["DatasetPlatformStatsDto"];

const wholeNumber = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });
const compactNumber = new Intl.NumberFormat("en-US", {
  notation: "compact",
  maximumFractionDigits: 1
});

// Fixed "en-US" so the server render and the browser agree on separators.
export function formatCount(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return "—";
  return wholeNumber.format(value);
}

// 4_803_099 → "4.8M". For estimates, where more digits would claim precision the number lacks.
export function formatCompactCount(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return "—";
  return compactNumber.format(value);
}

const BYTE_UNITS = ["B", "KB", "MB", "GB", "TB", "PB"] as const;

// Binary units, labelled like PostgreSQL's pg_size_pretty, so the figure matches what psql reports.
export function formatBytes(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value) || value < 0) return "—";
  let size = value;
  let unit = 0;
  while (size >= 1024 && unit < BYTE_UNITS.length - 1) {
    size /= 1024;
    unit += 1;
  }
  const digits = unit === 0 || size >= 100 ? 0 : 1;
  return `${size.toFixed(digits)} ${BYTE_UNITS[unit]}`;
}

// The platforms whose ladders are crawled. Falls back to the platforms present in the data when the
// worker reported no crawl configuration, so the figure is never a misleading zero.
export function crawledPlatformCount(stats: DatasetStats): number {
  return stats.crawledPlatforms.length > 0 ? stats.crawledPlatforms.length : stats.platforms.length;
}
