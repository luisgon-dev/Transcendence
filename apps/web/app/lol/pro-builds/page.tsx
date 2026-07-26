import Image from "next/image";
import Link from "next/link";
import { Suspense } from "react";
import type { components } from "@transcendence/api-client";

import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { BackendErrorCard } from "@/components/BackendErrorCard";
import { ProScopeToggle } from "@/components/ProScopeToggle";
import { Card } from "@/components/ui/Card";
import { DataBar } from "@/components/ui/DataBar";
import { Skeleton } from "@/components/ui/Skeleton";
import { Toolbar } from "@/components/ui/Toolbar";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { formatDateTimeMs, formatGames, formatRelativeTime } from "@/lib/format";
import { championDisplayName, itemDisplayName } from "@/lib/gameDisplay";
import { buildProBuildPageHref, normalizeProBuildScope, type ProBuildScope } from "@/lib/proBuilds";
import { encodeRiotIdPath } from "@/lib/riotid";
import { championIconUrl, fetchChampionMap, fetchItemMap, itemIconUrl } from "@/lib/staticData";
import { normalizeTierListEntries } from "@/lib/tierlist";

type TierListResponse = components["schemas"]["TierListResponse"];
type ChampionProBuildsResponse = components["schemas"]["ChampionProBuildsResponse"];
type ProMatchBuildDto = components["schemas"]["ProMatchBuildDto"];
type ProChampionPlayrateResponse = components["schemas"]["ProChampionPlayrateResponse"];
type ProRosterResponse = components["schemas"]["ProRosterResponse"];

const SCOPE_TITLE: Record<ProBuildScope, string> = {
  all: "Pro and One-Trick",
  pro: "Pro",
  highelo: "One-Trick"
};

const SCOPE_NOUN: Record<ProBuildScope, string> = {
  all: "pros and high-elo one-tricks",
  pro: "pro players",
  highelo: "high-elo one-tricks"
};

const MAX_PLAYRATE_ROWS = 30;

