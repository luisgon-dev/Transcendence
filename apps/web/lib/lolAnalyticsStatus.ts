import "server-only";

import { cache } from "react";

import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";

export type LolAnalyticsStatus = {
  patch: string | null;
  activePatchReleasedAtUtc: string | null;
  activePatchDetectedAtUtc: string | null;
};

// Deduplicate the status lookup across layouts and sibling server components in one render.
export const fetchLolAnalyticsStatus = cache(async (): Promise<LolAnalyticsStatus | null> => {
  const res = await fetchBackendJson<LolAnalyticsStatus>(
    `${getBackendBaseUrl()}/api/lol/analytics/status`,
    { next: { revalidate: 60 } }
  );

  return res.ok ? (res.body ?? null) : null;
});
