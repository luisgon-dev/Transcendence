export type AnalyticsRegionOption = {
  code: string;
  label: string;
  isDefault?: boolean;
};

export const ANALYTICS_REGION_COOKIE = "trn_analytics_region";
export const ANALYTICS_REGION_STORAGE_KEY = "trn.analytics.region";
export const GLOBAL_ANALYTICS_REGION = "ALL";

const FALLBACK_LABELS: Record<string, string> = {
  ALL: "Global",
  NA1: "North America",
  EUW1: "Europe West",
  EUN1: "Europe Nordic & East",
  KR: "Korea"
};

export function normalizeAnalyticsRegionCode(
  value: string | null | undefined,
  options: readonly AnalyticsRegionOption[]
): string {
  const normalized = (value ?? "").trim().toUpperCase();
  if (!normalized) {
    return options.find((option) => option.isDefault)?.code ?? GLOBAL_ANALYTICS_REGION;
  }

  return options.some((option) => option.code === normalized)
    ? normalized
    : options.find((option) => option.isDefault)?.code ?? GLOBAL_ANALYTICS_REGION;
}

export function analyticsRegionLabel(
  value: string | null | undefined,
  options: readonly AnalyticsRegionOption[]
): string {
  const normalized = normalizeAnalyticsRegionCode(value, options);
  return (
    options.find((option) => option.code === normalized)?.label ??
    FALLBACK_LABELS[normalized] ??
    normalized
  );
}
