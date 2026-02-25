import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client/schema";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { ChampionPortrait } from "@/components/ChampionPortrait";
import { FilterBar } from "@/components/FilterBar";
import { WinRateText } from "@/components/WinRateText";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { formatGames } from "@/lib/format";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";

type ChampionWinRateSummary = components["schemas"]["ChampionWinRateSummary"];
type ChampionMatchupsResponse = components["schemas"]["ChampionMatchupsResponse"];
type MatchupEntryDto = components["schemas"]["MatchupEntryDto"];

const ROLES = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"] as const;
const RANK_TIERS = [
  "all",
  "IRON",
  "BRONZE",
  "SILVER",
  "GOLD",
  "PLATINUM",
  "EMERALD",
  "DIAMOND",
  "MASTER",
  "GRANDMASTER",
  "CHALLENGER"
] as const;

function normalizeRole(role: string | undefined) {
  if (!role) return null;
  const upper = role.toUpperCase();
  return ROLES.includes(upper as (typeof ROLES)[number]) ? upper : null;
}

function normalizeRankTier(rankTier: string | undefined) {
  if (!rankTier) return null;
  const upper = rankTier.toUpperCase();
  if (upper === "ALL") return null;
  return RANK_TIERS.includes(upper as (typeof RANK_TIERS)[number]) ? upper : null;
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
  sort
}: {
  championId: number;
  role: string;
  rankTier: string | null;
  sort: string;
}) {
  const params = new URLSearchParams({ role, sort });
  if (rankTier) params.set("rankTier", rankTier);
  return `/matchups/${championId}?${params.toString()}`;
}

