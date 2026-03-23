import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { RoleFilterTabs } from "@/components/RoleFilterTabs";
import { RuneSetupDisplay } from "@/components/RuneSetupDisplay";
import { WinRateText } from "@/components/WinRateText";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { fetchBackendJson, type BackendJsonResult } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { pickMostSevereAnalyticsSample, type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { formatDateTimeMs, formatRelativeTime } from "@/lib/format";
import {
  buildProBuildFilterParams,
  buildProBuildPageHref,
  normalizeProBuildPatch,
  normalizeProBuildRole,
  PRO_BUILD_ROLES
} from "@/lib/proBuilds";
import { roleDisplayLabel } from "@/lib/roles";
import {
  championIconUrl,
  fetchChampionMap,
  fetchItemMap,
  fetchRunesReforged,
  itemIconUrl
} from "@/lib/staticData";

type ChampionWinRateSummary = components["schemas"]["ChampionWinRateSummary"];
type ChampionProBuildsResponse = components["schemas"]["ChampionProBuildsResponse"];

function proFeedErrorMessage(result: BackendJsonResult<ChampionProBuildsResponse>) {
  if (result.errorKind === "timeout") return "This page is taking too long to load.";
  if (result.errorKind === "unreachable") return "We couldn't load pro-build data right now.";
  return "We couldn't load pro-build data.";
}

function ProItemsRow({
  itemIds,
  itemVersion,
  items
}: {
  itemIds: number[];
  itemVersion: string;
  items: Record<string, { name: string; plaintext?: string }>;
}) {
  const cleaned = itemIds.filter((itemId) => Number.isFinite(itemId) && itemId > 0);
  if (cleaned.length === 0) {
    return <p className="text-xs text-muted">Items unavailable.</p>;
  }

  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {cleaned.map((itemId, idx) => {
        const meta = items[String(itemId)];
        const title = meta
          ? `${meta.name}${meta.plaintext ? ` - ${meta.plaintext}` : ""}`
          : `Item ${itemId}`;
        return (
          <Image
            key={`${itemId}-${idx}`}
            src={itemIconUrl(itemVersion, itemId)}
            alt={meta?.name ?? `Item ${itemId}`}
            title={title}
            width={28}
            height={28}
            className="rounded-md border border-border/50"
          />
        );
      })}
    </div>
  );
}

