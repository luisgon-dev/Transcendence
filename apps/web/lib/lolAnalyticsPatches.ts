import "server-only";

import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { type LolAnalyticsPatchOption } from "@/lib/lolPatchFilters";

export async function fetchLolAnalyticsPatches(): Promise<LolAnalyticsPatchOption[]> {
  const res = await fetchBackendJson<LolAnalyticsPatchOption[]>(
    `${getBackendBaseUrl()}/api/lol/analytics/patches`,
    { next: { revalidate: 60 * 10 } }
  );

  return res.ok ? (res.body ?? []) : [];
}
