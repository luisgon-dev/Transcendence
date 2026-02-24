import Image from "next/image";
import Link from "next/link";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { FilterBar } from "@/components/FilterBar";
import { ItemBuildDisplay } from "@/components/ItemBuildDisplay";
import { RuneSetupDisplay } from "@/components/RuneSetupDisplay";
import { StatsBar } from "@/components/StatsBar";
import { TierBadge } from "@/components/TierBadge";
import { WinRateText } from "@/components/WinRateText";
import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { formatGames, formatPercent } from "@/lib/format";
import { roleDisplayLabel } from "@/lib/roles";
import {
  championIconUrl,
  fetchChampionMap,
  fetchItemMap,
  fetchRunesReforged
} from "@/lib/staticData";
import { deriveTier } from "@/lib/tierlist";

type ChampionWinRateDto = {
  championId: number;
  role: string;
  rankTier: string;
  games: number;
  wins: number;
  winRate: number;
  pickRate: number;
  patch: string;
};

type ChampionWinRateSummary = {
  championId: number;
  patch: string;
  byRoleTier: ChampionWinRateDto[];
};

type ChampionBuildDto = {
  items: number[];
  coreItems: number[];
  situationalItems: number[];
  primaryStyleId: number;
  subStyleId: number;
  primaryRunes: number[];
  subRunes: number[];
  statShards: number[];
  games: number;
  winRate: number;
};

type ChampionBuildsResponse = {
  championId: number;
  role: string;
  rankTier: string;
  patch: string;
  globalCoreItems: number[];
  builds: ChampionBuildDto[];
};

type MatchupEntryDto = {
  opponentChampionId: number;
  games: number;
  wins: number;
  losses: number;
  winRate: number;
};

type ChampionMatchupsResponse = {
  championId: number;
  role: string;
  rankTier?: string | null;
  patch: string;
  counters: MatchupEntryDto[];
  favorableMatchups: MatchupEntryDto[];
};

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