function buildProHomeHref({
  scope,
  region,
  query
}: {
  scope: string;
  region: string;
  query: string | null;
}) {
  const params = new URLSearchParams();
  if (scope && scope !== "pro") params.set("scope", scope);
  if (region && region !== "ALL") params.set("region", region);
  if (query) params.set("q", query);
  const qs = params.toString();
  return qs ? `/lol/pro-builds?${qs}` : "/lol/pro-builds";
}

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
  searchParams?: Promise<{ q?: string; region?: string; scope?: string }>;
}) {
  const verbosity = getErrorVerbosity();
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const { activeRegion, activeRegionLabel, options: regionOptions } = await resolveAnalyticsRegion(
    resolvedSearchParams?.region
  );
  const championQuery = normalizeChampionQuery(resolvedSearchParams?.q);
  const scope = normalizeProBuildScope(resolvedSearchParams?.scope);
  const includeOneTricks = scope !== "pro";
  const tierListQuery = new URLSearchParams();
  if (activeRegion !== "ALL") tierListQuery.set("region", activeRegion);

  const [{ version, champions }, tierListRes, playrateRes, rosterRes] = await Promise.all([
    fetchChampionMap(),
    fetchBackendJson<TierListResponse>(`${getBackendBaseUrl()}/api/lol/analytics/tierlist?${tierListQuery.toString()}`, {
      next: { revalidate: 60 * 60 }
    }),
    fetchBackendJson<ProChampionPlayrateResponse>(
      `${getBackendBaseUrl()}/api/lol/analytics/pro/champions?scope=${encodeURIComponent(scope)}&region=${encodeURIComponent(activeRegion)}`,
      { next: { revalidate: 60 * 30 } }
    ),
    fetchBackendJson<ProRosterResponse>(
      `${getBackendBaseUrl()}/api/lol/analytics/pro/players?region=${encodeURIComponent(activeRegion)}`,
      { next: { revalidate: 60 * 30 } }
    )
  ]);

  const playrateChampions = (playrateRes.ok ? playrateRes.body?.champions ?? [] : []).slice(
    0,
    MAX_PLAYRATE_ROWS
  );
  const rosterPlayers = rosterRes.ok ? rosterRes.body?.players ?? [] : [];

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

  if (!tierListRes.ok) {
    return (
      <BackendErrorCard
        title="Pro Solo Queue Builds"
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
    <div className="grid gap-4">
      <Toolbar
        eyebrow="Ranked Solo Queue"
        title="Pro Solo Queue Builds"
        meta={
          <>
            <span className="type-tabular tabular-nums">{playrateChampions.length} ranked picks</span>
            <span aria-hidden="true">·</span>
            <span>{activeRegionLabel}</span>
            {championQuery ? (
              <>
                <span aria-hidden="true">·</span>
                <span>Search: {championQuery}</span>
              </>
            ) : null}
          </>
        }
        filters={<AnalyticsRegionFilter options={regionOptions} activeRegion={activeRegion} variant="select" />}
      />
      <AnalyticsSampleBanner
        sample={(tierListRes.body as { sample?: unknown } | null)?.sample as AnalyticsSampleLike}
      />

      <Card className="bg-surface-2/50 px-5 py-4">
        <p className="type-ui text-fg/80">
          Ranked Solo/Duo matches from tracked professional accounts. Tournament matches are not included.
        </p>
      </Card>

      <Card className="page-panel p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border/40 px-5 py-4">
          <div>
            <h2 className="type-section">{SCOPE_TITLE[scope]} Picks</h2>
            <p className="type-ui mt-1 text-muted">
              Champions most picked by tracked {SCOPE_NOUN[scope]} this patch.
            </p>
          </div>
          <ProScopeToggle
            checked={includeOneTricks}
            href={buildProHomeHref({
              scope: includeOneTricks ? "pro" : "all",
              region: activeRegion,
              query: championQuery
            })}
          />
        </div>

        {playrateChampions.length === 0 ? (
          <p className="px-5 py-6 text-sm text-muted">
            No tracked solo-queue picks are available for the current selection yet. Try a different region or scope.
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="type-overline text-muted">
                <tr className="border-b border-border/30">
                  <th className="w-10 py-2.5 pl-5 pr-3">#</th>
                  <th className="py-2.5 pr-4">Champion</th>
                  <th className="py-2.5 pr-4 text-right">Games</th>
                  <th className="py-2.5 pr-4 text-right">Win Rate</th>
                  <th className="py-2.5 pr-5 text-right">Players</th>
                </tr>
              </thead>
              <tbody>
                {playrateChampions.map((entry, idx) => {
                  const championId = entry.championId ?? 0;
                  const champion = championById.get(championId);
                  const slug = champion?.slug ?? "Unknown";
                  const name = championDisplayName(champion);
                  return (
                    <tr
                      key={championId || idx}
                      className="border-t border-border/40 transition hover:bg-surface-2/40"
                    >
                      <td className="type-tabular py-2.5 pl-5 pr-3 tabular-nums text-muted">{idx + 1}</td>
                      <td className="py-2.5 pr-4">
                        <Link
                          href={buildProBuildPageHref(championId, {
                            role: "ALL",
                            region: activeRegion,
                            scope,
                            patch: null
                          })}
                          className="flex min-w-0 items-center gap-2.5 hover:underline"
                        >
                          <Image
                            src={championIconUrl(version, slug)}
                            alt={name}
                            width={28}
                            height={28}
                            className="rounded-md border border-border/50"
                          />
                          <span className="truncate font-medium text-fg">{name}</span>
                        </Link>
                      </td>
                      <td className="type-tabular py-2.5 pr-4 text-right tabular-nums text-fg/75">
                        {formatGames(entry.games ?? 0)}
                      </td>
                      <td className="py-2.5 pr-4 text-right">
                        <DataBar value={entry.winRate ?? 0} decimals={1} className="justify-end" />
                      </td>
                      <td className="type-tabular py-2.5 pr-5 text-right tabular-nums text-fg/70">
                        {entry.uniquePlayers ?? 0}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {rosterPlayers.length > 0 ? (
        <Card className="p-5">
          <div className="flex items-center justify-between gap-3">
            <div>
              <h2 className="type-section">Tracked Pros</h2>
              <p className="type-ui mt-1 text-muted">Open a tracked pro profile.</p>
            </div>
            <span className="type-tabular tabular-nums text-muted">{rosterPlayers.length} players</span>
          </div>
          <div className="mt-4 grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
            {rosterPlayers.map((pro, idx) => {
              const displayName =
                pro.proName?.trim() ||
                (pro.gameName ? `${pro.gameName}#${pro.tagLine ?? ""}` : "Unknown player");
              const meta = [pro.teamName, pro.platformRegion?.toUpperCase()].filter(Boolean).join(" · ");
              const canLink = Boolean(pro.gameName && pro.tagLine && pro.platformRegion);
              const body = (
                <>
                  <p className="truncate text-sm font-medium text-fg">{displayName}</p>
                  {meta ? <p className="truncate text-xs text-muted">{meta}</p> : null}
                </>
              );
              return canLink ? (
                <Link
                  key={`${displayName}-${idx}`}
                  href={`/lol/summoners/${(pro.platformRegion ?? "").toLowerCase()}/${encodeRiotIdPath({ gameName: pro.gameName!, tagLine: pro.tagLine! })}`}
                  className="surface-subtle min-w-0 rounded-card p-3 transition hover:bg-surface-2/72"
                >
                  {body}
                </Link>
              ) : (
                <div key={`${displayName}-${idx}`} className="surface-subtle min-w-0 rounded-card p-3 opacity-80">
                  {body}
                </div>
              );
            })}
          </div>
        </Card>
      ) : null}

      <Card className="page-panel p-5">
        <h2 className="type-section">Search Champions</h2>
        <form action="/lol/pro-builds" method="get" className="mt-3 flex flex-wrap items-center gap-2">
          {activeRegion !== "ALL" ? <input type="hidden" name="region" value={activeRegion} /> : null}
          {scope !== "pro" ? <input type="hidden" name="scope" value={scope} /> : null}
          <input
            type="text"
            name="q"
            defaultValue={championQuery ?? ""}
            placeholder="Search champion name (for example, Ahri)"
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
              href={buildProHomeHref({ scope, region: activeRegion, query: null })}
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
                href={buildProBuildPageHref(champion.championId, {
                  role: "ALL",
                  region: activeRegion,
                  scope,
                  patch: null
                })}
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

      <Suspense fallback={<RecentProMatchesFallback />}>
        <RecentProMatches
          championIds={feedChampionIds}
          championById={championById}
          version={version}
          activeRegion={activeRegion}
          scope={scope}
        />
      </Suspense>
    </div>
  );
}

function RecentProMatchesFallback() {
  return (
    <Card className="p-0" aria-busy="true" aria-label="Loading recent tracked solo-queue matches">
      <div className="border-b border-border/40 px-4 py-3">
        <h2 className="type-section">Recent Tracked Solo Queue Matches</h2>
        <p className="type-ui mt-1 text-muted">Loading the latest tracked builds…</p>
      </div>
      <div className="grid gap-0">
        {Array.from({ length: 5 }, (_, index) => (
          <div
            key={index}
            className="grid gap-3 border-b border-border/20 px-4 py-3 sm:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]"
          >
            <div className="flex items-center gap-3">
              <Skeleton className="size-[34px] shrink-0 rounded-md" />
              <div className="grid flex-1 gap-2">
                <Skeleton className="h-4 w-32" />
                <Skeleton className="h-3 w-48 max-w-full" />
              </div>
            </div>
            <div className="hidden justify-self-end sm:grid sm:gap-2">
              <Skeleton className="h-3 w-20" />
              <Skeleton className="h-3 w-28" />
            </div>
          </div>
        ))}
      </div>
    </Card>
  );
}

async function RecentProMatches({
  championIds,
  championById,
  version,
  activeRegion,
  scope
}: {
  championIds: number[];
  championById: Map<number, ChampionLookup>;
  version: string;
  activeRegion: string;
  scope: ProBuildScope;
}) {
  const [itemStatic, proResponses] = await Promise.all([
    fetchItemMap(),
    Promise.all(
      championIds.map(async (championId) => ({
        championId,
        response: await fetchBackendJson<ChampionProBuildsResponse>(
          `${getBackendBaseUrl()}/api/lol/analytics/champions/${championId}/pro-builds?region=${encodeURIComponent(activeRegion)}&role=ALL&scope=${encodeURIComponent(scope)}`,
          { next: { revalidate: 60 * 30 } }
        )
      }))
    )
  ]);

  const successfulFeeds = proResponses.filter((row) => row.response.ok && row.response.body);
  const proFeedRows: ProFeedRow[] = [];

  for (const row of successfulFeeds) {
    for (const match of (row.response.body?.recentProMatches ?? []).slice(0, MAX_MATCHES_PER_CHAMPION)) {
      proFeedRows.push({ championId: row.championId, match, patch: row.response.body?.patch });
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
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border/40 px-4 py-3">
        <div>
          <h2 className="type-section">Recent Tracked Solo Queue Matches</h2>
          <p className="type-ui mt-1 text-muted">
            {recentMatchesFeed.length > 0
              ? `${recentMatchesFeed.length} recent builds. Open any row for champion-specific details.`
              : "Click any row to open champion-specific solo-queue builds and recent match details."}
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
          No tracked solo-queue matches are available for the current selection.
        </p>
      ) : (
        <ul className="grid gap-0">
          {recentMatchesFeed.map((entry, idx) => {
            const champion = championById.get(entry.championId);
            const championSlug = champion?.slug ?? "Unknown";
            const championName = championDisplayName(champion);
            const playedAt = entry.match.playedAt ?? 0;
            const hasTimestamp = Number.isFinite(playedAt) && playedAt > 0;
            const items = (entry.match.items ?? [])
              .filter((itemId) => Number.isFinite(itemId) && itemId > 0)
              .slice(0, 6);

            return (
              <li key={`${entry.match.matchId ?? "match"}-${entry.championId}-${idx}`}>
                <Link
                  href={buildProBuildPageHref(entry.championId, {
                    role: "ALL",
                    region: activeRegion,
                    scope,
                    patch: null
                  })}
                  className="block border-b border-border/20 px-4 py-3 transition hover:bg-surface-2/40"
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
                              alt={itemDisplayName(itemMeta)}
                              title={itemDisplayName(itemMeta)}
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
  );
}
