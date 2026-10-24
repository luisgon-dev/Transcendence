import Image from "next/image";
import Link from "next/link";
import { Suspense } from "react";
import type { components } from "@transcendence/api-client";

import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { BackendErrorCard } from "@/components/BackendErrorCard";
import { ItemStrip, RunePair } from "@/components/ProBuildBits";
import { ProScopeToggle } from "@/components/ProScopeToggle";
import { DataBar } from "@/components/ui/DataBar";
import { Skeleton } from "@/components/ui/Skeleton";
import { Toolbar } from "@/components/ui/Toolbar";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { cn } from "@/lib/cn";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { formatDateTimeMs, formatGames, formatRelativeTime } from "@/lib/format";
import { championDisplayName } from "@/lib/gameDisplay";
import { buildProBuildPageHref, normalizeProBuildScope, type ProBuildScope } from "@/lib/proBuilds";
import { encodeRiotIdPath } from "@/lib/riotid";
import {
  championIconUrl,
  fetchChampionMap,
  fetchItemMap,
  fetchRunesReforged
} from "@/lib/staticData";

type ChampionProBuildsResponse = components["schemas"]["ChampionProBuildsResponse"];
type ProMatchBuildDto = components["schemas"]["ProMatchBuildDto"];
type ProChampionPlayrateResponse = components["schemas"]["ProChampionPlayrateResponse"];
type ProRosterResponse = components["schemas"]["ProRosterResponse"];
type ProPlayerDto = components["schemas"]["ProPlayerDto"];

const SCOPE_NOUN: Record<ProBuildScope, string> = {
  all: "pros and one-tricks",
  pro: "pros",
  highelo: "one-tricks"
};

const MAX_PLAYRATE_ROWS = 20;
const MAX_SEARCH_RESULTS = 12;
const MAX_FEED_CHAMPIONS_DEFAULT = 8;
const MAX_FEED_CHAMPIONS_SEARCH = 6;
const MAX_MATCHES_PER_CHAMPION = 8;
const MAX_FEED_ROWS = 15;

const ROW_LINK =
  "transition-colors hover:bg-surface-2/55 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary/45";

type ChampionLookup = {
  championId: number;
  slug: string;
  name: string;
};

type ProFeedRow = {
  championId: number;
  match: ProMatchBuildDto;
};

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

function normalizeChampionQuery(query: string | undefined): string | null {
  const trimmed = query?.trim();
  return trimmed ? trimmed : null;
}

function compareChampionQueryRelevance(a: ChampionLookup, b: ChampionLookup, queryLower: string) {
  const aName = a.name.toLowerCase();
  const bName = b.name.toLowerCase();
  const aStarts = aName.startsWith(queryLower) ? 0 : 1;
  const bStarts = bName.startsWith(queryLower) ? 0 : 1;
  if (aStarts !== bStarts) return aStarts - bStarts;
  if (aName.length !== bName.length) return aName.length - bName.length;
  return aName.localeCompare(bName);
}

function proProfileHref(pro: ProPlayerDto): string | null {
  if (!pro.gameName || !pro.tagLine || !pro.platformRegion) return null;
  return `/lol/summoners/${pro.platformRegion.toLowerCase()}/${encodeRiotIdPath({
    gameName: pro.gameName,
    tagLine: pro.tagLine
  })}`;
}

