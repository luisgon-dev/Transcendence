import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client";

import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { BackendErrorCard } from "@/components/BackendErrorCard";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { formatDateTimeMs, formatRelativeTime } from "@/lib/format";
import { championIconUrl, fetchChampionMap, fetchItemMap, itemIconUrl } from "@/lib/staticData";
import { normalizeTierListEntries } from "@/lib/tierlist";

type TierListResponse = components["schemas"]["TierListResponse"];
type ChampionProBuildsResponse = components["schemas"]["ChampionProBuildsResponse"];
type ProMatchBuildDto = components["schemas"]["ProMatchBuildDto"];

type ChampionLookup = {
  championId: number;
  slug: string;
  name: string;
};

type ProFeedRow = {
  championId: number;
  match: ProMatchBuildDto;
  patch: string | null | undefined;
};

const MAX_SEARCH_RESULTS = 12;
const MAX_FEED_CHAMPIONS_DEFAULT = 8;
const MAX_FEED_CHAMPIONS_SEARCH = 6;
const MAX_MATCHES_PER_CHAMPION = 8;
const MAX_FEED_ROWS = 48;

function normalizeChampionQuery(query: string | undefined): string | null {
  if (!query) return null;
  const trimmed = query.trim();
  return trimmed.length > 0 ? trimmed : null;
}

function uniqueChampionIdsByGames(entries: ReturnType<typeof normalizeTierListEntries>): number[] {
  return entries
    .slice()
    .sort((a, b) => b.games - a.games)
    .map((entry) => entry.championId)
    .filter((championId, idx, rows) => rows.indexOf(championId) === idx);
}

function championMatchesQuery(champion: ChampionLookup, queryLower: string) {
  return (
    champion.name.toLowerCase().includes(queryLower) || String(champion.championId) === queryLower
  );
}

function compareChampionQueryRelevance(
  a: ChampionLookup,
  b: ChampionLookup,
  queryLower: string
) {
  const aName = a.name.toLowerCase();
  const bName = b.name.toLowerCase();
  const aStarts = aName.startsWith(queryLower) ? 0 : 1;
  const bStarts = bName.startsWith(queryLower) ? 0 : 1;
  if (aStarts !== bStarts) return aStarts - bStarts;
  if (aName.length !== bName.length) return aName.length - bName.length;
  return aName.localeCompare(bName);
}

