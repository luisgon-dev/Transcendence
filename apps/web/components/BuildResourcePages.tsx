import { notFound } from "next/navigation";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { BuildResourceDetail } from "@/components/BuildResourceDetail";
import { BuildResourceIndex } from "@/components/BuildResourceIndex";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { fetchBackendJson } from "@/lib/backendCall";
import {
  normalizeBuildResourceSort,
  type BuildResourceDetailResponse,
  type BuildResourceIndexResponse,
  type BuildResourceKind
} from "@/lib/buildResources";
import { getBackendBaseUrl } from "@/lib/env";
import { fetchChampionMap, fetchItemMap, fetchRunesReforged } from "@/lib/staticData";

type BuildResourceSearchParams = { region?: string; q?: string; sort?: string };

export async function BuildResourceIndexPage({
  kind,
  searchParams
}: {
  kind: BuildResourceKind;
  searchParams?: Promise<BuildResourceSearchParams>;
}) {
  const params = searchParams ? await searchParams : undefined;
  const region = await resolveAnalyticsRegion(params?.region);
  const query = new URLSearchParams();
  if (region.activeRegion !== "ALL") query.set("region", region.activeRegion);
  const endpoint = `${getBackendBaseUrl()}/api/lol/analytics/${kind}${query.size ? `?${query}` : ""}`;
  const [analytics, champions, itemMap, runeData] = await Promise.all([
    fetchBackendJson<BuildResourceIndexResponse>(endpoint, { next: { revalidate: 60 * 60 } }),
    fetchChampionMap().catch(() => null),
    kind === "items" ? fetchItemMap().catch(() => null) : Promise.resolve(null),
    kind === "runes" ? fetchRunesReforged().catch(() => null) : Promise.resolve(null)
  ]);

  return (
    <BuildResourceIndex
      kind={kind}
      response={analytics.ok ? analytics.body : null}
      champions={champions}
      itemMap={itemMap}
      runeData={runeData}
      regionOptions={region.options}
      activeRegion={region.activeRegion}
      activeRegionLabel={region.activeRegionLabel}
      query={params?.q}
      sort={normalizeBuildResourceSort(params?.sort)}
    />
  );
}

export async function BuildResourceDetailPage({
  kind,
  resourceId,
  searchParams
}: {
  kind: BuildResourceKind;
  resourceId: number;
  searchParams?: Promise<{ region?: string }>;
}) {
  if (!Number.isInteger(resourceId) || resourceId <= 0) notFound();

  const params = searchParams ? await searchParams : undefined;
  const region = await resolveAnalyticsRegion(params?.region);
  const query = new URLSearchParams();
  if (region.activeRegion !== "ALL") query.set("region", region.activeRegion);
  const endpoint = `${getBackendBaseUrl()}/api/lol/analytics/${kind}/${resourceId}${query.size ? `?${query}` : ""}`;
  const [analytics, champions, itemMap, runeData] = await Promise.all([
    fetchBackendJson<BuildResourceDetailResponse>(endpoint, { next: { revalidate: 60 * 60 } }),
    fetchChampionMap().catch(() => null),
    kind === "items" ? fetchItemMap().catch(() => null) : Promise.resolve(null),
    kind === "runes" ? fetchRunesReforged().catch(() => null) : Promise.resolve(null)
  ]);

  if (analytics.status === 404) notFound();
  if (!analytics.ok || !analytics.body) {
    return (
      <BackendErrorCard
        title={`${kind === "items" ? "Item" : "Rune"} analytics`}
        message="This analytics detail could not be loaded right now."
        hint="The current patch may still be processing, or the analytics service may be temporarily unavailable."
        requestId={analytics.requestId}
      />
    );
  }

  return (
    <BuildResourceDetail
      kind={kind}
      response={analytics.body}
      champions={champions}
      itemMap={itemMap}
      runeData={runeData}
      activeRegionLabel={region.activeRegionLabel}
    />
  );
}
