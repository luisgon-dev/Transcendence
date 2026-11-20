import "server-only";

import type { components } from "@transcendence/api-client";
import { connection } from "next/server";
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

export const fetchPublishedAnalyticsPatches = cache(async (queue?: string | null, rankTier?: string | null): Promise<LolAnalyticsPatchOption[]> => {
  await connection();
  const normalizedQueue = normalizeAnalyticsQueue(queue);
  const params = new URLSearchParams();
  if (normalizedQueue !== "solo") params.set("queue", normalizedQueue);
  if (rankTier) params.set("rankTier", rankTier);
  const query = params.size ? `?${params}` : "";
  const res = await fetchBackendJson<components["schemas"]["PublishedAnalyticsPatchOptionDto"][]>(
    `${getBackendBaseUrl()}/api/lol/analytics/published-patches${query}`,
    { cache: "no-store" }
  );
  if (!res.ok) return [];
  return (res.body ?? []).flatMap(option =>
    typeof option.patch === "string" ? [{
      patch: option.patch,
      releasedAtUtc: option.releasedAtUtc,
      detectedAtUtc: option.detectedAtUtc,
      isActive: option.isActive ?? false,
      rankedSoloDuoMatchCount: option.matchCount ?? 0,
      state: option.state
    }] : []
  );
});
