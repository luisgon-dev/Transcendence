import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { AnalyticsPatchFilter } from "@/components/AnalyticsPatchFilter";
import { AnalyticsRegionFilter } from "@/components/AnalyticsRegionFilter";
import { ItemStrip, keystoneId, RunePair, SpellPair, cleanItemIds } from "@/components/ProBuildBits";
import { ProScopeToggle } from "@/components/ProScopeToggle";
import { RoleFilterTabs } from "@/components/RoleFilterTabs";
import { sortRuneSelections } from "@/components/lol-profile/shared";
import { buttonClassName } from "@/components/ui/buttonStyles";
import { DataBar } from "@/components/ui/DataBar";
import { fetchBackendJson, type BackendJsonResult } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { cn } from "@/lib/cn";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { formatDateTimeMs, formatGames, formatPercent, formatRelativeTime, plural } from "@/lib/format";
import { championDisplayName } from "@/lib/gameDisplay";
import { fetchLolAnalyticsPatches } from "@/lib/lolAnalyticsPatches";
import {
  buildProBuildPageHref,
  mostCommon,
  normalizeProBuildPatch,
  normalizeProBuildRole,
  normalizeProBuildScope,
  type ProBuildScope
} from "@/lib/proBuilds";
import { LANE_ROLES, roleDisplayLabel } from "@/lib/roles";
import {
  championIconUrl,
  fetchChampionMap,
  fetchItemMap,
  fetchRunesReforged,
  fetchSummonerSpellMap,
  runeIconUrl,
  type RuneStaticData
} from "@/lib/staticData";

type ChampionWinRateSummary = components["schemas"]["ChampionWinRateSummary"];
type ChampionProBuildsResponse = components["schemas"]["ChampionProBuildsResponse"];
type ProMatchBuildDto = components["schemas"]["ProMatchBuildDto"];

const SCOPE_NOUN: Record<ProBuildScope, string> = {
  all: "pros and one-tricks",
  pro: "pros",
  highelo: "one-tricks"
};

function proFeedErrorMessage(result: BackendJsonResult<ChampionProBuildsResponse>) {
  if (result.errorKind === "timeout") return "This page is taking too long to load.";
  if (result.errorKind === "unreachable") return "We couldn't load pro-build data right now.";
  return "We couldn't load pro-build data.";
}

function itemSetKey(itemIds: readonly number[] | null | undefined) {
  return cleanItemIds(itemIds)
    .sort((a, b) => a - b)
    .join(",");
}

function skillKeys(maxOrder: string | null | undefined): string[] {
  return (maxOrder ?? "").toUpperCase().replace(/[^QWER]/g, "").split("");
}

function SkillOrder({ maxOrder, size = "md" }: { maxOrder: string | null | undefined; size?: "sm" | "md" }) {
  const keys = skillKeys(maxOrder);
  if (keys.length === 0) return null;
  return (
    <span className="flex items-center gap-1" aria-label={`Skill max order ${keys.join(", ")}`}>
      {keys.map((key, idx) => (
        <span key={`${key}-${idx}`} className="flex items-center gap-1" aria-hidden="true">
          {idx > 0 ? <span className="text-muted">›</span> : null}
          <span
            className={cn(
              "grid place-items-center rounded-md border border-border bg-surface-2 font-semibold text-fg",
              size === "md" ? "size-8 text-sm" : "size-6 text-xs"
            )}
          >
            {key}
          </span>
        </span>
      ))}
    </span>
  );
}

function RuneIcon({ id, runeStatic, size }: { id: number; runeStatic: RuneStaticData; size: number }) {
  const rune = runeStatic.runeById[String(id)];
  if (!rune) return null;
  return (
    <Image
      src={runeIconUrl(rune.icon)}
      alt={rune.name}
      title={rune.name}
      width={size}
      height={size}
      sizes={`${size}px`}
      className="rounded-full bg-surface-2/70 p-0.5"
    />
  );
}

function BuildCell({
  label,
  footer,
  children
}: {
  label: string;
  footer?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <div className="grid content-start gap-3 bg-surface p-4 sm:p-5">
      <h3 className="type-caption font-semibold text-muted">{label}</h3>
      {children}
      {footer ? <p className="type-caption text-muted">{footer}</p> : null}
    </div>
  );
}

// A build seen once or twice reads 0% or 100%; below this many games only the count is shown.
const MIN_GAMES_FOR_WIN_RATE = 3;

function gamesLine(games: number, winRate: number) {
  const count = `${formatGames(games)} ${plural(games, "game")}`;
  return games >= MIN_GAMES_FOR_WIN_RATE ? `${count} · ${formatPercent(winRate)} win rate` : count;
}