export default async function MatchupAnalysisPage({
  params,
  searchParams
}: {
  params: Promise<{ championId: string }>;
  searchParams?: Promise<{ role?: string; rankTier?: string; sort?: string }>;
}) {
  const resolvedParams = await params;
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
  const championId = Number(resolvedParams.championId);
  if (!Number.isFinite(championId) || championId <= 0) {
    return <BackendErrorCard title="Matchup Analysis" message="Invalid champion id." />;
  }

  const explicitRole = normalizeRole(resolvedSearchParams?.role);
  const normalizedRankTier = normalizeRankTier(resolvedSearchParams?.rankTier);
  const sortKey = resolvedSearchParams?.sort === "games" ? "games" : "winRate";

  const verbosity = getErrorVerbosity();
  const qsTier = normalizedRankTier ? `?rankTier=${encodeURIComponent(normalizedRankTier)}` : "";
  const [staticData, winRes] = await Promise.all([
    fetchChampionMap(),
    fetchBackendJson<ChampionWinRateSummary>(
      `${getBackendBaseUrl()}/api/analytics/champions/${championId}/winrates${qsTier}`,
      { next: { revalidate: 60 * 60 } }
    )
  ]);

  const winrates = winRes.ok ? winRes.body! : null;
  const effectiveRole = explicitRole ?? mostPlayedRole(winrates) ?? "MIDDLE";

  const qsRank = normalizedRankTier ? `&rankTier=${encodeURIComponent(normalizedRankTier)}` : "";
  const matchupRes = await fetchBackendJson<ChampionMatchupsResponse>(
    `${getBackendBaseUrl()}/api/analytics/champions/${championId}/matchups?role=${encodeURIComponent(effectiveRole)}${qsRank}`,
    { next: { revalidate: 60 * 60 } }
  );

  if (!matchupRes.ok && !winRes.ok) {
    return (
      <BackendErrorCard
        title="Matchup Analysis"
        message={
          matchupRes.errorKind === "timeout"
            ? "Timed out reaching the backend."
            : matchupRes.errorKind === "unreachable"
              ? "We are having trouble reaching the backend."
              : "Failed to load matchup analysis."
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

  return (
    <div className="grid gap-6">
      <header className="glass-card mesh-highlight grid gap-3 rounded-3xl p-5 md:p-6">
        <div className="flex flex-wrap items-center gap-3">
          <Image
            src={championIconUrl(version, champion?.id ?? "Unknown")}
            alt={championName}
            width={56}
            height={56}
            className="rounded-xl border border-border/60"
          />
          <div>
            <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
              Matchup Analysis
            </h1>
            <p className="text-sm text-fg/75">{championName}</p>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Badge className="border-primary/40 bg-primary/10 text-primary">
            Patch {matchups?.patch ?? winrates?.patch ?? "Unknown"}
          </Badge>
          <Badge>{roleDisplayLabel(effectiveRole)}</Badge>
          <Badge>{normalizedRankTier ?? "All Ranks"}</Badge>
          <Badge>{allMatchups.length} matchups</Badge>
        </div>

        <FilterBar
          roles={ROLES}
          activeRole={effectiveRole}
          activeRank={normalizedRankTier?.toLowerCase() ?? "all"}
          baseHref={`/matchups/${championId}`}
          patch={matchups?.patch ?? winrates?.patch}
        />
      </header>

      <div className="grid gap-6 md:grid-cols-2">
        <Card className="p-5">
          <h2 className="font-[var(--font-sora)] text-lg font-semibold">Weak Against</h2>
          <p className="mt-1 text-xs text-muted">Champions that consistently counter {championName}</p>
          {counters.length === 0 ? (
            <p className="mt-3 text-sm text-muted">No counter data available for this filter.</p>
          ) : (
            <ul className="mt-3 grid gap-2">
              {counters.map((entry, idx) => {
                const opponentId = entry.opponentChampionId ?? 0;
                const opponent = champions[String(opponentId)];
                return (
                  <li key={`${opponentId}-${idx}`} className="flex items-center justify-between rounded-lg border border-border/50 bg-white/[0.03] px-3 py-2">
                    <Link href={`/champions/${opponentId}`} className="min-w-0 hover:underline">
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
          <h2 className="font-[var(--font-sora)] text-lg font-semibold">Strong Against</h2>
          <p className="mt-1 text-xs text-muted">{championName} tends to perform well into these champions</p>
          {favorable.length === 0 ? (
            <p className="mt-3 text-sm text-muted">No favorable matchup data available for this filter.</p>
          ) : (
            <ul className="mt-3 grid gap-2">
              {favorable.map((entry, idx) => {
                const opponentId = entry.opponentChampionId ?? 0;
                const opponent = champions[String(opponentId)];
                return (
                  <li key={`${opponentId}-${idx}`} className="flex items-center justify-between rounded-lg border border-border/50 bg-white/[0.03] px-3 py-2">
                    <Link href={`/champions/${opponentId}`} className="min-w-0 hover:underline">
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
          <h2 className="font-[var(--font-sora)] text-lg font-semibold">All Matchups</h2>
          <div className="flex items-center gap-2 text-xs">
            <Link
              href={buildSortHref({
                championId,
                role: effectiveRole,
                rankTier: normalizedRankTier,
                sort: "winRate"
              })}
              className={`rounded-full border px-2.5 py-1 ${
                sortKey === "winRate"
                  ? "border-primary/45 bg-primary/10 text-primary"
                  : "border-border/60 bg-white/[0.03] text-fg/75"
              }`}
            >
              Sort by Win Rate
            </Link>
            <Link
              href={buildSortHref({
                championId,
                role: effectiveRole,
                rankTier: normalizedRankTier,
                sort: "games"
              })}
              className={`rounded-full border px-2.5 py-1 ${
                sortKey === "games"
                  ? "border-primary/45 bg-primary/10 text-primary"
                  : "border-border/60 bg-white/[0.03] text-fg/75"
              }`}
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
                    No matchup samples available for the selected filters.
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
                        <Link href={`/champions/${opponentId}`} className="hover:underline">
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
                        title="Gold diff at 15 minutes is not yet provided by the backend."
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
