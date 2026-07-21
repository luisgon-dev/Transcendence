import "server-only";

import { cache } from "react";

import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { normalizeAnalyticsQueue } from "@/lib/analyticsQueues";
import { type LolAnalyticsPatchOption } from "@/lib/lolPatchFilters";

// cache() dedupes the patches lookup across the page and any sibling components in one render.
export const fetchLolAnalyticsPatches = cache(async (queue?: string | null): Promise<LolAnalyticsPatchOption[]> => {
  const normalizedQueue = normalizeAnalyticsQueue(queue);
  const query = normalizedQueue === "solo" ? "" : `?queue=${encodeURIComponent(normalizedQueue)}`;
  const res = await fetchBackendJson<LolAnalyticsPatchOption[]>(
    `${getBackendBaseUrl()}/api/lol/analytics/patches${query}`,
    { next: { revalidate: 60 * 10 } }
  );

  return res.ok ? (res.body ?? []) : [];
});
