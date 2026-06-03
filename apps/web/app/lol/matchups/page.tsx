import type { components } from "@transcendence/api-client";

import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { BackendErrorCard } from "@/components/BackendErrorCard";
import { Card } from "@/components/ui/Card";
import { Toolbar } from "@/components/ui/Toolbar";
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
    <div className="grid gap-4">
      <Toolbar
        eyebrow="Matchup Tool"
        title="Matchup Analysis"
        meta={
          <>
            <span>{effectiveRegionLabel}</span>
            <span aria-hidden="true">·</span>
            <span className="type-tabular tabular-nums">{popular.length} role pages</span>
          </>
        }
        filters={<AnalyticsRegionFilter options={regionOptions} activeRegion={effectiveRegion} />}
      />
      {fallbackMessage ? <p className="type-ui px-1 text-muted">{fallbackMessage}</p> : null}
      <AnalyticsSampleBanner
        sample={(tierListRes.body as { sample?: unknown } | null)?.sample as AnalyticsSampleLike}
      />

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