export default async function ProBuildsIndexPage({
  searchParams
}: {
  searchParams?: Promise<{ q?: string; region?: string }>;
}) {
  const verbosity = getErrorVerbosity();
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const { activeRegion, activeRegionLabel, options: regionOptions } = await resolveAnalyticsRegion(
    resolvedSearchParams?.region
  );
  const championQuery = normalizeChampionQuery(resolvedSearchParams?.q);
  const tierListQuery = new URLSearchParams();
  if (activeRegion !== "ALL") tierListQuery.set("region", activeRegion);

  const [{ version, champions }, itemStatic, tierListRes] = await Promise.all([
    fetchChampionMap(),
    fetchItemMap(),
    fetchBackendJson<TierListResponse>(`${getBackendBaseUrl()}/api/lol/analytics/tierlist?${tierListQuery.toString()}`, {
      next: { revalidate: 60 * 60 }
    })
  ]);

  const championCatalog = Object.entries(champions)
    .map(([championId, champion]) => ({
      championId: Number(championId),
      slug: champion.id,
      name: champion.name
    }))
    .filter((row) => Number.isFinite(row.championId) && row.championId > 0)
    .sort((a, b) => a.name.localeCompare(b.name));

  const championById = new Map<number, ChampionLookup>(
    championCatalog.map((champion) => [champion.championId, champion])
  );

  const tierEntries = tierListRes.ok ? normalizeTierListEntries(tierListRes.body?.entries ?? []) : [];
  const featuredChampionIds =
    tierEntries.length > 0
      ? uniqueChampionIdsByGames(tierEntries).slice(0, MAX_SEARCH_RESULTS)
      : championCatalog.slice(0, MAX_SEARCH_RESULTS).map((champion) => champion.championId);

  const queryLower = championQuery?.toLowerCase() ?? null;
  const matchingChampions = queryLower
    ? championCatalog
        .filter((champion) => championMatchesQuery(champion, queryLower))
        .sort((a, b) => compareChampionQueryRelevance(a, b, queryLower))
    : [];

  const championsToShow = (championQuery
    ? matchingChampions.slice(0, MAX_SEARCH_RESULTS)
    : featuredChampionIds.map((championId) => championById.get(championId)).filter(Boolean)) as ChampionLookup[];

  const feedChampionIds = (
    championQuery
      ? matchingChampions.slice(0, MAX_FEED_CHAMPIONS_SEARCH).map((champion) => champion.championId)
      : featuredChampionIds.slice(0, MAX_FEED_CHAMPIONS_DEFAULT)
  ).filter((championId, idx, rows) => rows.indexOf(championId) === idx);

  const proResponses = await Promise.all(
    feedChampionIds.map(async (championId) => ({
      championId,
      response: await fetchBackendJson<ChampionProBuildsResponse>(
        `${getBackendBaseUrl()}/api/lol/analytics/champions/${championId}/pro-builds?region=${encodeURIComponent(activeRegion)}&role=ALL`,
        { next: { revalidate: 60 * 30 } }
      )
    }))
  );

  const successfulFeeds = proResponses.filter((row) => row.response.ok && row.response.body);
  const proFeedRows: ProFeedRow[] = [];

  for (const row of successfulFeeds) {
    const body = row.response.body;
    const recentMatches = body?.recentProMatches ?? [];
    for (const match of recentMatches.slice(0, MAX_MATCHES_PER_CHAMPION)) {
      proFeedRows.push({
        championId: row.championId,
        match,
        patch: body?.patch
      });
    }
  }

  const dedupe = new Set<string>();
  const recentMatchesFeed = proFeedRows
    .sort((a, b) => (b.match.playedAt ?? 0) - (a.match.playedAt ?? 0))
    .filter((entry) => {
      const key = `${entry.match.matchId ?? "unknown"}:${entry.championId}`;
      if (dedupe.has(key)) return false;
      dedupe.add(key);
      return true;
    })
    .slice(0, MAX_FEED_ROWS);

  const failedFeedCount = proResponses.length - successfulFeeds.length;

  if (!tierListRes.ok) {
    return (
      <BackendErrorCard
        title="Pro Builds"
        message={
          tierListRes.errorKind === "timeout"
            ? "This page is taking too long to load."
            : tierListRes.errorKind === "unreachable"
              ? "We couldn't load pro-build data right now."
              : "We couldn't load pro-build data."
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

  return (
    <div className="grid gap-8">
      <header className="page-hero p-5 md:p-8">
        <p className="type-kicker text-muted">Tracked Matches</p>
        <h1 className="type-page-title mt-3">
          Pro Builds
        </h1>
        <p className="type-ui mt-3 text-fg/75">
          Recent builds from tracked pro and high-MMR matches, with quick champion search.
        </p>
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <Badge className="border-primary/45 bg-primary/10 text-primary">
            {recentMatchesFeed.length} matches loaded
          </Badge>
          <Badge>{feedChampionIds.length} champions featured</Badge>
          <Badge>{activeRegionLabel}</Badge>
          {championQuery ? <Badge>Search: {championQuery}</Badge> : null}
        </div>
        <div className="mt-3">
          <AnalyticsRegionFilter options={regionOptions} activeRegion={activeRegion} />
        </div>
        <div className="mt-3">
          <AnalyticsSampleBanner
            sample={(tierListRes.body as { sample?: unknown } | null)?.sample as AnalyticsSampleLike}
          />
        </div>
      </header>

      <Card className="page-panel p-5">
        <h2 className="type-section">Search Champions</h2>
        <form action="/lol/pro-builds" method="get" className="mt-3 flex flex-wrap items-center gap-2">
          {activeRegion !== "ALL" ? <input type="hidden" name="region" value={activeRegion} /> : null}
          <input
            type="text"
            name="q"
            defaultValue={championQuery ?? ""}
            placeholder="Search champion name or id (e.g., Ahri or 103)"
            className="control-input min-w-[220px] flex-1"
          />
          <button
            type="submit"
            className="type-ui h-11 rounded-control border border-primary/40 bg-primary/12 px-4 font-semibold text-primary transition hover:bg-primary/20"
          >
            Search
          </button>
          {championQuery ? (
            <Link
              href={`/lol/pro-builds${activeRegion !== "ALL" ? `?region=${encodeURIComponent(activeRegion)}` : ""}`}
              className="control-tab type-ui h-11 px-4"
            >
              Clear
            </Link>
          ) : null}
        </form>

        <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          {championsToShow.length === 0 ? (
            <p className="col-span-full text-sm text-muted">
              No champions matched your search.
            </p>
          ) : (
            championsToShow.map((champion) => (
              <Link
                key={champion.championId}
                href={`/lol/pro-builds/${champion.championId}${activeRegion !== "ALL" ? `?region=${encodeURIComponent(activeRegion)}` : ""}`}
                className="surface-subtle rounded-card p-3 transition hover:bg-surface-2/72"
              >
                <div className="flex items-center gap-2.5">
                  <Image
                    src={championIconUrl(version, champion.slug)}
                    alt={champion.name}
                    width={34}
                    height={34}
                    className="rounded-md"
                  />
                  <p className="truncate text-sm font-medium text-fg">{champion.name}</p>
                </div>
              </Link>
            ))
          )}
        </div>
      </Card>

      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border/40 px-4 py-3">
          <div>
            <h2 className="type-section">Recent Pro Matches</h2>
            <p className="type-ui mt-1 text-muted">
              Click any row to open champion-specific pro builds and recent match details.
            </p>
          </div>
          {failedFeedCount > 0 ? (
            <p className="text-xs text-muted">
              {failedFeedCount} champion feed{failedFeedCount === 1 ? "" : "s"} unavailable right now.
            </p>
          ) : null}
        </div>

        {recentMatchesFeed.length === 0 ? (
          <p className="px-4 py-4 text-sm text-muted">
            No pro matches are available for the current selection.
          </p>
        ) : (
          <ul className="grid gap-0">
            {recentMatchesFeed.map((entry, idx) => {
              const champion = championById.get(entry.championId);
              const championSlug = champion?.slug ?? "Unknown";
              const championName = champion?.name ?? `Champion ${entry.championId}`;
              const playedAt = entry.match.playedAt ?? 0;
              const hasTimestamp = Number.isFinite(playedAt) && playedAt > 0;
              const items = (entry.match.items ?? [])
                .filter((itemId) => Number.isFinite(itemId) && itemId > 0)
                .slice(0, 6);
              return (
                <li key={`${entry.match.matchId ?? "match"}-${entry.championId}-${idx}`}>
                  <Link
                    href={`/lol/pro-builds/${entry.championId}`}
                    className="block border-b border-border/20 px-4 py-3 transition hover:bg-white/[0.04]"
                  >
                    <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_auto] lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)_minmax(0,1fr)]">
                      <div className="min-w-0">
                        <div className="flex items-center gap-3">
                          <Image
                            src={championIconUrl(version, championSlug)}
                            alt={championName}
                            width={34}
                            height={34}
                            className="rounded-md border border-border/60"
                          />
                          <div className="min-w-0 flex-1">
                            <div className="flex items-center gap-2">
                              <p className="truncate text-sm font-medium text-fg">{championName}</p>
                              <span className={`shrink-0 text-xs font-semibold sm:hidden ${entry.match.win ? "text-wr-high" : "text-wr-low"}`}>
                                {entry.match.win ? "Win" : "Loss"}
                              </span>
                            </div>
                            <p className="truncate text-xs text-muted">
                              {entry.match.playerName ?? "Unknown player"}
                              {entry.match.teamName ? ` (${entry.match.teamName})` : ""}
                              {hasTimestamp ? ` · ${formatRelativeTime(playedAt)}` : ""}
                            </p>
                          </div>
                        </div>

                        <div className="mt-2 flex flex-wrap items-center gap-1.5">
                          {items.map((itemId, itemIdx) => {
                            const itemMeta = itemStatic.items[String(itemId)];
                            return (
                              <Image
                                key={`${itemId}-${itemIdx}`}
                                src={itemIconUrl(itemStatic.version, itemId)}
                                alt={itemMeta?.name ?? `Item ${itemId}`}
                                title={itemMeta?.name ?? `Item ${itemId}`}
                                width={24}
                                height={24}
                                className="rounded-md border border-border/40"
                              />
                            );
                          })}
                        </div>
                      </div>

                      <div className="hidden sm:block">
                        <p className={`text-sm font-semibold ${entry.match.win ? "text-wr-high" : "text-wr-low"}`}>
                          {entry.match.win ? "Win" : "Loss"}
                        </p>
                        <p className="text-xs text-muted">{entry.match.matchId ?? "Unknown match id"}</p>
                        <p className="mt-1 text-xs text-muted">Patch {entry.patch ?? "Unknown"}</p>
                      </div>

                      <div className="hidden text-left lg:block lg:text-right">
                        <p className="text-xs text-muted">
                          {hasTimestamp ? formatRelativeTime(playedAt) : "Time unavailable"}
                        </p>
                        <p className="text-xs text-muted">
                          {hasTimestamp ? formatDateTimeMs(playedAt) : "-"}
                        </p>
                      </div>
                    </div>
                  </Link>
                </li>
              );
            })}
          </ul>
        )}
      </Card>
    </div>
  );
}
