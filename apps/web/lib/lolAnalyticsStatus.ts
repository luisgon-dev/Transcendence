import "server-only";

import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";

export type LolAnalyticsStatus = {
  patch: string | null;
  activePatchReleasedAtUtc: string | null;
  activePatchDetectedAtUtc: string | null;
};

export async function fetchLolAnalyticsStatus(): Promise<LolAnalyticsStatus | null> {
  const res = await fetchBackendJson<LolAnalyticsStatus>(
    `${getBackendBaseUrl()}/api/lol/analytics/status`,
    { next: { revalidate: 60 } }
  );

  return res.ok ? (res.body ?? null) : null;
}
