import type { components } from "@transcendence/api-client/schema";

export type BuildResourceKind = "items" | "runes";
export type BuildResourceSort = "popular" | "winrate" | "name";

export type BuildResourceChampionStat = components["schemas"]["BuildResourceChampionStatDto"];
export type BuildResourceEntry = components["schemas"]["BuildResourceAnalyticsEntryDto"];
export type BuildResourceIndexResponse = components["schemas"]["BuildResourceAnalyticsIndexResponse"];
export type BuildResourceDetailResponse = components["schemas"]["BuildResourceAnalyticsDetailResponse"];

export function normalizeBuildResourceSort(value: string | undefined): BuildResourceSort {
  return value === "winrate" || value === "name" ? value : "popular";
}

export function filterAndSortBuildResources(
  entries: BuildResourceEntry[],
  query: string | undefined,
  sort: BuildResourceSort
) {
  const normalizedQuery = query?.trim().toLocaleLowerCase() ?? "";
  const filtered = normalizedQuery
    ? entries.filter((entry) =>
        `${entry.name} ${entry.resourceId}`.toLocaleLowerCase().includes(normalizedQuery)
      )
    : entries.slice();

  return filtered.sort((a, b) => {
    if (sort === "name") return a.name.localeCompare(b.name);
    if (sort === "winrate") return b.winRate - a.winRate || b.games - a.games;
    return b.games - a.games || b.winRate - a.winRate;
  });
}

export function buildResourceHref(
  kind: BuildResourceKind,
  resourceId: number | null,
  region?: string | null
) {
  const base = resourceId == null ? `/lol/${kind}` : `/lol/${kind}/${resourceId}`;
  return region && region !== "ALL" ? `${base}?region=${encodeURIComponent(region)}` : base;
}
