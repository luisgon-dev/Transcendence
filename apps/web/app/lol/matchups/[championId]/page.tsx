import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { AnalyticsSampleBanner } from "@/components/AnalyticsSampleBanner";
import { ChampionPortrait } from "@/components/ChampionPortrait";
import { FilterBar } from "@/components/FilterBar";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { DataBar } from "@/components/ui/DataBar";
import { fetchBackendJson } from "@/lib/backendCall";
import { resolveAnalyticsRegion } from "@/lib/analyticsRegions";
import { pickMostSevereAnalyticsSample, type AnalyticsSampleLike } from "@/lib/analyticsSample";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { formatGames } from "@/lib/format";
import { fetchLolAnalyticsPatches } from "@/lib/lolAnalyticsPatches";
import { normalizeAnalyticsPatch } from "@/lib/lolPatchFilters";
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
  patch,
  sort
}: {
  championId: number;
  role: string;
  rankTier: string | null;
  region: string;
  patch: string | null;
  sort: string;
}) {
  const params = new URLSearchParams({ role, sort });
  if (rankTier) params.set("rankTier", rankTier);
  if (region !== "ALL") params.set("region", region);
  if (patch) params.set("patch", patch);
  return `/lol/matchups/${championId}?${params.toString()}`;
}

export default async function MatchupAnalysisPage({
  params,
  searchParams
}: {
  params: Promise<{ championId: string }>;
  searchParams?: Promise<{ role?: string; rankTier?: string; sort?: string; region?: string; patch?: string }>;
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
  const selectedPatch = normalizeAnalyticsPatch(resolvedSearchParams?.patch);
  const sortKey = resolvedSearchParams?.sort === "games" ? "games" : "winRate";

  const verbosity = getErrorVerbosity();
  const winrateQuery = new URLSearchParams();
  if (normalizedRankTier) winrateQuery.set("rankTier", normalizedRankTier);
  if (activeRegion !== "ALL") winrateQuery.set("region", activeRegion);
  if (selectedPatch) winrateQuery.set("patch", selectedPatch);
  const qsTier = winrateQuery.toString() ? `?${winrateQuery.toString()}` : "";
  const [staticData, patchOptions, winRes] = await Promise.all([
    fetchChampionMap(),
    fetchLolAnalyticsPatches(),
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
  if (selectedPatch) matchupQuery.set("patch", selectedPatch);
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
  const championLinkParams = new URLSearchParams();
  if (normalizedRankTier) championLinkParams.set("rankTier", normalizedRankTier);
  if (activeRegion !== "ALL") championLinkParams.set("region", activeRegion);
  if (selectedPatch) championLinkParams.set("patch", selectedPatch);
  const championLinkQuery = championLinkParams.toString();
  const sharedFilterParams = selectedPatch ? { patch: selectedPatch } : {};

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
            <h1 className="type-page-title">
              Matchup Analysis
            </h1>
            <p className="type-ui mt-2 text-fg/75">How {championName} performs into the selected patch field.</p>
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
          patchOptions={patchOptions}
          activePatch={selectedPatch}
          extraParams={sharedFilterParams}
          baseHref={`/lol/matchups/${championId}`}
        />
        <div className="mt-3">
          <AnalyticsSampleBanner sample={sampleNotice} />
        </div>
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
                  <li key={`${opponentId}-${idx}`} className="surface-subtle flex items-center justify-between rounded-card px-3 py-2">
                    <Link href={`/lol/champions/${opponentId}${championLinkQuery ? `?${championLinkQuery}` : ""}`} className="min-w-0 hover:underline">
                      <ChampionPortrait
                        championSlug={opponent?.id ?? "Unknown"}
                        championName={opponent?.name ?? `Champion ${opponentId}`}
                        version={version}
                        size={24}
                        showName
                        className="min-w-0"
                      />
                    </Link>
                    <DataBar value={entry.winRate} decimals={1} />
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
                  <li key={`${opponentId}-${idx}`} className="surface-subtle flex items-center justify-between rounded-card px-3 py-2">
                    <Link href={`/lol/champions/${opponentId}${championLinkQuery ? `?${championLinkQuery}` : ""}`} className="min-w-0 hover:underline">
                      <ChampionPortrait
                        championSlug={opponent?.id ?? "Unknown"}
                        championName={opponent?.name ?? `Champion ${opponentId}`}
                        version={version}
                        size={24}
                        showName
                        className="min-w-0"
                      />
                    </Link>
                    <DataBar value={entry.winRate} decimals={1} />
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
                patch: selectedPatch,
                sort: "winRate"
              })}
              className={`control-tab type-ui px-3 py-2 ${
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
                patch: selectedPatch,
                sort: "games"
              })}
              className={`control-tab type-ui px-3 py-2 ${
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
                    verdict === "Favored" ? "text-win" : verdict === "Unfavored" ? "text-loss" : "text-muted";
                  return (
                    <tr key={`${opponentId}-${idx}`} className="border-b border-border/40">
                      <td className="py-2.5 pr-4">
                        <Link href={`/lol/champions/${opponentId}${championLinkQuery ? `?${championLinkQuery}` : ""}`} className="hover:underline">
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
                      <td className="type-tabular py-2.5 pr-4 text-right tabular-nums text-fg/70">{formatGames(entry.games)}</td>
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
