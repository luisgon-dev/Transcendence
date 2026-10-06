import "server-only";

import { cache } from "react";

import { fetchBackendJson } from "@/lib/backendCall";
import type { DatasetStats } from "@/lib/datasetStats";
import { getBackendBaseUrl } from "@/lib/env";

// The worker refreshes the snapshot every 5 minutes and the API caches it for 60 s, so a 60 s
// revalidate keeps "last match ingested" close to the snapshot without adding backend load.
// Null when the backend has no snapshot yet (404) or is unreachable: callers hide the figures.
export const fetchDatasetStats = cache(async (): Promise<DatasetStats | null> => {
  const res = await fetchBackendJson<DatasetStats>(
    `${getBackendBaseUrl()}/api/lol/analytics/dataset`,
    { next: { revalidate: 60 } }
  );

  return res.ok ? (res.body ?? null) : null;
});
