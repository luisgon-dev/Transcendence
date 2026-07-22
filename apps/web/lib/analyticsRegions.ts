import "server-only";

import { cookies } from "next/headers";
import { cache } from "react";

import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import {
  ANALYTICS_REGION_COOKIE,
  analyticsRegionLabel,
  GLOBAL_ANALYTICS_REGION,
  normalizeAnalyticsRegionCode,
  shouldUseFallbackAnalyticsRegions,
  type AnalyticsRegionOption
} from "@/lib/analyticsRegionShared";

const FALLBACK_OPTIONS: AnalyticsRegionOption[] = [
  { code: GLOBAL_ANALYTICS_REGION, label: "Global", isDefault: true },
  { code: "NA1", label: "North America" },
  { code: "EUW1", label: "Europe West" },
  { code: "EUN1", label: "Europe Nordic & East" },
  { code: "KR", label: "Korea" }
];

// Deduplicate region options shared by layouts and page-level selectors in one render.
export const fetchAnalyticsRegions = cache(async (): Promise<AnalyticsRegionOption[]> => {
  const result = await fetchBackendJson<AnalyticsRegionOption[]>(
    `${getBackendBaseUrl()}/api/lol/analytics/regions`,
    { next: { revalidate: 60 * 60 } }
  );

  if (!result.ok || shouldUseFallbackAnalyticsRegions(result.body)) {
    return FALLBACK_OPTIONS;
  }

  return result.body ?? FALLBACK_OPTIONS;
});

export async function resolveAnalyticsRegion(
  regionParam: string | null | undefined
): Promise<{
  activeRegion: string;
  activeRegionLabel: string;
  options: AnalyticsRegionOption[];
}> {
  const options = await fetchAnalyticsRegions();
  const cookieStore = await cookies();
  const cookieRegion = cookieStore.get(ANALYTICS_REGION_COOKIE)?.value ?? null;
  const activeRegion = normalizeAnalyticsRegionCode(regionParam ?? cookieRegion, options);

  return {
    activeRegion,
    activeRegionLabel: analyticsRegionLabel(activeRegion, options),
    options
  };
}