function pickMostPlayedRole(summary: ChampionWinRateSummary | null) {
  if (!summary?.byRoleTier?.length) return null;
  const gamesByRole = new Map<string, number>();
  for (const entry of summary.byRoleTier) {
    const role = entry.role.toUpperCase();
    gamesByRole.set(role, (gamesByRole.get(role) ?? 0) + entry.games);
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
  const forRole = winrates.byRoleTier.filter(
    (e) => e.role.toUpperCase() === role.toUpperCase()
  );
  if (forRole.length === 0) return null;
  return forRole.reduce((best, cur) => (cur.games > best.games ? cur : best));
}

export default async function ChampionDetailPage({
  params,
  searchParams
}: {
  params: Promise<{ championId: string }>;
  searchParams?: Promise<{ role?: string; rankTier?: string }>;
}) {
  const resolvedParams = await params;
  const resolvedSearchParams = searchParams ? await searchParams : undefined;
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
  const normalizedRankTier = normalizeRankTier(resolvedSearchParams?.rankTier);
  const qsTier = normalizedRankTier
    ? `?rankTier=${encodeURIComponent(normalizedRankTier)}`
    : "";

  const verbosity = getErrorVerbosity();
  const [staticData, itemStatic, runeStatic, winRes] = await Promise.all([
    fetchChampionMap(),
    fetchItemMap(),
    fetchRunesReforged(),
    fetchBackendJson<ChampionWinRateSummary>(
      `${getBackendBaseUrl()}/api/analytics/champions/${championId}/winrates${qsTier}`,
      { next: { revalidate: 60 * 60 } }
    )
  ]);

  const winrates = winRes.ok ? winRes.body! : null;
  let fallbackWinrates: ChampionWinRateSummary | null = null;

  if (!explicitRole && normalizedRankTier && (!winrates || winrates.byRoleTier.length === 0)) {
    const fallbackWinRes = await fetchBackendJson<ChampionWinRateSummary>(
      `${getBackendBaseUrl()}/api/analytics/champions/${championId}/winrates`,
      { next: { revalidate: 60 * 60 } }
    );
    fallbackWinrates = fallbackWinRes.ok ? fallbackWinRes.body! : null;
  }

  const effectiveRole =
    explicitRole ??
    pickMostPlayedRole(winrates) ??
    pickMostPlayedRole(fallbackWinrates) ??
    "MIDDLE";

  const qsBuildAndMatchupTier = normalizedRankTier
    ? `&rankTier=${encodeURIComponent(normalizedRankTier)}`
    : "";

  const [buildRes, matchupRes] = await Promise.all([
    fetchBackendJson<ChampionBuildsResponse>(
      `${getBackendBaseUrl()}/api/analytics/champions/${championId}/builds?role=${encodeURIComponent(
        effectiveRole
      )}${qsBuildAndMatchupTier}`,
      { next: { revalidate: 60 * 60 } }
    ),
    fetchBackendJson<ChampionMatchupsResponse>(
      `${getBackendBaseUrl()}/api/analytics/champions/${championId}/matchups?role=${encodeURIComponent(
        effectiveRole
      )}${qsBuildAndMatchupTier}`,
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

  if (!winRes.ok && !buildRes.ok && !matchupRes.ok) {
    const requestId = winRes.requestId || buildRes.requestId || matchupRes.requestId;
    const kind = winRes.errorKind ?? buildRes.errorKind ?? matchupRes.errorKind;
    return (
      <BackendErrorCard
        title={champName}
        message={
          kind === "timeout"
            ? "Timed out reaching the backend."
            : kind === "unreachable"
              ? "We are having trouble reaching the backend."
              : "Failed to load champion data from the backend."
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
  const heroEntry = pickBestEntry(winrates, effectiveRole);
  const heroTier = deriveTier(heroEntry?.winRate);

  return (
    <div className="grid gap-6">
      {/* ── Champion Header ── */}
      <header className="flex flex-col gap-4">
        <div className="flex items-center gap-4">
          <Image
            src={championIconUrl(version, champSlug)}
            alt={champName}
            width={64}
            height={64}
            className="rounded-xl border border-border/60"
          />
          <div>
            <div className="flex items-center gap-2.5">
              <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
                {champName}
              </h1>
              <TierBadge tier={heroTier} size="md" />
            </div>
            <p className="mt-0.5 text-sm text-muted">
              {roleDisplayLabel(effectiveRole)} &middot; {normalizedRankTier ?? "All Ranks"}
            </p>
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
          activeRank={normalizedRankTier?.toLowerCase() ?? "all"}
          baseHref={`/champions/${championId}`}
          patch={winrates?.patch ?? builds?.patch}
        />
      </header>

      {/* ── Win Rates Table ── */}
      <Card className="p-5">
        <h2 className="font-[var(--font-sora)] text-lg font-semibold">
          Win Rates
        </h2>
        {!winrates ? (
          <p className="mt-2 text-sm text-fg/75">No win rate data available.</p>
        ) : winrates.byRoleTier.length === 0 ? (
          <p className="mt-2 text-sm text-fg/75">No samples for this patch.</p>
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
                {winrates.byRoleTier
                  .slice()
                  .sort((a, b) => b.games - a.games)
                  .map((w) => (
                    <tr
                      key={`${w.role}-${w.rankTier}`}
                      className="border-t border-border/30 transition hover:bg-white/[0.03]"
                    >
                      <td className="py-2.5 pr-4 font-medium">
                        {roleDisplayLabel(w.role)}
                      </td>
                      <td className="py-2.5 pr-4 text-muted">{w.rankTier}</td>
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
        <Card className="p-5">
          <h2 className="font-[var(--font-sora)] text-lg font-semibold">
            Builds
          </h2>
          {!builds ? (
            <p className="mt-2 text-sm text-fg/75">No build data available.</p>
          ) : builds.builds.length === 0 ? (
            <p className="mt-2 text-sm text-fg/75">No samples for this role.</p>
          ) : (
            <div className="mt-4 grid gap-4">
              {/* Global Core Items */}
              {builds.globalCoreItems.length > 0 ? (
                <ItemBuildDisplay
                  allItems={[]}
                  coreItems={builds.globalCoreItems}
                  situationalItems={[]}
                  version={itemVersion}
                  items={items}
                />
              ) : null}

              {builds.builds.map((b, idx) => (
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
                      allItems={b.items}
                      coreItems={b.coreItems}
                      situationalItems={b.situationalItems}
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
                      primaryStyleId={b.primaryStyleId}
                      subStyleId={b.subStyleId}
                      primarySelections={b.primaryRunes ?? []}
                      subSelections={b.subRunes ?? []}
                      statShards={b.statShards ?? []}
                      runeById={runeById}
                      styleById={styleById}
                      iconSize={20}
                    />
                  </div>
                </div>
              ))}
            </div>
          )}
        </Card>

        {/* ── Matchups ── */}
        <Card className="p-5">
          <h2 className="font-[var(--font-sora)] text-lg font-semibold">
            Matchups
          </h2>
          {!matchups ? (
            <p className="mt-2 text-sm text-fg/75">
              No matchup data available.
            </p>
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
                {matchups.counters.length === 0 ? (
                  <p className="mt-2 text-xs text-muted">No strong counters found.</p>
                ) : (
                  <ul className="mt-2 grid gap-1.5 text-sm">
                    {matchups.counters.map((m) => {
                      const opp = champions[String(m.opponentChampionId)];
                      return (
                        <li
                          key={m.opponentChampionId}
                          className="flex items-center justify-between rounded-md border border-border/50 bg-white/[0.02] px-3 py-2"
                        >
                          <Link
                            href={`/champions/${m.opponentChampionId}`}
                            className="flex min-w-0 items-center gap-2 hover:underline"
                          >
                            {opp?.id ? (
                              <Image
                                src={championIconUrl(version, opp.id)}
                                alt={opp?.name ?? `Champion ${m.opponentChampionId}`}
                                width={24}
                                height={24}
                                className="rounded-md"
                              />
                            ) : (
                              <div className="h-6 w-6 rounded-md border border-border/60 bg-black/25" />
                            )}
                            <span className="truncate font-medium">
                              {opp?.name ?? `Champion ${m.opponentChampionId}`}
                            </span>
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
                {matchups.favorableMatchups.length === 0 ? (
                  <p className="mt-2 text-xs text-muted">
                    No strong favorable matchups found.
                  </p>
                ) : (
                  <ul className="mt-2 grid gap-1.5 text-sm">
                    {matchups.favorableMatchups.map((m) => {
                      const opp = champions[String(m.opponentChampionId)];
                      return (
                        <li
                          key={m.opponentChampionId}
                          className="flex items-center justify-between rounded-md border border-border/50 bg-white/[0.02] px-3 py-2"
                        >
                          <Link
                            href={`/champions/${m.opponentChampionId}`}
                            className="flex min-w-0 items-center gap-2 hover:underline"
                          >
                            {opp?.id ? (
                              <Image
                                src={championIconUrl(version, opp.id)}
                                alt={opp?.name ?? `Champion ${m.opponentChampionId}`}
                                width={24}
                                height={24}
                                className="rounded-md"
                              />
                            ) : (
                              <div className="h-6 w-6 rounded-md border border-border/60 bg-black/25" />
                            )}
                            <span className="truncate font-medium">
                              {opp?.name ?? `Champion ${m.opponentChampionId}`}
                            </span>
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
