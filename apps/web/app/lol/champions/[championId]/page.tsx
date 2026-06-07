import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { ChampionPortrait } from "@/components/ChampionPortrait";
import { FilterBar } from "@/components/FilterBar";
import { ItemBuildDisplay } from "@/components/ItemBuildDisplay";
import { RuneTreeDisplay } from "@/components/RuneTreeDisplay";
import { StatsBar } from "@/components/StatsBar";
import { TierBadge } from "@/components/TierBadge";
import { WinRateText } from "@/components/WinRateText";
import { Card } from "@/components/ui/Card";
import { DataBar } from "@/components/ui/DataBar";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { pickMostSevereAnalyticsSample, type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { formatGames, formatPercent } from "@/lib/format";
import { fetchLolAnalyticsPatches } from "@/lib/lolAnalyticsPatches";
import { normalizeAnalyticsPatch } from "@/lib/lolPatchFilters";
import { resolveDefaultedRankTier, rankTierDisplayLabel } from "@/lib/ranks";
import { roleDisplayLabel } from "@/lib/roles";
import {
  championIconUrl,
  fetchChampionMap,
  fetchItemMap,
  fetchRunesReforged
} from "@/lib/staticData";
import { deriveTier } from "@/lib/tierlist";

type ChampionWinRateDto = components["schemas"]["ChampionWinRateDto"];
type ChampionWinRateSummary = components["schemas"]["ChampionWinRateSummary"];
type ChampionProfileAnalyticsResponse = components["schemas"]["ChampionProfileAnalyticsResponse"];
type MatchupEntryDto = components["schemas"]["MatchupEntryDto"];

const ROLES = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"] as const;

function matchupVerdict(winRate: number | null | undefined): string {
  const pct = (winRate ?? 0) * 100;
  if (pct >= 52) return "Favored";
  if (pct < 48) return "Unfavored";
  return "Even";
}

function normalizeRole(role: string | null | undefined) {
  if (!role) return null;
  const upper = role.toUpperCase();
  return ROLES.includes(upper as (typeof ROLES)[number]) ? upper : null;
}

function pickMostPlayedRole(summary: ChampionWinRateSummary | null) {
  if (!summary?.byRoleTier?.length) return null;
  const gamesByRole = new Map<string, number>();
  for (const entry of summary.byRoleTier ?? []) {
    if (!entry.role) continue;
    const role = entry.role.toUpperCase();
    gamesByRole.set(role, (gamesByRole.get(role) ?? 0) + (entry.games ?? 0));
  }
  const sorted = [...gamesByRole.entries()].sort((a, b) => b[1] - a[1]);
  if (sorted.length === 0) return null;
  const candidate = sorted[0][0];
  return normalizeRole(candidate);
}

function pickBestEntry(
  winrates: ChampionWinRateSummary | null,
  role: string
): ChampionWinRateDto | null {
  if (!winrates?.byRoleTier?.length) return null;
  const forRole = (winrates.byRoleTier ?? []).filter(
    (e) => (e.role ?? "").toUpperCase() === role.toUpperCase()
  );
  if (forRole.length === 0) return null;
  return forRole.reduce((best, cur) => ((cur.games ?? 0) > (best.games ?? 0) ? cur : best));
}

export default async function ChampionDetailPage({
  params,
  searchParams
}: {
  params: Promise<{ championId: string }>;
  searchParams?: Promise<{ role?: string; rankTier?: string; region?: string; patch?: string; sort?: string }>;
}) {
  const resolvedParams = await params;
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const { activeRegion, activeRegionLabel, options: regionOptions } = await resolveAnalyticsRegion(
    resolvedSearchParams?.region
  );
  const championId = Number(resolvedParams.championId);
  if (!Number.isFinite(championId) || championId <= 0) {
    return (
      <BackendErrorCard
        title="Champion"
        message="Invalid champion id."
      />
    );
  }

  const explicitRole = normalizeRole(resolvedSearchParams?.role);
  // Champion pages default to Emerald+ unless a rank is in the URL; an explicit
  // ?rankTier=all resolves to null (all ranks). See resolveDefaultedRankTier.
  const normalizedRankTier = resolveDefaultedRankTier(resolvedSearchParams?.rankTier);
  const selectedPatch = normalizeAnalyticsPatch(resolvedSearchParams?.patch);
  const winrateQuery = new URLSearchParams();
  if (normalizedRankTier) winrateQuery.set("rankTier", normalizedRankTier);
  if (activeRegion !== "ALL") winrateQuery.set("region", activeRegion);
  if (selectedPatch) winrateQuery.set("patch", selectedPatch);
  if (explicitRole) winrateQuery.set("role", explicitRole);
  const profileQuery = winrateQuery.toString() ? `?${winrateQuery.toString()}` : "";

  const verbosity = getErrorVerbosity();
  const [staticData, itemStatic, runeStatic, patchOptions, profileRes] = await Promise.all([
    fetchChampionMap(),
    fetchItemMap(),
    fetchRunesReforged(),
    fetchLolAnalyticsPatches(),
    fetchBackendJson<ChampionProfileAnalyticsResponse>(
      `${getBackendBaseUrl()}/api/lol/analytics/champions/${championId}/profile${profileQuery}`,
      { next: { revalidate: 60 * 60 } }
    )
  ]);

  const { version, champions } = staticData;
  const champ = champions[String(championId)];
  const champName = champ?.name ?? `Champion ${championId}`;
  const champSlug = champ?.id ?? "Unknown";
  const itemVersion = itemStatic.version;
  const items = itemStatic.items;
  const runeById = runeStatic.runeById;
  const styleById = runeStatic.styleById;
  const runeTrees = runeStatic.trees;

  if (!profileRes.ok) {
    const kind = profileRes.errorKind;
    return (
      <BackendErrorCard
        title={champName}
        message={
          kind === "timeout"
            ? "This page is taking too long to load."
            : kind === "unreachable"
              ? "We couldn't load champion data right now."
              : "We couldn't load champion data."
        }
        requestId={profileRes.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify(
                {
                  profile: { status: profileRes.status, errorKind: profileRes.errorKind }
                },
                null,
                2
              )
            : null
        }
      />
    );
  }

  const profile = profileRes.body!;
  const winrates = profile.winRates ?? null;
  const builds = profile.builds ?? null;
  const matchups = profile.matchups ?? null;
  const effectiveRole = normalizeRole(profile.effectiveRole) ?? explicitRole ?? pickMostPlayedRole(winrates) ?? "MIDDLE";
  const winrateRows = winrates?.byRoleTier ?? [];
  const buildRows = builds?.builds ?? [];
  const globalCoreItems = builds?.globalCoreItems ?? [];
  const counters = matchups?.counters ?? [];
  const favorableMatchups = matchups?.favorableMatchups ?? [];
  const matchupSortKey = resolvedSearchParams?.sort === "games" ? "games" : "winRate";
  const allMatchups = [...counters, ...favorableMatchups]
    .filter((m): m is MatchupEntryDto => Boolean(m?.opponentChampionId))
    .filter(
      (entry, idx, rows) =>
        rows.findIndex((candidate) => candidate.opponentChampionId === entry.opponentChampionId) === idx
    )
    .sort((a, b) =>
      matchupSortKey === "games"
        ? (b.games ?? 0) - (a.games ?? 0)
        : (a.winRate ?? 0) - (b.winRate ?? 0)
    );
  const buildMatchupSortHref = (sort: string) => {
    const sortParams = new URLSearchParams({ role: effectiveRole, sort });
    if (normalizedRankTier) sortParams.set("rankTier", normalizedRankTier);
    if (activeRegion !== "ALL") sortParams.set("region", activeRegion);
    if (selectedPatch) sortParams.set("patch", selectedPatch);
    return `/lol/champions/${championId}?${sortParams.toString()}#matchups`;
  };
  const heroEntry = pickBestEntry(winrates, effectiveRole);
  const heroTier = deriveTier(heroEntry?.winRate);
  const splashUrl = `https://ddragon.leagueoflegends.com/cdn/img/champion/splash/${champSlug}_0.jpg`;
  const sampleNotice = pickMostSevereAnalyticsSample(
    (winrates as { sample?: unknown } | null)?.sample as AnalyticsSampleLike,
    (builds as { sample?: unknown } | null)?.sample as AnalyticsSampleLike,
    (matchups as { sample?: unknown } | null)?.sample as AnalyticsSampleLike
  );
  const linkParams = new URLSearchParams();
  if (normalizedRankTier) linkParams.set("rankTier", normalizedRankTier);
  if (activeRegion !== "ALL") linkParams.set("region", activeRegion);
  if (selectedPatch) linkParams.set("patch", selectedPatch);
  const linkQuery = linkParams.toString();
  const sharedFilterParams = selectedPatch ? { patch: selectedPatch } : {};

  return (
    <div className="grid gap-8">
      {/* ── Champion Header ── */}
      <header className="page-hero relative p-5 md:p-8">
        <div
          className="pointer-events-none absolute inset-0 opacity-30"
          style={{
            backgroundImage: `linear-gradient(to right, var(--t-bg) 20%, color-mix(in oklch, var(--t-bg), transparent 18%) 45%, transparent 100%), url(${splashUrl})`,
            backgroundSize: "cover",
            backgroundPosition: "top right"
          }}
        />

        <div className="relative flex flex-col gap-5">
        <div className="flex items-start gap-3 sm:items-center sm:gap-4">
          <Image
            src={championIconUrl(version, champSlug)}
            alt={champName}
            width={64}
            height={64}
            className="h-12 w-12 rounded-xl border border-border/60 sm:h-16 sm:w-16"
          />
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="type-page-title">
                {champName}
              </h1>
              <TierBadge tier={heroTier} size="md" />
            </div>
            {champ?.title ? <p className="mt-0.5 text-xs uppercase tracking-wide text-muted">{champ.title}</p> : null}
            <p className="mt-0.5 text-sm text-muted">
              {roleDisplayLabel(effectiveRole)} &middot; {rankTierDisplayLabel(normalizedRankTier ?? "all")} &middot; {activeRegionLabel}
            </p>
            <div className="mt-2.5 flex flex-wrap items-center gap-2 text-xs">
              <Link
                href="#matchups"
                className="rounded-lg border border-border/60 bg-surface-2/50 px-2.5 py-1 font-medium text-fg/80 transition-colors hover:bg-surface-2/80"
              >
                Matchups
              </Link>
              <Link
                href={`/lol/pro-builds/${championId}${linkQuery ? `?${linkQuery}` : ""}`}
                className="rounded-lg border border-primary/40 bg-primary/10 px-2.5 py-1 font-medium text-primary transition-colors hover:bg-primary/20"
              >
                Pro Builds
              </Link>
            </div>
          </div>
        </div>

        {/* ── Stats Bar ── */}
        <StatsBar
          tier={heroTier}
          winRate={heroEntry?.winRate}
          pickRate={heroEntry?.pickRate}
          games={heroEntry?.games}
        />

        {/* ── Filters ── */}
        <FilterBar
          roles={ROLES}
          activeRole={effectiveRole}
          activeRank={normalizedRankTier ?? "all"}
          regionOptions={regionOptions}
          activeRegion={activeRegion}
          patchOptions={patchOptions}
          activePatch={selectedPatch}
          extraParams={sharedFilterParams}
          explicitAllRank
          baseHref={`/lol/champions/${championId}`}
        />
        <div className="mt-3">
          <AnalyticsSampleBanner sample={sampleNotice} />
        </div>
        </div>
      </header>

      {/* ── Win Rates Table ── */}
      <Card className="p-5">
        <h2 className="type-section">
          Win Rates
        </h2>
        {!winrates ? (
          <div className="mt-2">
            <p className="text-sm text-fg/75">Win-rate data is unavailable right now.</p>
            <p className="mt-1 text-xs text-muted">Try selecting a different region or check back after patch data has been processed.</p>
          </div>
        ) : winrateRows.length === 0 ? (
          <p className="mt-2 text-sm text-fg/75">No games have been added for the selected patch yet.</p>
        ) : (
          <div className="mt-4 overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="type-overline text-muted">
                <tr className="border-b border-border/30">
                  <th className="py-2 pr-4">Role</th>
                  <th className="py-2 pr-4">Tier</th>
                  <th className="py-2 pr-4 text-right">Win Rate</th>
                  <th className="py-2 pr-4 text-right">Pick Rate</th>
                  <th className="py-2 pr-4 text-right">Games</th>
                </tr>
              </thead>
              <tbody>
                {winrateRows
                  .slice()
                  .sort((a, b) => (b.games ?? 0) - (a.games ?? 0))
                  .map((w) => (
                    <tr
                      key={`${w.role ?? "ALL"}-${w.rankTier ?? "all"}`}
                      className="border-t border-border/40 transition hover:bg-surface-2/40"
                    >
                      <td className="py-2.5 pr-4 font-medium">
                        {roleDisplayLabel(w.role ?? "ALL")}
                      </td>
                      <td className="py-2.5 pr-4 text-muted">
                        {rankTierDisplayLabel(w.rankTier ?? "all")}
                      </td>
                      <td className="py-2.5 pr-4 text-right">
                        <DataBar value={w.winRate} decimals={2} className="justify-end" />
                      </td>
                      <td className="type-tabular py-2.5 pr-4 text-right tabular-nums text-fg/70">
                        {formatPercent(w.pickRate, { decimals: 1 })}
                      </td>
                      <td className="type-tabular py-2.5 pr-4 text-right tabular-nums text-fg/70">
                        {formatGames(w.games)}
                      </td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* ── Builds + Matchups ── */}
      <div className="grid gap-6 md:grid-cols-2">
        {/* ── Builds ── */}
        <Card className="p-5" id="builds">
          <h2 className="type-section">
            Builds
          </h2>
          {!builds ? (
            <div className="mt-2">
              <p className="text-sm text-fg/75">Build data is unavailable right now.</p>
              <p className="mt-1 text-xs text-muted">Try selecting a different region or check back after patch data has been processed.</p>
            </div>
          ) : buildRows.length === 0 ? (
            <p className="mt-2 text-sm text-fg/75">There are not enough games for this role yet.</p>
          ) : (
            <div className="mt-4 grid gap-4">
              {/* Global Core Items */}
              {globalCoreItems.length > 0 ? (
                <ItemBuildDisplay
                  allItems={[]}
                  coreItems={globalCoreItems}
                  situationalItems={[]}
                  version={itemVersion}
                  items={items}
                />
              ) : null}

              {buildRows.map((b, idx) => (
                <div
                  key={idx}
                  className="rounded-lg border border-border/60 bg-surface-2/40 p-3"
                >
                  <div className="flex items-center justify-between">
                    <p className="text-sm font-semibold text-fg">
                      {idx === 0 ? "Recommended Build" : `Alternative ${idx}`}
                    </p>
                    <p className="text-xs text-muted">
                      <WinRateText value={b.winRate} decimals={1} games={b.games} />
                    </p>
                  </div>

                  {/* Items: Core + Situational */}
                  <div className="mt-3">
                    <ItemBuildDisplay
                      allItems={b.items ?? []}
                      coreItems={b.coreItems ?? []}
                      situationalItems={b.situationalItems ?? []}
                      version={itemVersion}
                      items={items}
                      winRate={b.winRate}
                      games={b.games}
                    />
                  </div>

                  {/* Runes */}
                  <div className="mt-3 border-t border-border/40 pt-3">
                    <p className="mb-2 text-xs font-medium text-muted">Runes</p>
                    <RuneTreeDisplay
                      primaryStyleId={b.primaryStyleId ?? 0}
                      subStyleId={b.subStyleId ?? 0}
                      primarySelections={b.primaryRunes ?? []}
                      subSelections={b.subRunes ?? []}
                      statShards={b.statShards ?? []}
                      trees={runeTrees}
                      runeById={runeById}
                      styleById={styleById}
                      iconSize={22}
                    />
                  </div>
                </div>
              ))}
            </div>
          )}
        </Card>

        {/* ── Matchups ── */}
        <Card className="p-5">
          <h2 className="type-section">
            Matchups
          </h2>
          {!matchups ? (
            <div className="mt-2">
              <p className="text-sm text-fg/75">Matchup data is unavailable right now.</p>
              <p className="mt-1 text-xs text-muted">Try selecting a different region or check back after patch data has been processed.</p>
            </div>
          ) : (
            <div className="mt-4 grid gap-5">
              {/* Toughest Matchups */}
              <div>
                <p className="text-sm font-semibold text-fg">
                  Toughest Matchups
                </p>
                <p className="mt-0.5 text-xs text-muted">
                  These champions counter {champName}
                </p>
                {counters.length === 0 ? (
                  <p className="mt-2 text-xs text-muted">No strong counters found.</p>
                ) : (
                  <ul className="mt-2 grid gap-1.5 text-sm">
                    {counters.map((m, idx) => {
                      const opponentChampionId = m.opponentChampionId ?? 0;
                      const opp = champions[String(opponentChampionId)];
                      return (
                        <li
                          key={`${opponentChampionId}-${idx}`}
                          className="flex items-center justify-between rounded-md border border-border/50 bg-surface-2/40 px-3 py-2"
                        >
                          <Link
                            href={`/lol/champions/${opponentChampionId}${linkQuery ? `?${linkQuery}` : ""}`}
                            className="flex min-w-0 items-center gap-2 hover:underline"
                          >
                            <ChampionPortrait
                              championSlug={opp?.id ?? "Unknown"}
                              championName={opp?.name ?? `Champion ${opponentChampionId}`}
                              version={version}
                              size={24}
                              showName
                              className="min-w-0"
                            />
                          </Link>
                          <span className="shrink-0">
                            <DataBar value={m.winRate} decimals={1} />
                          </span>
                        </li>
                      );
                    })}
                  </ul>
                )}
              </div>

              {/* Best Matchups */}
              <div>
                <p className="text-sm font-semibold text-fg">
                  Best Matchups
                </p>
                <p className="mt-0.5 text-xs text-muted">
                  {champName} performs well against these champions
                </p>
                {favorableMatchups.length === 0 ? (
                  <p className="mt-2 text-xs text-muted">
                    No strong favorable matchups found.
                  </p>
                ) : (
                  <ul className="mt-2 grid gap-1.5 text-sm">
                    {favorableMatchups.map((m, idx) => {
                      const opponentChampionId = m.opponentChampionId ?? 0;
                      const opp = champions[String(opponentChampionId)];
                      return (
                        <li
                          key={`${opponentChampionId}-${idx}`}
                          className="flex items-center justify-between rounded-md border border-border/50 bg-surface-2/40 px-3 py-2"
                        >
                          <Link
                            href={`/lol/champions/${opponentChampionId}${linkQuery ? `?${linkQuery}` : ""}`}
                            className="flex min-w-0 items-center gap-2 hover:underline"
                          >
                            <ChampionPortrait
                              championSlug={opp?.id ?? "Unknown"}
                              championName={opp?.name ?? `Champion ${opponentChampionId}`}
                              version={version}
                              size={24}
                              showName
                              className="min-w-0"
                            />
                          </Link>
                          <span className="shrink-0">
                            <DataBar value={m.winRate} decimals={1} />
                          </span>
                        </li>
                      );
                    })}
                  </ul>
                )}
              </div>
            </div>
          )}
        </Card>
      </div>

      {/* ── All Matchups (full table) ── */}
      <Card className="p-5" id="matchups">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="type-section">All Matchups</h2>
            <p className="mt-1 text-xs text-muted">
              Lane matchups for {champName} as {roleDisplayLabel(effectiveRole)}
            </p>
          </div>
          <div className="flex items-center gap-2 text-xs">
            <Link
              href={buildMatchupSortHref("winRate")}
              scroll={false}
              className="control-tab type-ui px-3 py-2"
              data-active={matchupSortKey === "winRate"}
            >
              Sort by Win Rate
            </Link>
            <Link
              href={buildMatchupSortHref("games")}
              scroll={false}
              className="control-tab type-ui px-3 py-2"
              data-active={matchupSortKey === "games"}
            >
              Sort by Games
            </Link>
          </div>
        </div>
        <div className="mt-4 overflow-x-auto">
          <table className="w-full min-w-[640px] text-left text-sm">
            <thead className="type-overline text-muted">
              <tr className="border-b border-border/30">
                <th className="py-2 pr-4">Opponent</th>
                <th className="py-2 pr-4 text-right">Win Rate</th>
                <th className="py-2 pr-4 text-right">Games</th>
                <th className="py-2 pr-4 text-right">Verdict</th>
              </tr>
            </thead>
            <tbody>
              {allMatchups.length === 0 ? (
                <tr>
                  <td colSpan={4} className="py-4 text-sm text-muted">
                    No matchup data is available for the selected filters yet.
                  </td>
                </tr>
              ) : (
                allMatchups.map((entry, idx) => {
                  const opponentId = entry.opponentChampionId ?? 0;
                  const opponent = champions[String(opponentId)];
                  const verdict = matchupVerdict(entry.winRate);
                  const verdictClass =
                    verdict === "Favored"
                      ? "text-win"
                      : verdict === "Unfavored"
                        ? "text-loss"
                        : "text-muted";
                  return (
                    <tr key={`${opponentId}-${idx}`} className="border-b border-border/40 transition hover:bg-surface-2/40">
                      <td className="py-2.5 pr-4">
                        <Link
                          href={`/lol/champions/${opponentId}${linkQuery ? `?${linkQuery}` : ""}`}
                          className="hover:underline"
                        >
                          <ChampionPortrait
                            championSlug={opponent?.id ?? "Unknown"}
                            championName={opponent?.name ?? `Champion ${opponentId}`}
                            version={version}
                            size={24}
                            showName
                          />
                        </Link>
                      </td>
                      <td className="py-2.5 pr-4 text-right">
                        <DataBar value={entry.winRate} decimals={1} className="justify-end" />
                      </td>
                      <td className="type-tabular py-2.5 pr-4 text-right tabular-nums text-fg/70">
                        {formatGames(entry.games)}
                      </td>
                      <td className={`py-2.5 pr-4 text-right font-medium ${verdictClass}`}>{verdict}</td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  );
}
