import type { components } from "@transcendence/api-client";

import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { BackendErrorCard } from "@/components/BackendErrorCard";
import { Card } from "@/components/ui/Card";
import { Badge } from "@/components/ui/Badge";
import { MatchupsExplorerClient } from "@/components/MatchupsExplorerClient";
import {
  fetchWithGlobalAnalyticsRegionFallback,
  resolveAnalyticsRegionPresentation
} from "@/lib/analyticsRegionFallback";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { GLOBAL_ANALYTICS_REGION } from "@/lib/analyticsRegionShared";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { fetchChampionMap } from "@/lib/staticData";
import { normalizeTierListEntries } from "@/lib/tierlist";

type TierListResponse = components["schemas"]["TierListResponse"];

export default async function MatchupsIndexPage({
  searchParams
}: {
  searchParams?: Promise<{ region?: string }>;
}) {
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const { activeRegion, activeRegionLabel, options: regionOptions } = await resolveAnalyticsRegion(
    resolvedSearchParams?.region
  );
  const verbosity = getErrorVerbosity();
  const fetchTierList = async (region: string) => {
    const requestQuery = new URLSearchParams();
    if (region !== GLOBAL_ANALYTICS_REGION) requestQuery.set("region", region);

    return fetchBackendJson<TierListResponse>(
      `${getBackendBaseUrl()}/api/lol/analytics/tierlist?${requestQuery.toString()}`,
      {
        next: { revalidate: 60 * 60 }
      }
    );
  };
  const [{ version, champions }, tierListFetch] = await Promise.all([
    fetchChampionMap(),
    fetchWithGlobalAnalyticsRegionFallback(activeRegion, fetchTierList)
  ]);
  const { result: tierListRes, usedGlobalFallback } = tierListFetch;
  const {
    effectiveRegion,
    effectiveRegionLabel,
    fallbackMessage
  } = resolveAnalyticsRegionPresentation(activeRegion, activeRegionLabel, regionOptions, usedGlobalFallback);

  if (!tierListRes.ok) {
    return (
      <BackendErrorCard
        title="Matchup Analysis"
        message={
          tierListRes.errorKind === "timeout"
            ? "This page is taking too long to load."
            : tierListRes.errorKind === "unreachable"
              ? "We couldn't load matchup data right now."
              : "We couldn't load matchup data."
        }
        requestId={tierListRes.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify({ status: tierListRes.status, errorKind: tierListRes.errorKind }, null, 2)
            : null
        }
      >
        <div className="grid gap-4">
          <p className="type-ui text-fg/70">
            Try switching back to Global or another region.
          </p>
          <AnalyticsRegionFilter options={regionOptions} activeRegion={activeRegion} />
        </div>
      </BackendErrorCard>
    );
  }

  const tierEntries = tierListRes.ok
    ? normalizeTierListEntries(tierListRes.body?.entries ?? [])
    : [];

  const popular = tierEntries
    .slice()
    .sort((a, b) => b.games - a.games)
    .slice(0, 120)
    .map((entry) => ({
      championId: entry.championId,
      role: entry.role,
      games: entry.games,
      winRate: entry.winRate
    }));

  return (
    <div className="grid gap-8">
      <header className="page-hero p-5 md:p-8">
        <div className="flex flex-wrap items-center gap-2">
          <Badge className="border-primary/40 bg-primary/10 text-primary">
            Matchup Tool
          </Badge>
          <Badge>{popular.length} role pages</Badge>
        </div>
        <h1 className="type-title mt-4 sm:text-[2.4rem]">
          Matchup Analysis
        </h1>
        <p className="type-ui mt-3 text-fg/75">
          Search for a champion, pick a role, and jump straight to counters and favorable lanes.
        </p>
        {fallbackMessage ? (
          <p className="type-ui mt-3 text-fg/68">{fallbackMessage}</p>
        ) : null}
        <div className="mt-4 flex flex-wrap items-center gap-2">
          <Badge>{effectiveRegionLabel}</Badge>
          <AnalyticsRegionFilter options={regionOptions} activeRegion={effectiveRegion} />
        </div>
        <div className="mt-3">
          <AnalyticsSampleBanner
            sample={(tierListRes.body as { sample?: unknown } | null)?.sample as AnalyticsSampleLike}
          />
        </div>
      </header>

      <Card className="page-panel p-4 md:p-5">
        <MatchupsExplorerClient
          entries={popular}
          champions={champions}
          version={version}
          activeRegion={effectiveRegion}
        />
      </Card>
    </div>
  );
}
