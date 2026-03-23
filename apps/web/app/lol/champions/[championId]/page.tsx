import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { ChampionPortrait } from "@/components/ChampionPortrait";
import { FilterBar } from "@/components/FilterBar";
import { ItemBuildDisplay } from "@/components/ItemBuildDisplay";
import { RuneSetupDisplay } from "@/components/RuneSetupDisplay";
import { StatsBar } from "@/components/StatsBar";
import { TierBadge } from "@/components/TierBadge";
import { WinRateText } from "@/components/WinRateText";
import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { pickMostSevereAnalyticsSample, type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { formatGames, formatPercent } from "@/lib/format";
import { normalizeRankTierParam, rankTierDisplayLabel } from "@/lib/ranks";
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
type ChampionBuildsResponse = components["schemas"]["ChampionBuildsResponse"];
type ChampionMatchupsResponse = components["schemas"]["ChampionMatchupsResponse"];

const ROLES = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"] as const;

function normalizeRole(role: string | undefined) {
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
  searchParams?: Promise<{ role?: string; rankTier?: string; region?: string }>;
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
  const normalizedRankTier = normalizeRankTierParam(resolvedSearchParams?.rankTier);
  const winrateQuery = new URLSearchParams();
  if (normalizedRankTier) winrateQuery.set("rankTier", normalizedRankTier);
  if (activeRegion !== "ALL") winrateQuery.set("region", activeRegion);
  const qsTier = winrateQuery.toString() ? `?${winrateQuery.toString()}` : "";

  const verbosity = getErrorVerbosity();
  const [staticData, itemStatic, runeStatic, winRes] = await Promise.all([
    fetchChampionMap(),
    fetchItemMap(),
    fetchRunesReforged(),
    fetchBackendJson<ChampionWinRateSummary>(
      `${getBackendBaseUrl()}/api/lol/analytics/champions/${championId}/winrates${qsTier}`,
      { next: { revalidate: 60 * 60 } }
    )
  ]);

  const winrates = winRes.ok ? winRes.body! : null;
  let fallbackWinrates: ChampionWinRateSummary | null = null;

  if (
    !explicitRole &&
    normalizedRankTier &&
    (!winrates || (winrates.byRoleTier?.length ?? 0) === 0)
  ) {
    const fallbackQuery = new URLSearchParams();
    if (activeRegion !== "ALL") fallbackQuery.set("region", activeRegion);
    const fallbackWinRes = await fetchBackendJson<ChampionWinRateSummary>(
      `${getBackendBaseUrl()}/api/lol/analytics/champions/${championId}/winrates${fallbackQuery.toString() ? `?${fallbackQuery.toString()}` : ""}`,
      { next: { revalidate: 60 * 60 } }
    );
    fallbackWinrates = fallbackWinRes.ok ? fallbackWinRes.body! : null;
  }

  const effectiveRole =
    explicitRole ??
    pickMostPlayedRole(winrates) ??
    pickMostPlayedRole(fallbackWinrates) ??
    "MIDDLE";

  const buildMatchupQuery = new URLSearchParams({ role: effectiveRole });
  if (normalizedRankTier) buildMatchupQuery.set("rankTier", normalizedRankTier);
  if (activeRegion !== "ALL") buildMatchupQuery.set("region", activeRegion);

  const [buildRes, matchupRes] = await Promise.all([
    fetchBackendJson<ChampionBuildsResponse>(
      `${getBackendBaseUrl()}/api/lol/analytics/champions/${championId}/builds?${buildMatchupQuery.toString()}`,
      { next: { revalidate: 60 * 60 } }
    ),
    fetchBackendJson<ChampionMatchupsResponse>(
      `${getBackendBaseUrl()}/api/lol/analytics/champions/${championId}/matchups?${buildMatchupQuery.toString()}`,
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
  const runeSortById = runeStatic.runeSortById;

  if (!winRes.ok && !buildRes.ok && !matchupRes.ok) {
    const requestId = winRes.requestId || buildRes.requestId || matchupRes.requestId;
    const kind = winRes.errorKind ?? buildRes.errorKind ?? matchupRes.errorKind;
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
        requestId={requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify(
                {
                  winrates: { status: winRes.status, errorKind: winRes.errorKind },
                  builds: { status: buildRes.status, errorKind: buildRes.errorKind },
                  matchups: { status: matchupRes.status, errorKind: matchupRes.errorKind }
                },
                null,
                2
              )
            : null
        }
      />
    );
  }

  const builds = buildRes.ok ? buildRes.body! : null;
  const matchups = matchupRes.ok ? matchupRes.body! : null;
  const winrateRows = winrates?.byRoleTier ?? [];
  const buildRows = builds?.builds ?? [];
  const globalCoreItems = builds?.globalCoreItems ?? [];
  const counters = matchups?.counters ?? [];
  const favorableMatchups = matchups?.favorableMatchups ?? [];
  const heroEntry = pickBestEntry(winrates, effectiveRole);
  const heroTier = deriveTier(heroEntry?.winRate);
  const splashUrl = `https://ddragon.leagueoflegends.com/cdn/img/champion/splash/${champSlug}_0.jpg`;
  const sampleNotice = pickMostSevereAnalyticsSample(
    (winrates as { sample?: unknown } | null)?.sample as AnalyticsSampleLike,
    (builds as { sample?: unknown } | null)?.sample as AnalyticsSampleLike,
    (matchups as { sample?: unknown } | null)?.sample as AnalyticsSampleLike
  );

  return (
    <div className="grid gap-8">
      {/* ── Champion Header ── */}
      <header className="page-hero relative p-5 md:p-8">
        <div
          className="pointer-events-none absolute inset-0 opacity-30"
          style={{
            backgroundImage: `linear-gradient(to right, hsl(var(--bg)) 20%, hsl(var(--bg) / 0.82) 45%, transparent 100%), url(${splashUrl})`,
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
              <h1 className="type-title sm:text-[2.35rem]">
                {champName}
              </h1>
              <TierBadge tier={heroTier} size="md" />
            </div>
            {champ?.title ? <p className="mt-0.5 text-xs uppercase tracking-wide text-muted">{champ.title}</p> : null}
            <p className="mt-0.5 text-sm text-muted">
              {roleDisplayLabel(effectiveRole)} &middot; {rankTierDisplayLabel(normalizedRankTier ?? "all")} &middot; {activeRegionLabel}
            </p>
            <div className="mt-2 flex flex-wrap items-center gap-2 text-xs">
              <span className="surface-chip rounded-full px-2 py-1 text-fg/80">
                Rankings coming soon
              </span>
              <span
                className="surface-chip rounded-full px-2 py-1 text-muted"
                title="Ban rate is not available yet."
              >
                Ban Rate —
              </span>
              <Link
                href={`/lol/matchups/${championId}?role=${encodeURIComponent(effectiveRole)}${normalizedRankTier ? `&rankTier=${encodeURIComponent(normalizedRankTier)}` : ""}${activeRegion !== "ALL" ? `&region=${encodeURIComponent(activeRegion)}` : ""}`}
                className="rounded-full border border-primary/40 bg-primary/10 px-2 py-1 text-primary hover:bg-primary/20"
              >
                Matchup Analysis
              </Link>
              <Link
                href={`/lol/pro-builds/${championId}${activeRegion !== "ALL" ? `?region=${encodeURIComponent(activeRegion)}` : ""}`}
                className="rounded-full border border-primary/40 bg-primary/10 px-2 py-1 text-primary hover:bg-primary/20"
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
          baseHref={`/lol/champions/${championId}`}
          patch={winrates?.patch ?? builds?.patch}
        />
        <AnalyticsSampleBanner sample={sampleNotice} />
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
          <p className="mt-2 text-sm text-fg/75">No games have been added for this patch yet.</p>
        ) : (
          <div className="mt-4 overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-[11px] uppercase tracking-wider text-muted">
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
                      className="border-t border-border/30 transition hover:bg-surface-2/28"
                    >
                      <td className="py-2.5 pr-4 font-medium">
                        {roleDisplayLabel(w.role ?? "ALL")}
                      </td>
                      <td className="py-2.5 pr-4 text-muted">
                        {rankTierDisplayLabel(w.rankTier ?? "all")}
                      </td>
                      <td className="py-2.5 pr-4 text-right">
                        <WinRateText value={w.winRate} decimals={2} />
                      </td>
                      <td className="py-2.5 pr-4 text-right text-fg/70">
                        {formatPercent(w.pickRate, { decimals: 1 })}
                      </td>
                      <td className="py-2.5 pr-4 text-right text-fg/70">
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
                  className="rounded-lg border border-border/60 bg-white/[0.02] p-3"
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
                    <RuneSetupDisplay
                      primaryStyleId={b.primaryStyleId ?? 0}
                      subStyleId={b.subStyleId ?? 0}
                      primarySelections={b.primaryRunes ?? []}
                      subSelections={b.subRunes ?? []}
                      statShards={b.statShards ?? []}
                      runeById={runeById}
                      styleById={styleById}
                      runeSortById={runeSortById}
                      iconSize={24}
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
                          className="flex items-center justify-between rounded-md border border-border/50 bg-white/[0.02] px-3 py-2"
                        >
                          <Link
                            href={`/lol/champions/${opponentChampionId}`}
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
                          <span className="shrink-0 text-xs">
                            <WinRateText
                              value={m.winRate}
                              decimals={1}
                              games={m.games}
                            />
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
                          className="flex items-center justify-between rounded-md border border-border/50 bg-white/[0.02] px-3 py-2"
                        >
                          <Link
                            href={`/lol/champions/${opponentChampionId}`}
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
                          <span className="shrink-0 text-xs">
                            <WinRateText
                              value={m.winRate}
                              decimals={1}
                              games={m.games}
                            />
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
    </div>
  );
}
