import type { components } from "@transcendence/api-client";

import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { BackendErrorCard } from "@/components/BackendErrorCard";
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
  const { activeRegion, activeRegionLabel, options: regionOptions } = await resolveAnalyticsRegion(
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
            ? "Timed out reaching the backend."
            : tierListRes.errorKind === "unreachable"
              ? "We are having trouble reaching the backend."
              : "Failed to load champions analytics."
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
    <div className="grid gap-6">
      <header className="grid gap-2">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
          Champions
        </h1>
        <p className="text-sm text-fg/75">
          Builds, matchups, and win rates per role.
        </p>
        <div className="flex flex-wrap items-center gap-2">
          <AnalyticsSampleBanner
            sample={(tierListRes.body as { sample?: unknown } | null)?.sample as AnalyticsSampleLike}
          />
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <span className="rounded-full border border-primary/40 bg-primary/10 px-2.5 py-1 text-xs font-medium text-primary">
            {activeRegionLabel}
          </span>
          <AnalyticsRegionFilter options={regionOptions} activeRegion={activeRegion} />
        </div>
      </header>

      <ChampionsGridClient champions={list} version={version} activeRegion={activeRegion} />
    </div>
  );
}
