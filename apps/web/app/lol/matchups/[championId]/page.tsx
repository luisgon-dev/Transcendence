import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { ChampionPortrait } from "@/components/ChampionPortrait";
import { FilterBar } from "@/components/FilterBar";
import { WinRateText } from "@/components/WinRateText";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { pickMostSevereAnalyticsSample, type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { formatGames } from "@/lib/format";
import { normalizeRankTierParam, rankTierDisplayLabel } from "@/lib/ranks";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";

type ChampionWinRateSummary = components["schemas"]["ChampionWinRateSummary"];
type ChampionMatchupsResponse = components["schemas"]["ChampionMatchupsResponse"];
type MatchupEntryDto = components["schemas"]["MatchupEntryDto"];

const ROLES = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"] as const;

function normalizeRole(role: string | undefined) {
  if (!role) return null;
  const upper = role.toUpperCase();
  return ROLES.includes(upper as (typeof ROLES)[number]) ? upper : null;
}

function mostPlayedRole(winrates: ChampionWinRateSummary | null) {
  if (!winrates?.byRoleTier?.length) return null;
  const roleGames = new Map<string, number>();
  for (const row of winrates.byRoleTier ?? []) {
    if (!row.role) continue;
    roleGames.set(row.role, (roleGames.get(row.role) ?? 0) + (row.games ?? 0));
  }
  const [bestRole] = [...roleGames.entries()].sort((a, b) => b[1] - a[1])[0] ?? [];
  return bestRole ? normalizeRole(bestRole) : null;
}

function matchupVerdict(winRate: number | null | undefined): string {
  const pct = (winRate ?? 0) * 100;
  if (pct >= 52) return "Favored";
  if (pct < 48) return "Unfavored";
  return "Even";
}

function buildSortHref({
  championId,
  role,
  rankTier,
  region,
  sort
}: {
  championId: number;
  role: string;
  rankTier: string | null;
  region: string;
  sort: string;
}) {
  const params = new URLSearchParams({ role, sort });
  if (rankTier) params.set("rankTier", rankTier);
  if (region !== "ALL") params.set("region", region);
  return `/lol/matchups/${championId}?${params.toString()}`;
}

export default async function MatchupAnalysisPage({
  params,
  searchParams
}: {
  params: Promise<{ championId: string }>;
  searchParams?: Promise<{ role?: string; rankTier?: string; sort?: string; region?: string }>;
}) {
  const resolvedParams = await params;
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const { activeRegion, activeRegionLabel, options: regionOptions } = await resolveAnalyticsRegion(
    resolvedSearchParams?.region
  );
  const championId = Number(resolvedParams.championId);
  if (!Number.isFinite(championId) || championId <= 0) {
    return <BackendErrorCard title="Matchup Analysis" message="Invalid champion link." />;
  }

  const explicitRole = normalizeRole(resolvedSearchParams?.role);
  const normalizedRankTier = normalizeRankTierParam(resolvedSearchParams?.rankTier);
  const sortKey = resolvedSearchParams?.sort === "games" ? "games" : "winRate";

  const verbosity = getErrorVerbosity();
  const winrateQuery = new URLSearchParams();
  if (normalizedRankTier) winrateQuery.set("rankTier", normalizedRankTier);
  if (activeRegion !== "ALL") winrateQuery.set("region", activeRegion);
  const qsTier = winrateQuery.toString() ? `?${winrateQuery.toString()}` : "";
  const [staticData, winRes] = await Promise.all([
    fetchChampionMap(),
    fetchBackendJson<ChampionWinRateSummary>(
      `${getBackendBaseUrl()}/api/lol/analytics/champions/${championId}/winrates${qsTier}`,
      { next: { revalidate: 60 * 60 } }
    )
  ]);

  const winrates = winRes.ok ? winRes.body! : null;
  const effectiveRole = explicitRole ?? mostPlayedRole(winrates) ?? "MIDDLE";

  const matchupQuery = new URLSearchParams({ role: effectiveRole });
  if (normalizedRankTier) matchupQuery.set("rankTier", normalizedRankTier);
  if (activeRegion !== "ALL") matchupQuery.set("region", activeRegion);
  const matchupRes = await fetchBackendJson<ChampionMatchupsResponse>(
    `${getBackendBaseUrl()}/api/lol/analytics/champions/${championId}/matchups?${matchupQuery.toString()}`,
    { next: { revalidate: 60 * 60 } }
  );

  if (!matchupRes.ok && !winRes.ok) {
    return (
      <BackendErrorCard
        title="Matchup Analysis"
        message={
          matchupRes.errorKind === "timeout"
            ? "This page is taking too long to load."
            : matchupRes.errorKind === "unreachable"
              ? "We couldn't load matchup data right now."
              : "We couldn't load matchup data."
        }
        requestId={matchupRes.requestId || winRes.requestId}
        detail={
          verbosity === "verbose"
            ? JSON.stringify(
                {
                  winrates: { status: winRes.status, errorKind: winRes.errorKind },
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

  const matchups = matchupRes.ok ? matchupRes.body : null;
  const counters = matchups?.counters ?? [];
  const favorable = matchups?.favorableMatchups ?? [];

  const allMatchups = [...counters, ...favorable]
    .filter((m): m is MatchupEntryDto => Boolean(m?.opponentChampionId))
    .filter(
      (entry, idx, rows) =>
        rows.findIndex((candidate) => candidate.opponentChampionId === entry.opponentChampionId) === idx
    )
    .sort((a, b) =>
      sortKey === "games"
        ? (b.games ?? 0) - (a.games ?? 0)
        : (a.winRate ?? 0) - (b.winRate ?? 0)
    );

  const { version, champions } = staticData;
  const champion = champions[String(championId)];
  const championName = champion?.name ?? `Champion ${championId}`;
  const sampleNotice = pickMostSevereAnalyticsSample(
    (winrates as { sample?: unknown } | null)?.sample as AnalyticsSampleLike,
    (matchups as { sample?: unknown } | null)?.sample as AnalyticsSampleLike
  );

  return (
    <div className="grid gap-6">
      <header className="page-hero grid gap-3 p-5 md:p-6">
        <div className="flex flex-wrap items-center gap-3">
          <Image
            src={championIconUrl(version, champion?.id ?? "Unknown")}
            alt={championName}
            width={56}
            height={56}
            className="rounded-xl border border-border/60"
          />
          <div>
            <h1 className="type-title sm:text-[2.4rem]">
              Matchup Analysis
            </h1>
            <p className="type-ui mt-2 text-fg/75">How {championName} performs into the current field.</p>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Badge className="border-primary/40 bg-primary/10 text-primary">
            Patch {matchups?.patch ?? winrates?.patch ?? "Unknown"}
          </Badge>
          <Badge>{activeRegionLabel}</Badge>
          <Badge>{roleDisplayLabel(effectiveRole)}</Badge>
          <Badge>{rankTierDisplayLabel(normalizedRankTier ?? "all")}</Badge>
          <Badge>{allMatchups.length} matchups</Badge>
        </div>

        <FilterBar
          roles={ROLES}
          activeRole={effectiveRole}
          activeRank={normalizedRankTier ?? "all"}
          regionOptions={regionOptions}
          activeRegion={activeRegion}
          baseHref={`/lol/matchups/${championId}`}
          patch={matchups?.patch ?? winrates?.patch}
        />
        <AnalyticsSampleBanner sample={sampleNotice} />
      </header>

      <div className="grid gap-6 md:grid-cols-2">
        <Card className="p-5">
          <h2 className="type-section">Weak Against</h2>
          <p className="mt-1 text-xs text-muted">Champions that give {championName} the most trouble</p>
          {counters.length === 0 ? (
            <p className="mt-3 text-sm text-muted">No counter data is available for these filters.</p>
          ) : (
            <ul className="mt-3 grid gap-2">
              {counters.map((entry, idx) => {
                const opponentId = entry.opponentChampionId ?? 0;
                const opponent = champions[String(opponentId)];
                return (
                  <li key={`${opponentId}-${idx}`} className="flex items-center justify-between rounded-lg border border-border/50 bg-white/[0.03] px-3 py-2">
                    <Link href={`/lol/champions/${opponentId}${activeRegion !== "ALL" ? `?region=${encodeURIComponent(activeRegion)}` : ""}`} className="min-w-0 hover:underline">
                      <ChampionPortrait
                        championSlug={opponent?.id ?? "Unknown"}
                        championName={opponent?.name ?? `Champion ${opponentId}`}
                        version={version}
                        size={24}
                        showName
                        className="min-w-0"
                      />
                    </Link>
                    <WinRateText value={entry.winRate} decimals={1} games={entry.games} className="text-xs" />
                  </li>
                );
              })}
            </ul>
          )}
        </Card>

        <Card className="p-5">
          <h2 className="type-section">Strong Against</h2>
          <p className="mt-1 text-xs text-muted">Champions {championName} usually handles well</p>
          {favorable.length === 0 ? (
            <p className="mt-3 text-sm text-muted">No favorable matchup data is available for these filters.</p>
          ) : (
            <ul className="mt-3 grid gap-2">
              {favorable.map((entry, idx) => {
                const opponentId = entry.opponentChampionId ?? 0;
                const opponent = champions[String(opponentId)];
                return (
                  <li key={`${opponentId}-${idx}`} className="flex items-center justify-between rounded-lg border border-border/50 bg-white/[0.03] px-3 py-2">
                    <Link href={`/lol/champions/${opponentId}${activeRegion !== "ALL" ? `?region=${encodeURIComponent(activeRegion)}` : ""}`} className="min-w-0 hover:underline">
                      <ChampionPortrait
                        championSlug={opponent?.id ?? "Unknown"}
                        championName={opponent?.name ?? `Champion ${opponentId}`}
                        version={version}
                        size={24}
                        showName
                        className="min-w-0"
                      />
                    </Link>
                    <WinRateText value={entry.winRate} decimals={1} games={entry.games} className="text-xs" />
                  </li>
                );
              })}
            </ul>
          )}
        </Card>
      </div>

      <Card className="p-5">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="type-section">All Matchups</h2>
          <div className="flex items-center gap-2 text-xs">
            <Link
              href={buildSortHref({
                championId,
                role: effectiveRole,
                rankTier: normalizedRankTier,
                region: activeRegion,
                sort: "winRate"
              })}
              className={`control-chip type-ui px-3 py-2 ${
                sortKey === "winRate"
                  ? "font-semibold"
                  : ""
              }`}
              data-active={sortKey === "winRate"}
            >
              Sort by Win Rate
            </Link>
            <Link
              href={buildSortHref({
                championId,
                role: effectiveRole,
                rankTier: normalizedRankTier,
                region: activeRegion,
                sort: "games"
              })}
              className={`control-chip type-ui px-3 py-2 ${
                sortKey === "games"
                  ? "font-semibold"
                  : ""
              }`}
              data-active={sortKey === "games"}
            >
              Sort by Games
            </Link>
          </div>
        </div>
        <div className="mt-4 overflow-x-auto">
          <table className="w-full min-w-[720px] text-left text-sm">
            <thead className="text-[11px] uppercase tracking-wider text-muted">
              <tr className="border-b border-border/30">
                <th className="py-2 pr-4">Opponent</th>
                <th className="py-2 pr-4 text-right">Win Rate</th>
                <th className="py-2 pr-4 text-right">Games</th>
                <th className="py-2 pr-4 text-right">Verdict</th>
                <th className="py-2 pr-4 text-right">Gold @ 15</th>
              </tr>
            </thead>
            <tbody>
              {allMatchups.length === 0 ? (
                <tr>
                  <td colSpan={5} className="py-4 text-sm text-muted">
                    No matchup data is available for the selected filters yet.
                  </td>
                </tr>
              ) : (
                allMatchups.map((entry, idx) => {
                  const opponentId = entry.opponentChampionId ?? 0;
                  const opponent = champions[String(opponentId)];
                  const verdict = matchupVerdict(entry.winRate);
                  return (
                    <tr key={`${opponentId}-${idx}`} className="border-b border-border/20">
                      <td className="py-2.5 pr-4">
                        <Link href={`/lol/champions/${opponentId}${activeRegion !== "ALL" ? `?region=${encodeURIComponent(activeRegion)}` : ""}`} className="hover:underline">
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
                        <WinRateText value={entry.winRate} decimals={1} />
                      </td>
                      <td className="py-2.5 pr-4 text-right text-fg/70">{formatGames(entry.games)}</td>
                      <td className="py-2.5 pr-4 text-right text-fg/75">{verdict}</td>
                      <td
                        className="py-2.5 pr-4 text-right text-muted"
                        title="Gold difference at 15 minutes is not available yet."
                      >
                        —
                      </td>
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
