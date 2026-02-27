import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client/schema";

import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
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
  searchParams?: Promise<{ q?: string }>;
}) {
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const championQuery = normalizeChampionQuery(resolvedSearchParams?.q);

  const [{ version, champions }, itemStatic, tierListRes] = await Promise.all([
    fetchChampionMap(),
    fetchItemMap(),
    fetchBackendJson<TierListResponse>(`${getBackendBaseUrl()}/api/analytics/tierlist`, {
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
        `${getBackendBaseUrl()}/api/analytics/champions/${championId}/pro-builds?region=ALL&role=ALL`,
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

  return (
    <div className="grid gap-6">
      <header className="grid gap-2">
        <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
          Pro Builds
        </h1>
        <p className="text-sm text-fg/75">
          Live feed of recent matches from tracked pro and high-ELO players, with quick champion search.
        </p>
        <div className="flex flex-wrap items-center gap-2">
          <Badge className="border-primary/45 bg-primary/10 text-primary">
            {recentMatchesFeed.length} matches loaded
          </Badge>
          <Badge>{feedChampionIds.length} champions sampled</Badge>
          {championQuery ? <Badge>Search: {championQuery}</Badge> : null}
        </div>
      </header>

      <Card className="p-5">
        <h2 className="font-[var(--font-sora)] text-lg font-semibold">Search Champions</h2>
        <form action="/pro-builds" method="get" className="mt-3 flex flex-wrap items-center gap-2">
          <input
            type="text"
            name="q"
            defaultValue={championQuery ?? ""}
            placeholder="Search champion name or id (e.g., Ahri or 103)"
            className="h-11 min-w-[220px] flex-1 rounded-xl border border-border/80 bg-surface/50 px-3 text-sm text-fg shadow-glass outline-none placeholder:text-muted/80 focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
          />
          <button
            type="submit"
            className="h-11 rounded-xl border border-primary/40 bg-primary/12 px-4 text-sm font-medium text-primary transition hover:bg-primary/20"
          >
            Search
          </button>
          {championQuery ? (
            <Link
              href="/pro-builds"
              className="h-11 rounded-xl border border-border/70 bg-white/[0.03] px-4 text-sm leading-[44px] text-fg/85 transition hover:bg-white/[0.08]"
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
                href={`/pro-builds/${champion.championId}`}
                className="rounded-lg border border-border/60 bg-white/[0.03] p-3 transition hover:bg-white/[0.08]"
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
            <h2 className="font-[var(--font-sora)] text-lg font-semibold">Recent Pro Matches</h2>
            <p className="text-xs text-muted">
              Combined feed from champion pro endpoints. Click any row to open champion-specific pro analytics.
            </p>
          </div>
          {failedFeedCount > 0 ? (
            <p className="text-xs text-muted">
              {failedFeedCount} champion feed{failedFeedCount === 1 ? "" : "s"} unavailable.
            </p>
          ) : null}
        </div>

        {recentMatchesFeed.length === 0 ? (
          <p className="px-4 py-4 text-sm text-muted">
            No pro matches available for the current selection.
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
                    href={`/pro-builds/${entry.championId}`}
                    className="block border-b border-border/20 px-4 py-3 transition hover:bg-white/[0.04]"
                  >
                    <div className="grid gap-3 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)_minmax(0,1fr)]">
                      <div className="min-w-0">
                        <div className="flex items-center gap-3">
                          <Image
                            src={championIconUrl(version, championSlug)}
                            alt={championName}
                            width={34}
                            height={34}
                            className="rounded-md border border-border/60"
                          />
                          <div className="min-w-0">
                            <p className="truncate text-sm font-medium text-fg">{championName}</p>
                            <p className="truncate text-xs text-muted">
                              {entry.match.playerName ?? "Unknown player"}
                              {entry.match.teamName ? ` (${entry.match.teamName})` : ""}
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

                      <div>
                        <p className={`text-sm font-semibold ${entry.match.win ? "text-wr-high" : "text-wr-low"}`}>
                          {entry.match.win ? "Win" : "Loss"}
                        </p>
                        <p className="text-xs text-muted">{entry.match.matchId ?? "Unknown match id"}</p>
                        <p className="mt-1 text-xs text-muted">Patch {entry.patch ?? "Unknown"}</p>
                      </div>

                      <div className="text-left lg:text-right">
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
