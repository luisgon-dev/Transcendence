import type { components } from "@transcendence/api-client";

import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { BackendErrorCard } from "@/components/BackendErrorCard";
import { Toolbar } from "@/components/ui/Toolbar";
import {
  ChampionsGridClient,
  type ChampionGridEntry
} from "@/components/ChampionsGridClient";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { DEFAULT_TIERLIST_RANK_TIER } from "@/lib/ranks";
import { fetchChampionMap } from "@/lib/staticData";
import {
  normalizeTierListEntries,
  type UITierGrade
} from "@/lib/tierlist";

type TierListResponse = components["schemas"]["TierListResponse"];

export default async function ChampionsPage({
  searchParams
}: {
  searchParams?: Promise<{ region?: string }>;
}) {
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const { activeRegion, options: regionOptions } = await resolveAnalyticsRegion(
    resolvedSearchParams?.region
  );
  const verbosity = getErrorVerbosity();
  const tierListQuery = new URLSearchParams({
    rankTier: DEFAULT_TIERLIST_RANK_TIER
  });
  if (activeRegion !== "ALL") tierListQuery.set("region", activeRegion);
  const [{ version, champions }, tierListRes] = await Promise.all([
    fetchChampionMap(),
    fetchBackendJson<TierListResponse>(
      `${getBackendBaseUrl()}/api/lol/analytics/tierlist?${tierListQuery.toString()}`,
      { next: { revalidate: 60 * 60 } }
    )
  ]);

  if (!tierListRes.ok) {
    return (
      <BackendErrorCard
        title="Champions"
        message={
          tierListRes.errorKind === "timeout"
            ? "This page is taking too long to load."
            : tierListRes.errorKind === "unreachable"
              ? "We couldn't load champion data right now."
              : "We couldn't load champion stats."
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

  // Build a map of championId -> best tier/role/winRate from the tier list
  const tierMap = new Map<
    number,
    { tier: UITierGrade; role: string; winRate: number; games: number }
  >();

  if (tierListRes.ok && tierListRes.body) {
    const entries = normalizeTierListEntries(tierListRes.body.entries);
    for (const entry of entries) {
      const existing = tierMap.get(entry.championId);
      // Keep the entry with the most games (most relevant role)
      if (!existing || entry.games > existing.games) {
        tierMap.set(entry.championId, {
          tier: entry.tier,
          role: entry.role,
          winRate: entry.winRate,
          games: entry.games
        });
      }
    }
  }

  const list: ChampionGridEntry[] = Object.entries(champions)
    .map(([key, value]) => {
      const id = Number(key);
      const tierInfo = tierMap.get(id);
      return {
        championId: id,
        ...value,
        tier: tierInfo?.tier ?? null,
        winRate: tierInfo?.winRate ?? null,
        primaryRole: tierInfo?.role ?? null
      };
    })
    .sort((a, b) => a.name.localeCompare(b.name));

  return (
    <div className="grid gap-4">
      <Toolbar
        eyebrow="League Directory"
        title="Champions"
        meta={<span>Builds, win rates, and matchups for every champion</span>}
        filters={<AnalyticsRegionFilter options={regionOptions} activeRegion={activeRegion} />}
      />
      <AnalyticsSampleBanner
        sample={(tierListRes.body as { sample?: unknown } | null)?.sample as AnalyticsSampleLike}
      />
      <ChampionsGridClient champions={list} version={version} activeRegion={activeRegion} />
    </div>
  );
}
