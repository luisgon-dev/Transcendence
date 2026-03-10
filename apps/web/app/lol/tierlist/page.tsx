import type { components } from "@transcendence/api-client";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { FilterBar } from "@/components/FilterBar";
import { TierListTable } from "@/components/TierListTable";
import { Badge } from "@/components/ui/Badge";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { type AnalyticsSampleLike } from "@/lib/analyticsSample";
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
  const qs = new URLSearchParams();
  const roleParam = (resolvedSearchParams?.role ?? "").toUpperCase();
  const rawRankParam = resolvedSearchParams?.rankTier ?? null;
  const normalizedRankParam = normalizeRankTierParam(rawRankParam);
  const useDefaultRank = rawRankParam == null || rawRankParam.trim().length === 0;
  const effectiveRankParam = useDefaultRank ? DEFAULT_TIERLIST_RANK_TIER : normalizedRankParam;

  if (roleParam && roleParam !== "ALL") qs.set("role", roleParam);
  if (effectiveRankParam) qs.set("rankTier", effectiveRankParam);
  if (activeRegion !== "ALL") qs.set("region", activeRegion);

  const verbosity = getErrorVerbosity();
  const res = await fetchBackendJson<TierListResponse>(
    `${getBackendBaseUrl()}/api/lol/analytics/tierlist?${qs.toString()}`,
    { next: { revalidate: 60 * 60 } }
  );

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
      />
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
    <div className="grid gap-6">
      <header className="glass-card mesh-highlight flex flex-col gap-3 rounded-3xl p-5 md:p-6">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
          Tier List
        </h1>
        <p className="text-sm text-fg/75">
          See which champions are winning most often for this role, rank, and region.
        </p>

        <div className="flex flex-wrap items-center gap-2">
          <Badge className="border-primary/40 bg-primary/10 text-primary">
            Patch {tierlist.patch ?? "Unknown"}
          </Badge>
          <Badge>{activeRegionLabel}</Badge>
          <Badge>{roleDisplayLabel(tierlist.role ?? "ALL")}</Badge>
          <Badge>{rankTierDisplayLabel(rankTierValue ?? "all")}</Badge>
          <Badge>{normalizedEntries.length} champions</Badge>
        </div>

        <AnalyticsSampleBanner
          sample={(tierlist as { sample?: unknown } | null)?.sample as AnalyticsSampleLike}
        />

        <FilterBar
          activeRole={roleParam || "ALL"}
          activeRank={effectiveRankParam ?? "all"}
          regionOptions={regionOptions}
          activeRegion={activeRegion}
          baseHref="/lol/tierlist"
          patch={tierlist.patch}
        />
      </header>

      <TierListTable
        entries={normalizedEntries}
        champions={champions}
        version={version}
        rankTierValue={rankTierValue}
        activeRegion={activeRegion}
      />
    </div>
  );
}