export default async function ProBuildsChampionPage({
  params,
  searchParams
}: {
  params: Promise<{ championId: string }>;
  searchParams?: Promise<{ role?: string; region?: string; patch?: string }>;
}) {
  const resolvedParams = await params;
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const { activeRegion, activeRegionLabel, options: regionOptions } = await resolveAnalyticsRegion(
    resolvedSearchParams?.region
  );
  const championId = Number(resolvedParams.championId);
  if (!Number.isFinite(championId) || championId <= 0) {
    return <BackendErrorCard title="Pro Builds" message="Invalid champion id." />;
  }

  const roleFilter = normalizeProBuildRole(resolvedSearchParams?.role);
  const regionFilter = activeRegion;
  const patchFilter = normalizeProBuildPatch(resolvedSearchParams?.patch);

  const proFilters = buildProBuildFilterParams({
    role: roleFilter,
    region: regionFilter,
    patch: patchFilter
  });
  const proFilterQuery = proFilters.toString();

  const verbosity = getErrorVerbosity();
  const [{ version, champions }, itemStatic, runeStatic, winratesRes, proBuildsRes] =
    await Promise.all([
    fetchChampionMap(),
    fetchItemMap(),
    fetchRunesReforged(),
    fetchBackendJson<ChampionWinRateSummary>(
      `${getBackendBaseUrl()}/api/lol/analytics/champions/${championId}/winrates`,
      { next: { revalidate: 60 * 60 } }
    ),
    fetchBackendJson<ChampionProBuildsResponse>(
      `${getBackendBaseUrl()}/api/lol/analytics/champions/${championId}/pro-builds${
        proFilterQuery ? `?${proFilterQuery}` : ""
      }`,
      { next: { revalidate: 60 * 60 } }
    )
  ]);

  if (!winratesRes.ok && !proBuildsRes.ok) {
    return (
      <BackendErrorCard
        title="Pro Builds"
        message={
          proBuildsRes.errorKind === "timeout"
            ? "This page is taking too long to load."
            : proBuildsRes.errorKind === "unreachable"
              ? "We couldn't load pro-build data right now."
              : "We couldn't load pro-build data."
        }
        requestId={winratesRes.requestId || proBuildsRes.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify(
                {
                  winrates: { status: winratesRes.status, errorKind: winratesRes.errorKind },
                  proBuilds: {
                    status: proBuildsRes.status,
                    errorKind: proBuildsRes.errorKind
                  }
                },
                null,
                2
              )
            : null
        }
      />
    );
  }

  const winrates = winratesRes.ok ? winratesRes.body : null;
  const proBuilds = proBuildsRes.ok ? proBuildsRes.body : null;
  const champion = champions[String(championId)];
  const championName = champion?.name ?? `Champion ${championId}`;
  const recentMatches = proBuilds?.recentProMatches ?? [];
  const topPlayers = proBuilds?.topPlayers ?? [];
  const commonBuilds = proBuilds?.commonBuilds ?? [];
  const roleExtraParams: Record<string, string> = {};
  if (regionFilter !== "ALL") roleExtraParams.region = regionFilter;
  if (patchFilter) roleExtraParams.patch = patchFilter;

  const effectivePatch =
    proBuilds?.patch ?? winrates?.patch ?? patchFilter ?? "Unknown";
  const effectiveRole = proBuilds?.role ?? roleFilter;
  const sampleNotice = pickMostSevereAnalyticsSample(
    (proBuilds as { sample?: unknown } | null)?.sample as AnalyticsSampleLike,
    (winrates as { sample?: unknown } | null)?.sample as AnalyticsSampleLike
  );

  return (
    <div className="grid gap-6">
      <header className="grid gap-3">
        <div className="flex items-center gap-3">
          <Image
            src={championIconUrl(version, champion?.id ?? "Unknown")}
            alt={championName}
            width={60}
            height={60}
            className="rounded-xl border border-border/60"
          />
          <div>
            <h1 className="type-title sm:text-[2.4rem]">
              Pro Builds
            </h1>
            <p className="type-ui mt-2 text-fg/75">Recent pro and high-MMR builds for {championName}</p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge className="border-primary/45 bg-primary/10 text-primary">
            Patch {effectivePatch}
          </Badge>
          <Badge>{roleDisplayLabel(effectiveRole)}</Badge>
          <Badge>{activeRegionLabel}</Badge>
          <Badge>{recentMatches.length} matches</Badge>
        </div>
        <AnalyticsSampleBanner sample={sampleNotice} />
      </header>

      <Card className="p-5">
        <h2 className="type-section">Filters</h2>
        <div className="mt-3 grid gap-3">
          <RoleFilterTabs
            roles={PRO_BUILD_ROLES}
            activeRole={roleFilter}
            baseHref={`/lol/pro-builds/${championId}`}
            extraParams={roleExtraParams}
          />
          <AnalyticsRegionFilter options={regionOptions} activeRegion={activeRegion} />
          <form action={`/lol/pro-builds/${championId}`} method="get" className="flex flex-wrap items-center gap-2">
            {roleFilter !== "ALL" ? <input type="hidden" name="role" value={roleFilter} /> : null}
            {regionFilter !== "ALL" ? (
              <input type="hidden" name="region" value={regionFilter} />
            ) : null}
            <label htmlFor="patch" className="text-xs text-muted">
              Patch
            </label>
            <input
              id="patch"
              name="patch"
              defaultValue={patchFilter ?? ""}
              placeholder="14.5"
              className="control-input h-9 w-28 rounded-control px-3 text-sm"
            />
            <button
              type="submit"
              className="h-9 rounded-control border border-primary/40 bg-primary/10 px-3 text-sm font-semibold text-primary hover:bg-primary/20"
            >
              Apply
            </button>
            {patchFilter ? (
              <Link
                href={buildProBuildPageHref(championId, {
                  role: roleFilter,
                  region: regionFilter,
                  patch: null
                })}
                className="control-tab type-ui h-9 px-3"
              >
                Clear
              </Link>
            ) : null}
          </form>
        </div>
      </Card>

      {!proBuildsRes.ok ? (
        <Card className="surface-subtle p-5">
          <h2 className="type-section">Pro-Build Data Unavailable</h2>
          <p className="mt-2 text-sm text-fg/80">{proFeedErrorMessage(proBuildsRes)}</p>
          <p className="mt-1 text-xs text-muted">Try adjusting your filters or check back soon.</p>
          {proBuildsRes.requestId ? (
            <details className="mt-3 text-xs text-muted">
              <summary className="cursor-pointer select-none rounded hover:text-fg/65 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40">Technical details</summary>
              <p className="mt-1">Request ID: <code className="text-fg/65">{proBuildsRes.requestId}</code></p>
            </details>
          ) : null}
        </Card>
      ) : null}

      <div className="grid gap-6 md:grid-cols-2">
        <Card className="p-5">
          <h2 className="type-section">Top Players</h2>
          {topPlayers.length === 0 ? (
            <p className="mt-3 text-sm text-muted">
              No pro-player matches are available for these filters yet.
            </p>
          ) : (
            <ul className="mt-3 grid gap-2">
              {topPlayers.map((player, idx) => (
                <li
                  key={`${player.playerName ?? "player"}-${idx}`}
                  className="surface-subtle flex items-center justify-between rounded-card px-3 py-2"
                >
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium text-fg">
                      {player.playerName ?? "Unknown player"}
                    </p>
                    <p className="truncate text-xs text-muted">
                      {player.teamName ?? "No team"}
                    </p>
                  </div>
                  <p className="text-xs text-muted">
                    <WinRateText value={player.winRate} decimals={1} games={player.games} />
                  </p>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card className="p-5">
          <h2 className="type-section">Common Builds</h2>
          {commonBuilds.length === 0 ? (
            <p className="mt-3 text-sm text-muted">
              No repeat build patterns are available for these filters yet.
            </p>
          ) : (
            <div className="mt-3 grid gap-3">
              {commonBuilds.map((build, idx) => (
                <div
                  key={`common-build-${idx}`}
                  className="surface-subtle rounded-card p-3"
                >
                  <div className="mb-2 flex items-center justify-between gap-2">
                    <p className="text-xs text-muted">Build #{idx + 1}</p>
                    <p className="text-xs text-muted">
                      <WinRateText value={build.winRate} decimals={1} games={build.games} />
                    </p>
                  </div>
                  <ProItemsRow
                    itemIds={build.items ?? []}
                    itemVersion={itemStatic.version}
                    items={itemStatic.items}
                  />
                </div>
              ))}
            </div>
          )}
        </Card>
      </div>

      <Card className="p-5">
        <h2 className="type-section">Recent Pro Matches</h2>
        {recentMatches.length === 0 ? (
          <p className="mt-3 text-sm text-muted">
            No recent pro matches were found for this champion with these filters.
          </p>
        ) : (
          <ul className="mt-4 grid gap-3">
            {recentMatches.map((match, idx) => {
              const playedAt = match.playedAt ?? 0;
              const hasTimestamp = Number.isFinite(playedAt) && playedAt > 0;
              const resultClass = match.win ? "text-wr-high" : "text-wr-low";
              return (
                <li
                  key={`${match.matchId ?? "match"}-${idx}`}
                  className="surface-subtle rounded-card p-3"
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <p className="text-sm font-medium text-fg">
                        {match.playerName ?? "Unknown player"}
                        <span className="ml-2 text-xs text-muted">
                          {match.teamName ? `(${match.teamName})` : ""}
                        </span>
                      </p>
                      <p className="text-xs text-muted">
                        {hasTimestamp
                          ? `${formatRelativeTime(playedAt)} - ${formatDateTimeMs(playedAt)}`
                          : "Timestamp unavailable"}
                      </p>
                    </div>
                    <div className="text-right">
                      <p className={`text-sm font-semibold ${resultClass}`}>
                        {match.win ? "Win" : "Loss"}
                      </p>
                      <p className="text-[11px] text-muted">{match.matchId ?? "Unknown match"}</p>
                    </div>
                  </div>

                  <div className="mt-3 grid gap-3 lg:grid-cols-[auto,1fr]">
                    <div className="grid gap-1.5">
                      <p className="text-xs text-muted">Items</p>
                      <ProItemsRow
                        itemIds={match.items ?? []}
                        itemVersion={itemStatic.version}
                        items={itemStatic.items}
                      />
                    </div>
                    <div className="grid gap-1.5">
                      <p className="text-xs text-muted">Runes</p>
                      <RuneSetupDisplay
                        primaryStyleId={match.primaryStyleId ?? 0}
                        subStyleId={match.subStyleId ?? 0}
                        primarySelections={match.primaryRunes ?? []}
                        subSelections={match.subRunes ?? []}
                        statShards={match.statShards ?? []}
                        runeById={runeStatic.runeById}
                        styleById={runeStatic.styleById}
                        runeSortById={runeStatic.runeSortById}
                        iconSize={18}
                        density="compact"
                      />
                    </div>
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </Card>

      <Card className="border-primary/40 bg-primary/10 p-5">
        <h2 className="type-section text-primary">
          More Ways to Explore
        </h2>
        <p className="mt-2 text-sm text-fg/90">
          Open the main champion page or matchup view to compare pro builds with the broader player base.
        </p>
        <div className="mt-4 flex flex-wrap gap-2 text-sm">
          <Link
            href={`/lol/champions/${championId}`}
            className="control-tab type-ui font-semibold"
            data-active="true"
          >
            Open Champion Details
          </Link>
          <Link
            href={`/lol/matchups/${championId}`}
            className="control-tab type-ui"
          >
            Open Matchup Analysis
          </Link>
        </div>
      </Card>
    </div>
  );
}
