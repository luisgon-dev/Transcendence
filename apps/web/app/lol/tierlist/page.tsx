import type { components } from "@transcendence/api-client";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { FilterBar } from "@/components/FilterBar";
import { TierListTable } from "@/components/TierListTable";
import { Badge } from "@/components/ui/Badge";
import {
  fetchWithGlobalAnalyticsRegionFallback,
  resolveAnalyticsRegionPresentation
} from "@/lib/analyticsRegionFallback";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { GLOBAL_ANALYTICS_REGION } from "@/lib/analyticsRegionShared";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import {
  DEFAULT_TIERLIST_RANK_TIER,
  normalizeRankTierParam,
  rankTierDisplayLabel
} from "@/lib/ranks";
import { roleDisplayLabel } from "@/lib/roles";
import { fetchChampionMap } from "@/lib/staticData";
import { normalizeTierListEntries } from "@/lib/tierlist";

type TierListResponse = components["schemas"]["TierListResponse"];

export default async function TierListPage({
  searchParams
}: {
  searchParams?: Promise<{ role?: string; rankTier?: string; region?: string }>;
}) {
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const { activeRegion, activeRegionLabel, options: regionOptions } = await resolveAnalyticsRegion(
    resolvedSearchParams?.region
  );
  const roleParam = (resolvedSearchParams?.role ?? "").toUpperCase();
  const rawRankParam = resolvedSearchParams?.rankTier ?? null;
  const normalizedRankParam = normalizeRankTierParam(rawRankParam);
  const useDefaultRank = rawRankParam == null || rawRankParam.trim().length === 0;
  const effectiveRankParam = useDefaultRank ? DEFAULT_TIERLIST_RANK_TIER : normalizedRankParam;

  const verbosity = getErrorVerbosity();
  const fetchTierList = async (region: string) => {
    const requestQuery = new URLSearchParams();
    if (roleParam && roleParam !== "ALL") requestQuery.set("role", roleParam);
    if (effectiveRankParam) requestQuery.set("rankTier", effectiveRankParam);
    if (region !== GLOBAL_ANALYTICS_REGION) requestQuery.set("region", region);

    return fetchBackendJson<TierListResponse>(
      `${getBackendBaseUrl()}/api/lol/analytics/tierlist?${requestQuery.toString()}`,
      { next: { revalidate: 60 * 60 } }
    );
  };
  const { result: res, usedGlobalFallback } = await fetchWithGlobalAnalyticsRegionFallback(
    activeRegion,
    fetchTierList
  );
  const {
    effectiveRegion,
    effectiveRegionLabel,
    fallbackMessage
  } = resolveAnalyticsRegionPresentation(activeRegion, activeRegionLabel, regionOptions, usedGlobalFallback);

  if (!res.ok) {
    return (
      <BackendErrorCard
        title="Tier List"
        message={
          res.errorKind === "timeout"
            ? "This page is taking too long to load."
            : res.errorKind === "unreachable"
              ? "We couldn't load the tier list right now."
              : "We couldn't load the tier list."
        }
        requestId={res.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify({ status: res.status, errorKind: res.errorKind }, null, 2)
            : null
        }
      >
        <div className="grid gap-4">
          <p className="type-ui text-fg/70">
            Try switching back to Global or another region.
          </p>
          <FilterBar
            activeRole={roleParam || "ALL"}
            activeRank={effectiveRankParam ?? "all"}
            regionOptions={regionOptions}
            activeRegion={activeRegion}
            baseHref="/lol/tierlist"
            className="mt-0"
          />
        </div>
      </BackendErrorCard>
    );
  }

  const tierlist = res.body!;
  const { version, champions } = await fetchChampionMap();
  const normalizedEntries = normalizeTierListEntries(tierlist.entries);

  const rankTierValue =
    typeof tierlist.rankTier === "string" && tierlist.rankTier.toLowerCase() !== "all"
      ? tierlist.rankTier
      : null;

  return (
    <div className="grid gap-8">
      <header className="page-hero p-5 md:p-8">
        <p className="type-kicker text-muted">League Analytics</p>
        <h1 className="type-page-title mt-3">
          Tier List
        </h1>
        <p className="type-ui mt-3 text-fg/75">
          See which champions are winning most often for this role, rank, and region.
        </p>

        <div className="mt-3 flex flex-wrap items-center gap-2">
          <Badge className="border-primary/40 bg-primary/10 text-primary">
            Patch {tierlist.patch ?? "Unknown"}
          </Badge>
          <Badge>{effectiveRegionLabel}</Badge>
          <Badge>{roleDisplayLabel(tierlist.role ?? "ALL")}</Badge>
          <Badge>{rankTierDisplayLabel(rankTierValue ?? "all")}</Badge>
          <Badge>{normalizedEntries.length} champions</Badge>
        </div>
        {fallbackMessage ? (
          <p className="type-ui mt-3 text-fg/68">{fallbackMessage}</p>
        ) : null}

        <div className="mt-3">
          <AnalyticsSampleBanner
            sample={(tierlist as { sample?: unknown } | null)?.sample as AnalyticsSampleLike}
          />
        </div>

        <FilterBar
          activeRole={roleParam || "ALL"}
          activeRank={effectiveRankParam ?? "all"}
          regionOptions={regionOptions}
          activeRegion={effectiveRegion}
          baseHref="/lol/tierlist"
          className="mt-4"
        />
      </header>

      <TierListTable
        entries={normalizedEntries}
        champions={champions}
        version={version}
        rankTierValue={rankTierValue}
        activeRegion={effectiveRegion}
      />
    </div>
  );
}
