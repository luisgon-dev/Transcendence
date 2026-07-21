import type { Metadata } from "next";

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
import { normalizeAnalyticsSample, type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { analyticsQueueOption, normalizeAnalyticsQueue } from "@/lib/analyticsQueues";
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
import { socialImageUrl } from "@/lib/seo";
import { fetchChampionMap } from "@/lib/staticData";
import { normalizeTierListEntries, type TierListResponseLike } from "@/lib/tierlist";
import { UpdatedAgo } from "@/components/UpdatedAgo";

type TierListResponse = TierListResponseLike;

type TierListSearchParams = { role?: string; rankTier?: string; region?: string; patch?: string; queue?: string };

export async function generateMetadata({
  searchParams
}: {
  searchParams?: Promise<TierListSearchParams>;
}): Promise<Metadata> {
  const resolved = searchParams ? await searchParams : {};
  const role = (resolved.role ?? "ALL").toUpperCase();
  const queue = analyticsQueueOption(resolved.queue);
  const rank = rankTierDisplayLabel(normalizeRankTierParam(resolved.rankTier) ?? DEFAULT_TIERLIST_RANK_TIER);
  let patch = normalizeAnalyticsPatch(resolved.patch);
  if (!patch) {
    const patches = await fetchLolAnalyticsPatches(queue.value);
    patch = patches.find((candidate) => candidate.isActive)?.patch ?? patches[0]?.patch ?? null;
  }

  const roleLabel = roleDisplayLabel(role);
  const patchLabel = patch ? ` Patch ${patch}` : "";
  const title = `League of Legends ${queue.label} ${queue.hasRoles ? `${roleLabel} ` : ""}Tier List${patchLabel}`;
  const description = `${rank} ${queue.label} champion rankings${queue.hasRoles ? ` for ${roleLabel.toLowerCase()}` : ""}, with win rates, pick rates, sample confidence, and current-patch data.`;
  const image = socialImageUrl(title, "League tier list", `${rank}, trustworthy samples, and role-specific rankings`);

  return {
    title,
    description,
    alternates: { canonical: "/lol/tierlist" },
    openGraph: {
      type: "website",
      title,
      description,
      url: "/lol/tierlist",
      images: [{ url: image, width: 1200, height: 630, alt: title }]
    },
    twitter: { card: "summary_large_image", title, description, images: [image] }
  };
}

export default async function TierListPage({
  searchParams
}: {
  searchParams?: Promise<TierListSearchParams>;
}) {
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const activeQueue = normalizeAnalyticsQueue(resolvedSearchParams?.queue);
  const queueOption = analyticsQueueOption(activeQueue);
  const { activeRegion, activeRegionLabel, options: regionOptions } = await resolveAnalyticsRegion(
    resolvedSearchParams?.region
  );
  const roleParam = queueOption.hasRoles ? (resolvedSearchParams?.role ?? "").toUpperCase() : "ALL";
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
    if (activeQueue !== "solo") requestQuery.set("queue", activeQueue);

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
  const patchOptions = await fetchLolAnalyticsPatches(activeQueue);
  const sharedFilterParams = {
    ...(selectedPatch ? { patch: selectedPatch } : {}),
    ...(activeQueue !== "solo" ? { queue: activeQueue } : {})
  };

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
            activeQueue={activeQueue}
            showRoles={queueOption.hasRoles}
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

  const sample = (tierlist as { sample?: unknown } | null)?.sample as AnalyticsSampleLike;
  // The per-row "Stable" threshold for the confidence layer: the patch sample's
  // recommended games floor. Undefined when no sample (confidence falls back to its default).
  const minGames = normalizeAnalyticsSample(sample)?.minimumRecommendedSampleSize;

  // roleBaseline (the role-average win rate) rides the wire on each TierListEntry but is
  // dropped by normalizeTierListEntries; re-attach it by (champion, role) so the EB trust
  // hover can name the role average without re-fetching.
  const roleBaselineByKey = new Map<string, number>();
  for (const raw of tierlist.entries ?? []) {
    if (typeof raw.championId !== "number" || typeof raw.roleBaseline !== "number") continue;
    const role = typeof raw.role === "string" && raw.role ? raw.role.toUpperCase() : "ALL";
    roleBaselineByKey.set(`${raw.championId}-${role}`, raw.roleBaseline);
  }
  const entriesWithBaseline = normalizedEntries.map((entry) => ({
    ...entry,
    roleBaseline: roleBaselineByKey.get(`${entry.championId}-${entry.role.toUpperCase()}`) ?? 0
  }));

  return (
    <div className="grid gap-4">
      {/* Sticky so the filters (role / rank / region / patch) stay reachable deep in the ~170-row
          board without scrolling back to the top. Docks at top-24 below the sticky site header;
          z-30 keeps it under the header (z-40) and above the table. Freshness stays legible in the
          meta row while docked, so the separate freshness strip below no longer needs to stick. */}
      <Toolbar
        className="tierlist-toolbar-dock z-30"
        eyebrow="League Analytics"
        title="Tier List"
        meta={
          <>
            <Badge className="border-primary/40 bg-primary/10 text-primary">
              Patch {tierlist.patch ?? "Unknown"}
            </Badge>
            <span>{queueOption.label}</span>
            <span aria-hidden="true">·</span>
            <span>{effectiveRegionLabel}</span>
            <span aria-hidden="true">·</span>
            {queueOption.hasRoles ? <span>{roleDisplayLabel(tierlist.role ?? "ALL")}</span> : null}
            {queueOption.hasRoles ? <span aria-hidden="true">·</span> : null}
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
            activeQueue={activeQueue}
            showRoles={queueOption.hasRoles}
            extraParams={sharedFilterParams}
            baseHref="/lol/tierlist"
            className="mt-0 w-full border-0 bg-transparent p-0 shadow-none"
          />
        }
      />

      {fallbackMessage ? <p className="type-ui px-1 text-muted">{fallbackMessage}</p> : null}

      {/* Sample-size readout. No longer sticky — the sticky filter Toolbar above owns the docked
          region now, and its meta row already surfaces the "updated N ago" freshness. */}
      <AnalyticsSampleBanner
        sample={sample}
        variant="strip"
        className="rounded-lg border border-border bg-surface/92 px-3 py-2 shadow-soft"
      />

      <TierListTable
        entries={entriesWithBaseline}
        champions={champions}
        version={version}
        rankTierValue={rankTierValue}
        activeRegion={effectiveRegion}
        activePatch={selectedPatch}
        activeQueue={activeQueue}
        showRoles={queueOption.hasRoles}
        minGames={minGames}
      />
    </div>
  );
}