function shareOf(count: number, total: number) {
  return `${count} of ${total} ${plural(total, "game")}`;
}

export default async function ProBuildsChampionPage({
  params,
  searchParams
}: {
  params: Promise<{ championId: string }>;
  searchParams?: Promise<{ role?: string; region?: string; scope?: string; patch?: string }>;
}) {
  const resolvedParams = await params;
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const { activeRegion, activeRegionLabel, options: regionOptions } = await resolveAnalyticsRegion(
    resolvedSearchParams?.region
  );
  const championId = Number(resolvedParams.championId);
  if (!Number.isFinite(championId) || championId <= 0) {
    return <BackendErrorCard title="Pro Solo Q" message="That champion route is invalid." />;
  }

  const roleFilter = normalizeProBuildRole(resolvedSearchParams?.role);
  const scopeFilter = normalizeProBuildScope(resolvedSearchParams?.scope);
  const patchFilter = normalizeProBuildPatch(resolvedSearchParams?.patch);

  const proFilterQuery = new URLSearchParams();
  if (roleFilter !== "ALL") proFilterQuery.set("role", roleFilter);
  if (activeRegion !== "ALL") proFilterQuery.set("region", activeRegion);
  if (scopeFilter !== "pro") proFilterQuery.set("scope", scopeFilter);
  if (patchFilter) proFilterQuery.set("patch", patchFilter);
  const winrateQuery = new URLSearchParams();
  if (patchFilter) winrateQuery.set("patch", patchFilter);

  const verbosity = getErrorVerbosity();
  const [{ version, champions }, itemStatic, runeStatic, spellStatic, patchOptions, winratesRes, proBuildsRes] =
    await Promise.all([
      fetchChampionMap(),
      fetchItemMap(),
      fetchRunesReforged(),
      fetchSummonerSpellMap(),
      fetchLolAnalyticsPatches(),
      fetchBackendJson<ChampionWinRateSummary>(
        `${getBackendBaseUrl()}/api/lol/analytics/champions/${championId}/winrates${winrateQuery.size ? `?${winrateQuery}` : ""}`,
        { next: { revalidate: 60 * 60 } }
      ),
      fetchBackendJson<ChampionProBuildsResponse>(
        `${getBackendBaseUrl()}/api/lol/analytics/champions/${championId}/pro-builds${proFilterQuery.size ? `?${proFilterQuery}` : ""}`,
        { next: { revalidate: 60 * 60 } }
      )
    ]);

  if (!winratesRes.ok && !proBuildsRes.ok) {
    return (
      <BackendErrorCard
        title="Pro Solo Q"
        message={proFeedErrorMessage(proBuildsRes)}
        requestId={winratesRes.requestId || proBuildsRes.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify(
                {
                  winrates: { status: winratesRes.status, errorKind: winratesRes.errorKind },
                  proBuilds: { status: proBuildsRes.status, errorKind: proBuildsRes.errorKind }
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
  const championName = championDisplayName(champion);
  const recentMatches = proBuilds?.recentProMatches ?? [];
  const topPlayers = proBuilds?.topPlayers ?? [];
  // Older cached responses can still contain the former empty-inventory aggregate.
  // Never promote that placeholder into the answer-first recommendation.
  const commonBuilds = (proBuilds?.commonBuilds ?? []).filter((build) => itemSetKey(build.items).length > 0);
  const coreBuild = commonBuilds[0] ?? null;
  const otherBuilds = commonBuilds.slice(1);

  // Runes, spells, and skill order are tallied across the recent tracked games rather than lifted
  // from one "representative" match, so each choice carries how many of those games made it.
  const runeChoice = mostCommon(recentMatches, (match) => {
    const keystone = keystoneId(match.primaryRunes, runeStatic);
    return keystone ? `${keystone}:${match.subStyleId ?? 0}` : null;
  });
  const spellChoice = mostCommon(recentMatches, (match) => {
    const ids = [match.spell1Id ?? 0, match.spell2Id ?? 0].filter((id) => id > 0).sort((a, b) => a - b);
    return ids.length === 2 ? ids.join("-") : null;
  });
  const skillChoice = mostCommon(recentMatches, (match) => {
    const keys = skillKeys(match.skillOrder?.maxOrder);
    return keys.length > 0 ? keys.join("") : null;
  });

  const effectivePatch = proBuilds?.patch ?? winrates?.patch ?? patchFilter;
  // When no role is in the URL the backend resolves the champion's most-played lane and echoes it
  // back as proBuilds.role; drive the active tab and the scope/patch links off that.
  const effectiveRole = normalizeProBuildRole(proBuilds?.role ?? roleFilter);
  const effectiveScope = normalizeProBuildScope(proBuilds?.scope ?? scopeFilter);
  const includeOneTricks = scopeFilter !== "pro";
  const roleExtraParams: Record<string, string> = {};
  if (activeRegion !== "ALL") roleExtraParams.region = activeRegion;
  if (scopeFilter !== "pro") roleExtraParams.scope = scopeFilter;
  if (patchFilter) roleExtraParams.patch = patchFilter;

  return (
    <div className="grid grid-cols-1 gap-4">
      <header className="toolbar relative isolate overflow-hidden px-4 py-4 sm:px-5">
        {champion ? (
          <div aria-hidden className="splash-backdrop">
            <Image
              src={`https://ddragon.leagueoflegends.com/cdn/img/champion/splash/${champion.id}_0.jpg`}
              alt=""
              fill
              priority
              quality={55}
              sizes="(min-width: 1024px) 60rem, 100vw"
              className="object-cover object-[50%_20%]"
            />
          </div>
        ) : null}
        <div className="flex flex-wrap items-center gap-x-4 gap-y-3">
          <Image
            src={championIconUrl(version, champion?.id ?? "Unknown")}
            alt=""
            width={64}
            height={64}
            priority
            className="size-14 shrink-0 rounded-card border border-border/60 sm:size-16"
          />
          <div className="min-w-0 flex-1">
            <p className="type-caption text-muted">
              <Link href="/lol/pro-builds" className="rounded-sm transition-colors hover:text-fg">
                Pro Solo Q
              </Link>
            </p>
            <h1 className="type-page-title text-fg">{championName}</h1>
            <p className="type-caption mt-1 flex flex-wrap gap-x-3 gap-y-1 text-muted">
              {effectiveRole !== "ALL" ? <span>{roleDisplayLabel(effectiveRole)}</span> : null}
              {effectivePatch ? <span>Patch {effectivePatch}</span> : null}
              <span>{activeRegionLabel}</span>
              {recentMatches.length > 0 ? (
                <span className="type-tabular tabular-nums">
                  {formatGames(recentMatches.length)} recent {plural(recentMatches.length, "game")} from{" "}
                  {topPlayers.length} {SCOPE_NOUN[effectiveScope]}
                </span>
              ) : null}
            </p>
          </div>
          <Link
            href={`/lol/champions/${championId}`}
            className={buttonClassName({ variant: "outline", size: "sm" })}
          >
            {championName} stats
          </Link>
        </div>
        <div className="mt-4 flex flex-wrap items-center gap-2 border-t border-border/70 pt-3">
          <RoleFilterTabs
            roles={LANE_ROLES}
            activeRole={effectiveRole}
            baseHref={`/lol/pro-builds/${championId}`}
            extraParams={roleExtraParams}
          />
          <div className="flex flex-wrap items-center gap-2 lg:ml-auto">
            <AnalyticsRegionFilter options={regionOptions} activeRegion={activeRegion} variant="select" />
            <AnalyticsPatchFilter patches={patchOptions} activePatch={patchFilter} />
            <ProScopeToggle
              checked={includeOneTricks}
              href={buildProBuildPageHref(championId, {
                role: effectiveRole,
                region: activeRegion,
                scope: includeOneTricks ? "pro" : "all",
                patch: patchFilter
              })}
            />
          </div>
        </div>
      </header>

      {!proBuildsRes.ok ? (
        <section className="page-panel p-5">
          <h2 className="type-section">Pro-build data unavailable</h2>
          <p className="type-ui mt-2 text-fg/80">{proFeedErrorMessage(proBuildsRes)}</p>
          <p className="type-caption mt-1 text-muted">Try another role or region, or check back soon.</p>
          {proBuildsRes.requestId ? (
            <p className="type-caption mt-2 text-muted">
              Request ID: <code className="text-fg/65">{proBuildsRes.requestId}</code>
            </p>
          ) : null}
        </section>
      ) : recentMatches.length === 0 && !coreBuild ? (
        <section className="page-panel p-5">
          <h2 className="type-section">No tracked games yet</h2>
          <p className="type-ui mt-2 text-muted">
            No tracked {SCOPE_NOUN[effectiveScope]} have played {championName}
            {effectiveRole !== "ALL" ? ` ${roleDisplayLabel(effectiveRole).toLowerCase()}` : ""} in this scope.
            {includeOneTricks ? " Try another role or region." : " Include one-tricks or try another role."}
          </p>
        </section>
      ) : (
        <>
          <section aria-labelledby="pro-build-heading" className="page-panel overflow-hidden">
            <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1 px-4 py-4 sm:px-5">
              <h2 id="pro-build-heading" className="type-title text-fg">
                What pros build
              </h2>
              <p className="type-caption text-muted">The most common choice in each slot</p>
            </div>
            <div className="grid gap-px border-t border-border bg-border sm:grid-cols-2 xl:grid-cols-[1.4fr_1.2fr_1fr_1fr]">
              <BuildCell
                label="Core items"
                footer={coreBuild ? gamesLine(coreBuild.games, coreBuild.winRate) : null}
              >
                {coreBuild ? (
                  <ItemStrip itemIds={coreBuild.items} itemStatic={itemStatic} size={40} className="gap-1.5" />
                ) : (
                  <p className="type-ui text-muted">No repeat item path yet.</p>
                )}
              </BuildCell>

              <BuildCell
                label="Runes"
                footer={runeChoice ? shareOf(runeChoice.count, recentMatches.length) : null}
              >
                {runeChoice ? (
                  <RuneSummary match={runeChoice.entry} runeStatic={runeStatic} />
                ) : (
                  <p className="type-ui text-muted">No rune data.</p>
                )}
              </BuildCell>

              <BuildCell
                label="Summoner spells"
                footer={spellChoice ? shareOf(spellChoice.count, recentMatches.length) : null}
              >
                {spellChoice ? (
                  <div className="flex items-center gap-3">
                    <SpellPair
                      spellIds={[spellChoice.entry.spell1Id, spellChoice.entry.spell2Id]}
                      spellStatic={spellStatic}
                      size={36}
                      className="gap-1.5"
                    />
                    <span className="type-ui min-w-0 font-semibold text-fg">
                      {[spellChoice.entry.spell1Id, spellChoice.entry.spell2Id]
                        .map((id) => spellStatic.spells[String(id ?? 0)]?.name)
                        .filter(Boolean)
                        .join(" + ")}
                    </span>
                  </div>
                ) : (
                  <p className="type-ui text-muted">No spell data.</p>
                )}
              </BuildCell>

              <BuildCell
                label="Skill max order"
                footer={skillChoice ? shareOf(skillChoice.count, recentMatches.length) : null}
              >
                {skillChoice ? (
                  <SkillOrder maxOrder={skillChoice.entry.skillOrder?.maxOrder} />
                ) : (
                  <p className="type-ui text-muted">No skill data.</p>
                )}
              </BuildCell>
            </div>
          </section>

          <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start">
            <section aria-labelledby="pro-games-heading" className="page-panel overflow-hidden">
              <div className="px-4 py-4 sm:px-5">
                <h2 id="pro-games-heading" className="type-title text-fg">
                  Recent games
                </h2>
              </div>
              {recentMatches.length === 0 ? (
                <p className="border-t border-border px-5 py-6 text-sm text-muted">
                  No recent tracked games with these filters.
                </p>
              ) : (
                <ul className="border-t border-border">
                  {recentMatches.map((match, idx) => (
                    <RecentGameRow
                      key={`${match.matchId ?? "match"}-${match.playerName ?? "player"}-${idx}`}
                      match={match}
                      itemStatic={itemStatic}
                      runeStatic={runeStatic}
                      spellStatic={spellStatic}
                    />
                  ))}
                </ul>
              )}
            </section>

            <aside className="grid gap-4">
              <section aria-labelledby="pro-players-heading" className="page-panel overflow-hidden">
                <h2 id="pro-players-heading" className="type-section px-4 py-3.5 text-fg sm:px-5">
                  Who plays it
                </h2>
                {topPlayers.length === 0 ? (
                  <p className="border-t border-border px-5 py-4 text-sm text-muted">No tracked players yet.</p>
                ) : (
                  <ul className="border-t border-border">
                    {topPlayers.map((player, idx) => (
                      <li
                        key={`${player.playerName ?? "player"}-${idx}`}
                        className="flex max-w-none items-center justify-between gap-3 border-b border-border/50 px-4 py-2.5 last:border-b-0 sm:px-5"
                      >
                        <span className="grid min-w-0">
                          <span className="truncate font-semibold text-fg">
                            {player.playerName ?? "Unknown player"}
                          </span>
                          <span className="type-caption truncate text-muted">
                            {formatGames(player.games)} {plural(player.games, "game")}
                            {player.teamName ? ` · ${player.teamName}` : ""}
                          </span>
                        </span>
                        <DataBar value={player.winRate} games={player.games} />
                      </li>
                    ))}
                  </ul>
                )}
              </section>

              {otherBuilds.length > 0 ? (
                <section aria-labelledby="pro-other-builds-heading" className="page-panel overflow-hidden">
                  <h2 id="pro-other-builds-heading" className="type-section px-4 py-3.5 text-fg sm:px-5">
                    Other item paths
                  </h2>
                  <ul className="border-t border-border">
                    {otherBuilds.slice(0, 5).map((build) => (
                      <li
                        key={itemSetKey(build.items)}
                        className="grid max-w-none gap-1.5 border-b border-border/50 px-4 py-3 last:border-b-0 sm:px-5"
                      >
                        <ItemStrip itemIds={build.items} itemStatic={itemStatic} size={28} />
                        <span className="type-caption text-muted">{gamesLine(build.games, build.winRate)}</span>
                      </li>
                    ))}
                  </ul>
                </section>
              ) : null}
            </aside>
          </div>
        </>
      )}
    </div>
  );
}

function RuneSummary({ match, runeStatic }: { match: ProMatchBuildDto; runeStatic: RuneStaticData }) {
  const primary = sortRuneSelections([...(match.primaryRunes ?? [])], runeStatic.runeSortById);
  const secondary = sortRuneSelections([...(match.subRunes ?? [])], runeStatic.runeSortById);
  const keystone = runeStatic.runeById[String(primary[0] ?? 0)];
  const subStyle = runeStatic.styleById[String(match.subStyleId ?? 0)];
  return (
    <div className="grid gap-2.5">
      <div className="flex items-center gap-3">
        {keystone ? (
          <Image
            src={runeIconUrl(keystone.icon)}
            alt=""
            width={40}
            height={40}
            sizes="40px"
            className="shrink-0 rounded-full bg-surface-2/70"
          />
        ) : null}
        <span className="grid min-w-0">
          <span className="type-ui truncate font-semibold text-fg">{keystone?.name ?? "Unknown keystone"}</span>
          {subStyle ? <span className="type-caption truncate text-muted">with {subStyle.name}</span> : null}
        </span>
      </div>
      <div className="flex items-center gap-1">
        {primary.slice(1, 4).map((id) => (
          <RuneIcon key={`p-${id}`} id={id} runeStatic={runeStatic} size={24} />
        ))}
        {secondary.length > 0 ? <span aria-hidden className="mx-1 h-4 w-px bg-border" /> : null}
        {secondary.slice(0, 2).map((id) => (
          <RuneIcon key={`s-${id}`} id={id} runeStatic={runeStatic} size={24} />
        ))}
      </div>
    </div>
  );
}

function RecentGameRow({
  match,
  itemStatic,
  runeStatic,
  spellStatic
}: {
  match: ProMatchBuildDto;
  itemStatic: Awaited<ReturnType<typeof fetchItemMap>>;
  runeStatic: RuneStaticData;
  spellStatic: Awaited<ReturnType<typeof fetchSummonerSpellMap>>;
}) {
  const playedAt = match.playedAt ?? 0;
  const hasTimestamp = Number.isFinite(playedAt) && playedAt > 0;
  return (
    <li className="flex max-w-none flex-wrap items-center gap-x-4 gap-y-2 border-b border-border/50 px-4 py-3 last:border-b-0 sm:px-5">
      <span className={cn("type-ui w-9 shrink-0 font-semibold", match.win ? "text-win" : "text-loss")}>
        {match.win ? "Win" : "Loss"}
      </span>
      <span className="grid min-w-0 flex-1 basis-36">
        <span className="flex min-w-0 items-baseline gap-1.5">
          <span className="truncate font-semibold text-fg">{match.playerName ?? "Unknown player"}</span>
          {match.teamName ? <span className="type-caption truncate text-muted">{match.teamName}</span> : null}
        </span>
        <span className="type-caption text-muted">
          {hasTimestamp ? (
            <time title={formatDateTimeMs(playedAt)}>{formatRelativeTime(playedAt)}</time>
          ) : (
            "Time unavailable"
          )}
        </span>
      </span>
      <span className="flex shrink-0 items-center gap-1.5">
        <RunePair primaryRunes={match.primaryRunes} subStyleId={match.subStyleId} runeStatic={runeStatic} size={24} />
        <SpellPair spellIds={[match.spell1Id, match.spell2Id]} spellStatic={spellStatic} size={24} />
      </span>
      {/* A fixed slot for six items keeps the skill column aligned row to row. */}
      <span className="shrink-0 sm:w-[11.75rem]">
        <ItemStrip itemIds={match.items} itemStatic={itemStatic} size={28} />
      </span>
      <span className="hidden shrink-0 xl:block">
        <SkillOrder maxOrder={match.skillOrder?.maxOrder} size="sm" />
      </span>
    </li>
  );
}
