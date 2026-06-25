import { BackendErrorCard } from "@/components/BackendErrorCard";
import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { TierListFilterBar } from "@/components/TierListFilterBar";
import { TierListTable } from "@/components/TierListTable";
import { Badge } from "@/components/ui/Badge";
import { Toolbar } from "@/components/ui/Toolbar";
import {
  fetchWithGlobalAnalyticsRegionFallback,
  resolveAnalyticsRegionPresentation
} from "@/lib/analyticsRegionFallback";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { GLOBAL_ANALYTICS_REGION } from "@/lib/analyticsRegionShared";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { fetchLolAnalyticsPatches } from "@/lib/lolAnalyticsPatches";
import { normalizeAnalyticsPatch } from "@/lib/lolPatchFilters";
import {
  DEFAULT_TIERLIST_RANK_TIER,
  normalizeRankTierParam,
  rankTierDisplayLabel
} from "@/lib/ranks";
import { roleDisplayLabel } from "@/lib/roles";
import { fetchChampionMap } from "@/lib/staticData";
import { normalizeTierListEntries, type TierListResponseLike } from "@/lib/tierlist";
import { UpdatedAgo } from "@/components/UpdatedAgo";

type TierListResponse = TierListResponseLike;

export default async function TierListPage({
  searchParams
}: {
  searchParams?: Promise<{ role?: string; rankTier?: string; region?: string; patch?: string }>;
}) {
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const { activeRegion, activeRegionLabel, options: regionOptions } = await resolveAnalyticsRegion(
    resolvedSearchParams?.region
  );
  const roleParam = (resolvedSearchParams?.role ?? "").toUpperCase();
  const rawRankParam = resolvedSearchParams?.rankTier ?? null;
  const normalizedRankParam = normalizeRankTierParam(rawRankParam);
  const selectedPatch = normalizeAnalyticsPatch(resolvedSearchParams?.patch);
  const useDefaultRank = rawRankParam == null || rawRankParam.trim().length === 0;
  const effectiveRankParam = useDefaultRank ? DEFAULT_TIERLIST_RANK_TIER : normalizedRankParam;

  const verbosity = getErrorVerbosity();
  const fetchTierList = async (region: string) => {
    const requestQuery = new URLSearchParams();
    if (roleParam && roleParam !== "ALL") requestQuery.set("role", roleParam);
    if (effectiveRankParam) requestQuery.set("rankTier", effectiveRankParam);
    if (region !== GLOBAL_ANALYTICS_REGION) requestQuery.set("region", region);
    if (selectedPatch) requestQuery.set("patch", selectedPatch);

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
  const patchOptions = await fetchLolAnalyticsPatches();
  const sharedFilterParams = selectedPatch ? { patch: selectedPatch } : {};

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
          <TierListFilterBar
            activeRole={roleParam || "ALL"}
            activeRank={effectiveRankParam ?? "all"}
            regionOptions={regionOptions}
            activeRegion={activeRegion}
            patchOptions={patchOptions}
            activePatch={selectedPatch}
            extraParams={sharedFilterParams}
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
    <div className="grid gap-4">
      <Toolbar
        eyebrow="League Analytics"
        title="Tier List"
        meta={
          <>
            <Badge className="border-primary/40 bg-primary/10 text-primary">
              Patch {tierlist.patch ?? "Unknown"}
            </Badge>
            <span>{effectiveRegionLabel}</span>
            <span aria-hidden="true">·</span>
            <span>{roleDisplayLabel(tierlist.role ?? "ALL")}</span>
            <span aria-hidden="true">·</span>
            <span>{rankTierDisplayLabel(rankTierValue ?? "all")}</span>
            <span aria-hidden="true">·</span>
            <span className="type-tabular tabular-nums">{normalizedEntries.length} champions</span>
            {tierlist.computedAtUtc ? (
              <>
                <span aria-hidden="true">·</span>
                <UpdatedAgo timestamp={tierlist.computedAtUtc} />
              </>
            ) : null}
          </>
        }
        filters={
          <TierListFilterBar
            activeRole={roleParam || "ALL"}
            activeRank={effectiveRankParam ?? "all"}
            regionOptions={regionOptions}
            activeRegion={effectiveRegion}
            patchOptions={patchOptions}
            activePatch={selectedPatch}
            extraParams={sharedFilterParams}
            baseHref="/lol/tierlist"
            className="mt-0 w-full border-0 bg-transparent p-0 shadow-none"
          />
        }
      />

      {fallbackMessage ? <p className="type-ui px-1 text-muted">{fallbackMessage}</p> : null}
      <AnalyticsSampleBanner
        sample={(tierlist as { sample?: unknown } | null)?.sample as AnalyticsSampleLike}
      />

      <TierListTable
        entries={normalizedEntries}
        champions={champions}
        version={version}
        rankTierValue={rankTierValue}
        activeRegion={effectiveRegion}
        activePatch={selectedPatch}
      />
    </div>
  );
}
