import "server-only";

import { cache } from "react";

import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { type LolAnalyticsPatchOption } from "@/lib/lolPatchFilters";

// cache() dedupes the patches lookup across the page and any sibling components in one render.
export const fetchLolAnalyticsPatches = cache(async (): Promise<LolAnalyticsPatchOption[]> => {
  const res = await fetchBackendJson<LolAnalyticsPatchOption[]>(
    `${getBackendBaseUrl()}/api/lol/analytics/patches`,
    { next: { revalidate: 60 * 10 } }
  );

  return res.ok ? (res.body ?? []) : [];
});