// Esports fans read a roster by team, so the tracked list is grouped that way; bigger teams first.
function groupRosterByTeam(players: ProPlayerDto[]) {
  const teams = new Map<string, ProPlayerDto[]>();
  for (const pro of players) {
    const team = pro.teamName?.trim() || "Free agents";
    teams.set(team, [...(teams.get(team) ?? []), pro]);
  }
  return [...teams.entries()].sort((a, b) => b[1].length - a[1].length || a[0].localeCompare(b[0]));
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

  const [{ version, champions }, playrateRes, rosterRes] = await Promise.all([
    fetchChampionMap(),
    fetchBackendJson<ProChampionPlayrateResponse>(
      `${getBackendBaseUrl()}/api/lol/analytics/pro/champions?scope=${encodeURIComponent(scope)}&region=${encodeURIComponent(activeRegion)}`,
      { next: { revalidate: 60 * 30 } }
    ),
    fetchBackendJson<ProRosterResponse>(
      `${getBackendBaseUrl()}/api/lol/analytics/pro/players?region=${encodeURIComponent(activeRegion)}`,
      { next: { revalidate: 60 * 30 } }
    )
  ]);

  if (!playrateRes.ok && !rosterRes.ok) {
    return (
      <BackendErrorCard
        title="Pro Solo Q"
        message={
          playrateRes.errorKind === "timeout"
            ? "This page is taking too long to load."
            : playrateRes.errorKind === "unreachable"
              ? "We couldn't load pro solo queue data right now."
              : "We couldn't load pro solo queue data."
        }
        requestId={playrateRes.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify({ status: playrateRes.status, errorKind: playrateRes.errorKind }, null, 2)
            : null
        }
      />
    );
  }

  const allPlayrates = playrateRes.ok ? playrateRes.body?.champions ?? [] : [];
  const playrateChampions = allPlayrates.slice(0, MAX_PLAYRATE_ROWS);
  const trackedGames = allPlayrates.reduce((sum, entry) => sum + (entry.games ?? 0), 0);
  const patch = playrateRes.ok ? playrateRes.body?.patch : null;
  const rosterPlayers = rosterRes.ok ? rosterRes.body?.players ?? [] : [];
  const rosterTeams = groupRosterByTeam(rosterPlayers);

  const championCatalog: ChampionLookup[] = Object.entries(champions)
    .map(([championId, champion]) => ({
      championId: Number(championId),
      slug: champion.id,
      name: champion.name
    }))
    .filter((row) => Number.isFinite(row.championId) && row.championId > 0);
  const championById = new Map(championCatalog.map((champion) => [champion.championId, champion]));

  const queryLower = championQuery?.toLowerCase() ?? null;
  const matchingChampions = queryLower
    ? championCatalog
        .filter(
          (champion) =>
            champion.name.toLowerCase().includes(queryLower) ||
            String(champion.championId) === queryLower
        )
        .sort((a, b) => compareChampionQueryRelevance(a, b, queryLower))
    : [];

  // The feed follows what pros actually play (or the search), not the general ladder.
  const feedChampionIds = championQuery
    ? matchingChampions.slice(0, MAX_FEED_CHAMPIONS_SEARCH).map((champion) => champion.championId)
    : allPlayrates
        .slice(0, MAX_FEED_CHAMPIONS_DEFAULT)
        .map((entry) => entry.championId ?? 0)
        .filter((championId) => championId > 0);

  const detailHref = (championId: number) =>
    buildProBuildPageHref(championId, { role: "ALL", region: activeRegion, scope, patch: null });

  return (
    <div className="grid grid-cols-1 gap-4">
      <Toolbar
        title="Pro Solo Q"
        meta={
          <>
            {patch ? <span>Patch {patch}</span> : null}
            <span>{activeRegionLabel}</span>
            {trackedGames > 0 ? (
              <span className="type-tabular tabular-nums">
                {formatGames(trackedGames)} games from tracked {SCOPE_NOUN[scope]}
              </span>
            ) : null}
          </>
        }
        actions={
          <ProScopeToggle
            checked={includeOneTricks}
            href={buildProHomeHref({
              scope: includeOneTricks ? "pro" : "all",
              region: activeRegion,
              query: championQuery
            })}
          />
        }
        filters={
          <>
            <AnalyticsRegionFilter options={regionOptions} activeRegion={activeRegion} variant="select" />
            <form action="/lol/pro-builds" method="get" className="flex min-w-0 flex-1 items-center gap-2">
              {activeRegion !== "ALL" ? <input type="hidden" name="region" value={activeRegion} /> : null}
              {scope !== "pro" ? <input type="hidden" name="scope" value={scope} /> : null}
              <label className="min-w-0 flex-1 sm:max-w-sm">
                <span className="sr-only">Find a champion</span>
                <input
                  type="search"
                  name="q"
                  defaultValue={championQuery ?? ""}
                  placeholder="Find a champion's pro builds"
                  className="control-input h-9"
                />
              </label>
              {championQuery ? (
                <Link
                  href={buildProHomeHref({ scope, region: activeRegion, query: null })}
                  className="type-ui rounded-sm text-muted transition-colors hover:text-fg"
                >
                  Clear
                </Link>
              ) : null}
            </form>
          </>
        }
      />

      {championQuery ? (
        <section aria-label={`Champions matching ${championQuery}`} className="page-panel px-4 py-3 sm:px-5">
          {matchingChampions.length === 0 ? (
            <p className="type-ui text-muted">No champion matches &ldquo;{championQuery}&rdquo;.</p>
          ) : (
            <ul className="flex flex-wrap gap-2">
              {matchingChampions.slice(0, MAX_SEARCH_RESULTS).map((champion) => (
                <li key={champion.championId}>
                  <Link
                    href={detailHref(champion.championId)}
                    className="group inline-flex items-center gap-2 rounded-control border border-border bg-surface py-1 pl-1 pr-3 transition-colors hover:border-border-strong hover:bg-surface-2/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  >
                    <Image
                      src={championIconUrl(version, champion.slug)}
                      alt=""
                      width={28}
                      height={28}
                      sizes="28px"
                      className="rounded-md"
                    />
                    <span className="type-ui font-semibold text-fg group-hover:text-primary">{champion.name}</span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </section>
      ) : null}

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2 lg:items-start">
        <section aria-labelledby="pro-picks-heading" className="page-panel overflow-hidden">
          <div className="px-4 py-4 sm:px-5">
            <h2 id="pro-picks-heading" className="type-title text-fg">
              What pros are playing
            </h2>
            <p className="type-caption mt-1 text-muted">
              Ranked Solo/Duo on tracked accounts this patch; tournament games are not included.
            </p>
          </div>
          {playrateChampions.length === 0 ? (
            <p className="border-t border-border px-5 py-6 text-sm text-muted">
              No tracked solo-queue picks for this region and scope yet.
            </p>
          ) : (
            <div className="overflow-x-auto border-t border-border">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="border-b border-border">
                  <th scope="col" className="type-overline hidden w-10 py-2.5 pl-4 pr-2 font-semibold text-muted sm:table-cell sm:pl-5">
                    #
                  </th>
                  <th scope="col" className="type-overline w-full py-2.5 pl-4 pr-3 font-semibold text-muted sm:pl-0">
                    Champion
                  </th>
                  <th scope="col" className="type-overline py-2.5 pr-3 text-right font-semibold text-muted">
                    Games
                  </th>
                  <th scope="col" className="type-overline hidden py-2.5 pr-3 text-right font-semibold text-muted sm:table-cell">
                    Pros
                  </th>
                  <th scope="col" className="type-overline py-2.5 pr-4 text-right font-semibold text-muted sm:pr-5">
                    Win rate
                  </th>
                </tr>
              </thead>
              <tbody>
                {playrateChampions.map((entry, idx) => {
                  const championId = entry.championId ?? 0;
                  const champion = championById.get(championId);
                  return (
                    <tr key={championId || idx} className="tierlist-row border-b border-border/50 last:border-b-0">
                      <td className="type-tabular hidden py-2 pl-4 pr-2 tabular-nums text-muted sm:table-cell sm:pl-5">{idx + 1}</td>
                      <td className="max-w-0 py-2 pl-4 pr-3 sm:pl-0">
                        <Link
                          href={detailHref(championId)}
                          className="group flex min-w-0 items-center gap-2.5 rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                        >
                          <Image
                            src={championIconUrl(version, champion?.slug ?? "Unknown")}
                            alt=""
                            width={30}
                            height={30}
                            sizes="30px"
                            className="shrink-0 rounded-md border border-border/50"
                          />
                          <span className="truncate font-semibold text-fg transition-colors group-hover:text-primary">
                            {championDisplayName(champion)}
                          </span>
                        </Link>
                      </td>
                      <td className="type-tabular py-2 pr-3 text-right tabular-nums text-fg/85">
                        {formatGames(entry.games ?? 0)}
                      </td>
                      <td className="type-tabular hidden py-2 pr-3 text-right tabular-nums text-fg/70 sm:table-cell">
                        {entry.uniquePlayers ?? 0}
                      </td>
                      <td className="py-2 pr-4 text-right sm:pr-5">
                        {/* Pro samples are small; the whisker shows how wide the plausible range is. */}
                        <DataBar value={entry.winRate ?? 0} games={entry.games ?? 0} className="justify-end" />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
            </div>
          )}
        </section>

        <Suspense fallback={<RecentProMatchesFallback />}>
          <RecentProMatches
            championIds={feedChampionIds}
            championById={championById}
            version={version}
            activeRegion={activeRegion}
            scope={scope}
            searching={championQuery != null}
          />
        </Suspense>
      </div>

      {rosterTeams.length > 0 ? (
        <section aria-labelledby="roster-heading" className="page-panel px-4 py-4 sm:px-5">
          <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
            <h2 id="roster-heading" className="type-title text-fg">
              Tracked pros
            </h2>
            <p className="type-caption text-muted">
              <span className="type-tabular tabular-nums">{rosterPlayers.length}</span> players on{" "}
              <span className="type-tabular tabular-nums">{rosterTeams.length}</span> teams
            </p>
          </div>
          <div className="mt-4 columns-2 gap-x-6 sm:columns-3 lg:columns-5">
            {rosterTeams.map(([team, players]) => (
              <div key={team} className="mb-4 break-inside-avoid">
                <h3 className="type-caption truncate font-semibold text-muted">{team}</h3>
                <ul className="mt-1 grid gap-0.5">
                  {players.map((pro, idx) => {
                    const name =
                      pro.proName?.trim() || (pro.gameName ? `${pro.gameName}#${pro.tagLine ?? ""}` : "Unknown player");
                    const href = proProfileHref(pro);
                    return (
                      <li key={`${name}-${idx}`} className="flex min-w-0 items-baseline gap-1.5">
                        {href ? (
                          <Link
                            href={href}
                            className="type-ui truncate rounded-sm text-fg transition-colors hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                          >
                            {name}
                          </Link>
                        ) : (
                          <span className="type-ui truncate text-muted">{name}</span>
                        )}
                        {pro.platformRegion ? (
                          <span className="type-caption shrink-0 text-muted">{pro.platformRegion.toUpperCase()}</span>
                        ) : null}
                      </li>
                    );
                  })}
                </ul>
              </div>
            ))}
          </div>
        </section>
      ) : null}
    </div>
  );
}

function FeedHeader({ subtitle }: { subtitle: string }) {
  return (
    <div className="px-4 py-4 sm:px-5">
      <h2 id="pro-feed-heading" className="type-title text-fg">
        Latest games
      </h2>
      <p className="type-caption mt-1 text-muted">{subtitle}</p>
    </div>
  );
}

function RecentProMatchesFallback() {
  return (
    <section aria-busy="true" aria-labelledby="pro-feed-heading" className="page-panel overflow-hidden">
      <FeedHeader subtitle="Loading the latest tracked games…" />
      <ul className="border-t border-border">
        {Array.from({ length: 8 }, (_, index) => (
          <li key={index} className="flex max-w-none items-center gap-3 border-b border-border/50 px-4 py-2.5 last:border-b-0 sm:px-5">
            <Skeleton className="size-10 shrink-0 rounded-md" />
            <div className="grid flex-1 gap-1.5">
              <Skeleton className="h-4 w-32" />
              <Skeleton className="h-3 w-44 max-w-full" />
            </div>
            <Skeleton className="hidden h-6 w-40 sm:block" />
          </li>
        ))}
      </ul>
    </section>
  );
}

async function RecentProMatches({
  championIds,
  championById,
  version,
  activeRegion,
  scope,
  searching
}: {
  championIds: number[];
  championById: Map<number, ChampionLookup>;
  version: string;
  activeRegion: string;
  scope: ProBuildScope;
  searching: boolean;
}) {
  const [itemStatic, runeStatic, proResponses] = await Promise.all([
    fetchItemMap(),
    fetchRunesReforged(),
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
  const rows: ProFeedRow[] = successfulFeeds.flatMap((row) =>
    (row.response.body?.recentProMatches ?? [])
      .slice(0, MAX_MATCHES_PER_CHAMPION)
      .map((match) => ({ championId: row.championId, match }))
  );

  const seen = new Set<string>();
  const feed = rows
    .sort((a, b) => (b.match.playedAt ?? 0) - (a.match.playedAt ?? 0))
    .filter((row) => {
      const key = `${row.match.matchId ?? "unknown"}:${row.championId}`;
      if (seen.has(key)) return false;
      seen.add(key);
      return true;
    })
    .slice(0, MAX_FEED_ROWS);
  const failedFeedCount = proResponses.length - successfulFeeds.length;
  const subtitle = searching
    ? "Recent tracked games on the champions you searched."
    : "Recent tracked games on the champions pros play most.";

  return (
    <section aria-labelledby="pro-feed-heading" className="page-panel overflow-hidden">
      <FeedHeader subtitle={subtitle} />
      {feed.length === 0 ? (
        <p className="border-t border-border px-5 py-6 text-sm text-muted">
          No tracked solo-queue games for this selection yet.
        </p>
      ) : (
        <ul className="border-t border-border">
          {feed.map(({ championId, match }, idx) => {
            const champion = championById.get(championId);
            const championName = championDisplayName(champion);
            const playedAt = match.playedAt ?? 0;
            const hasTimestamp = Number.isFinite(playedAt) && playedAt > 0;
            return (
              <li key={`${match.matchId ?? "match"}-${championId}-${idx}`} className="max-w-none border-b border-border/50 last:border-b-0">
                <Link
                  href={buildProBuildPageHref(championId, { role: "ALL", region: activeRegion, scope, patch: null })}
                  className={cn("flex items-center gap-3 px-4 py-2.5 sm:px-5", ROW_LINK)}
                >
                  <span className="flex shrink-0 items-center gap-1">
                    <Image
                      src={championIconUrl(version, champion?.slug ?? "Unknown")}
                      alt={championName}
                      width={40}
                      height={40}
                      sizes="40px"
                      className="rounded-md border border-border/50"
                    />
                    <RunePair
                      primaryRunes={match.primaryRunes}
                      subStyleId={match.subStyleId}
                      runeStatic={runeStatic}
                      size={20}
                      className="flex-col"
                    />
                  </span>
                  <span className="grid min-w-0 flex-1">
                    <span className="flex min-w-0 items-baseline gap-1.5">
                      <span className="truncate font-semibold text-fg">{match.playerName ?? "Unknown player"}</span>
                      {match.teamName ? (
                        <span className="type-caption truncate text-muted">{match.teamName}</span>
                      ) : null}
                    </span>
                    <span className="type-caption truncate text-muted">
                      {championName}
                      {hasTimestamp ? (
                        <>
                          {" · "}
                          <time title={formatDateTimeMs(playedAt)}>{formatRelativeTime(playedAt)}</time>
                        </>
                      ) : null}
                    </span>
                  </span>
                  <ItemStrip itemIds={match.items} itemStatic={itemStatic} size={24} className="hidden shrink-0 flex-nowrap sm:flex" />
                  <span
                    className={cn(
                      "type-ui w-9 shrink-0 text-right font-semibold",
                      match.win ? "text-win" : "text-loss"
                    )}
                  >
                    {match.win ? "Win" : "Loss"}
                  </span>
                </Link>
              </li>
            );
          })}
        </ul>
      )}
      {failedFeedCount > 0 ? (
        <p className="type-caption border-t border-border px-4 py-2.5 text-muted sm:px-5">
          {failedFeedCount} champion {failedFeedCount === 1 ? "feed is" : "feeds are"} unavailable right now.
        </p>
      ) : null}
    </section>
  );
}
