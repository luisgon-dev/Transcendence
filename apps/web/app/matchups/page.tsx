import type { components } from "@transcendence/api-client/schema";

import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { BackendErrorCard } from "@/components/BackendErrorCard";
import { Card } from "@/components/ui/Card";
import { Badge } from "@/components/ui/Badge";
import { MatchupsExplorerClient } from "@/components/MatchupsExplorerClient";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { type AnalyticsSampleLike } from "@/lib/analyticsSample";
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
  const tierListQuery = new URLSearchParams();
  if (activeRegion !== "ALL") tierListQuery.set("region", activeRegion);
  const verbosity = getErrorVerbosity();
  const [{ version, champions }, tierListRes] = await Promise.all([
    fetchChampionMap(),
    fetchBackendJson<TierListResponse>(`${getBackendBaseUrl()}/api/analytics/tierlist?${tierListQuery.toString()}`, {
      next: { revalidate: 60 * 60 }
    })
  ]);

  if (!tierListRes.ok) {
    return (
      <BackendErrorCard
        title="Matchup Analysis"
        message={
          tierListRes.errorKind === "timeout"
            ? "Timed out reaching the backend."
            : tierListRes.errorKind === "unreachable"
              ? "We are having trouble reaching the backend."
              : "Failed to load matchup index data."
        }
        requestId={tierListRes.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify({ status: tierListRes.status, errorKind: tierListRes.errorKind }, null, 2)
            : null
        }
      />
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
    <div className="grid gap-6">
      <header className="grid gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <Badge className="border-primary/40 bg-primary/10 text-primary">
            Matchup Tool
          </Badge>
          <Badge>{popular.length} role pages</Badge>
        </div>
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
          Matchup Analysis
        </h1>
        <p className="text-sm text-fg/75">
          Search champions, filter by role, and jump directly to detailed counter pages.
        </p>
        <div className="flex flex-wrap items-center gap-2">
          <Badge>{activeRegionLabel}</Badge>
          <AnalyticsRegionFilter options={regionOptions} activeRegion={activeRegion} />
        </div>
        <AnalyticsSampleBanner
          sample={(tierListRes.body as { sample?: unknown } | null)?.sample as AnalyticsSampleLike}
        />
      </header>

      <Card className="p-4 md:p-5">
        <MatchupsExplorerClient
          entries={popular}
          champions={champions}
          version={version}
          activeRegion={activeRegion}
        />
      </Card>
    </div>
  );
}
