import Image from "next/image";
import Link from "next/link";

import { BackendErrorCard } from "@/components/BackendErrorCard";
import { RuneSetupDisplay } from "@/components/RuneSetupDisplay";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { getErrorVerbosity } from "@/lib/env";
import { formatPercent } from "@/lib/format";
import { roleDisplayLabel } from "@/lib/roles";
import {
  championIconUrl,
  fetchChampionMap,
  fetchItemMap,
  fetchRunesReforged,
  itemIconUrl
} from "@/lib/staticData";

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

  return (
    <div className="grid gap-6">
      <header className="flex flex-col gap-3">
        <div className="flex items-center gap-3">
          <Image
            src={championIconUrl(version, champSlug)}
            alt={champName}
            width={52}
            height={52}
            className="rounded-xl"
          />
          <div>
            <h1 className="font-[var(--font-sora)] text-3xl font-semibold tracking-tight">
              {champName}
            </h1>
            <p className="text-sm text-muted">Champion #{championId}</p>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Badge>Role: {roleDisplayLabel(effectiveRole)}</Badge>
          <Badge>Tier: {normalizedRankTier ?? "all"}</Badge>
          {winrates ? <Badge className="border-primary/40 bg-primary/10 text-primary">Patch {winrates.patch}</Badge> : null}
        </div>

        <form className="mt-2 flex flex-wrap items-end gap-2" method="get">
          <label className="grid gap-1">
            <span className="text-xs text-muted">Role</span>
            <select
              name="role"
              defaultValue={effectiveRole}
              className="h-10 min-w-[160px] rounded-md border border-border/70 bg-surface/35 px-3 text-sm text-fg shadow-glass outline-none focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
            >
              {ROLES.map((r) => (
                <option key={r} value={r}>
                  {roleDisplayLabel(r)}
                </option>
              ))}
            </select>
          </label>

          <label className="grid gap-1">
            <span className="text-xs text-muted">Rank Tier</span>
            <select
              name="rankTier"
              defaultValue={normalizedRankTier ?? "all"}
              className="h-10 min-w-[180px] rounded-md border border-border/70 bg-surface/35 px-3 text-sm text-fg shadow-glass outline-none focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
            >
              {RANK_TIERS.map((tier) => (
                <option key={tier} value={tier}>
                  {tier}
                </option>
              ))}
            </select>
          </label>

          <button
            type="submit"
            className="h-10 rounded-md border border-border/70 bg-white/5 px-4 text-sm text-fg/85 shadow-glass hover:bg-white/10"
          >
            Apply
          </button>
        </form>

        <nav className="flex flex-wrap gap-2">
          {ROLES.map((r) => (
            <Link
              key={r}
              href={`/champions/${championId}?role=${r}${normalizedRankTier ? `&rankTier=${encodeURIComponent(normalizedRankTier)}` : ""}`}
              className={`rounded-md border px-3 py-1 text-sm ${
                r === effectiveRole
                  ? "border-primary/50 bg-primary/15 text-primary"
                  : "border-border/70 bg-white/5 text-fg/80 hover:bg-white/10"
              }`}
            >
              {roleDisplayLabel(r)}
            </Link>
          ))}
        </nav>
      </header>

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
              <thead className="text-xs text-muted">
                <tr>
                  <th className="py-2 pr-4">Role</th>
                  <th className="py-2 pr-4">Tier</th>
                  <th className="py-2 pr-4">Win</th>
                  <th className="py-2 pr-4">Pick</th>
                  <th className="py-2 pr-4">Games</th>
                </tr>
              </thead>
              <tbody>
                {winrates.byRoleTier
                  .slice()
                  .sort((a, b) => b.games - a.games)
                  .map((w) => (
                    <tr key={`${w.role}-${w.rankTier}`} className="border-t border-border/50">
                      <td className="py-2 pr-4 font-medium">{roleDisplayLabel(w.role)}</td>
                      <td className="py-2 pr-4">{w.rankTier}</td>
                      <td className="py-2 pr-4">{formatPercent(w.winRate)}</td>
                      <td className="py-2 pr-4">{formatPercent(w.pickRate)}</td>
                      <td className="py-2 pr-4">{w.games.toLocaleString()}</td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <div className="grid gap-6 md:grid-cols-2">
        <Card className="p-5">
          <h2 className="font-[var(--font-sora)] text-lg font-semibold">
            Builds (Top 3)
          </h2>
          {!builds ? (
            <p className="mt-2 text-sm text-fg/75">No build data available.</p>
          ) : builds.builds.length === 0 ? (
            <p className="mt-2 text-sm text-fg/75">No samples for this role.</p>
          ) : (
            <div className="mt-4 grid gap-3">
              {builds.builds.map((b, idx) => (
                <div
                  key={idx}
                  className="rounded-lg border border-border/60 bg-white/5 p-3"
                >
                  <div className="flex items-center justify-between">
                    <p className="text-sm font-semibold text-fg">Build {idx + 1}</p>
                    <p className="text-xs text-muted">
                      {b.games.toLocaleString()} games, {formatPercent(b.winRate)} win
                    </p>
                  </div>

                  <div className="mt-3 flex flex-wrap items-center gap-2">
                    <div className="flex items-center gap-1.5">
                      {b.items.map((itemId, itemIdx) => {
                        if (!itemId) {
                          return (
                            <div
                              key={`${idx}-item-${itemIdx}`}
                              className="h-7 w-7 rounded-md border border-border/60 bg-black/25"
                            />
                          );
                        }
                        const meta = items[String(itemId)];
                        const title = meta
                          ? `${meta.name}${meta.plaintext ? ` — ${meta.plaintext}` : ""}`
                          : `Item ${itemId}`;
                        return (
                          <Image
                            key={`${idx}-${itemIdx}-${itemId}`}
                            src={itemIconUrl(itemVersion, itemId)}
                            alt={meta?.name ?? `Item ${itemId}`}
                            title={title}
                            width={28}
                            height={28}
                            className="rounded-md"
                          />
                        );
                      })}
                    </div>

                    <div className="mx-1 h-4 w-px bg-border/60" />

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

        <Card className="p-5">
          <h2 className="font-[var(--font-sora)] text-lg font-semibold">
            Matchups
          </h2>
          {!matchups ? (
            <p className="mt-2 text-sm text-fg/75">
              No matchup data available.
            </p>
          ) : (
            <div className="mt-4 grid gap-4">
              <div>
                <p className="text-sm font-semibold text-fg">Counters</p>
                {matchups.counters.length === 0 ? (
                  <p className="mt-1 text-xs text-muted">No strong counters.</p>
                ) : (
                  <ul className="mt-2 grid gap-2 text-sm">
                    {matchups.counters.map((m) => (
                      <li
                        key={m.opponentChampionId}
                        className="flex items-center justify-between rounded-md border border-border/60 bg-white/5 px-3 py-2"
                      >
                        <Link href={`/champions/${m.opponentChampionId}`} className="flex min-w-0 items-center gap-2">
                          {champions[String(m.opponentChampionId)]?.id ? (
                            <Image
                              src={championIconUrl(
                                version,
                                champions[String(m.opponentChampionId)]!.id
                              )}
                              alt={champions[String(m.opponentChampionId)]?.name ?? `Champion ${m.opponentChampionId}`}
                              width={22}
                              height={22}
                              className="rounded-md"
                            />
                          ) : (
                            <div className="h-[22px] w-[22px] rounded-md border border-border/60 bg-black/25" />
                          )}
                          <span className="truncate text-sm font-medium hover:underline">
                            {champions[String(m.opponentChampionId)]?.name ??
                              `Champion ${m.opponentChampionId}`}
                          </span>
                        </Link>
                        <span className="text-xs text-muted">
                          {formatPercent(m.winRate)} ({m.games})
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
              </div>

              <div>
                <p className="text-sm font-semibold text-fg">Favorable</p>
                {matchups.favorableMatchups.length === 0 ? (
                  <p className="mt-1 text-xs text-muted">
                    No strong favorable matchups.
                  </p>
                ) : (
                  <ul className="mt-2 grid gap-2 text-sm">
                    {matchups.favorableMatchups.map((m) => (
                      <li
                        key={m.opponentChampionId}
                        className="flex items-center justify-between rounded-md border border-border/60 bg-white/5 px-3 py-2"
                      >
                        <Link href={`/champions/${m.opponentChampionId}`} className="flex min-w-0 items-center gap-2">
                          {champions[String(m.opponentChampionId)]?.id ? (
                            <Image
                              src={championIconUrl(
                                version,
                                champions[String(m.opponentChampionId)]!.id
                              )}
                              alt={champions[String(m.opponentChampionId)]?.name ?? `Champion ${m.opponentChampionId}`}
                              width={22}
                              height={22}
                              className="rounded-md"
                            />
                          ) : (
                            <div className="h-[22px] w-[22px] rounded-md border border-border/60 bg-black/25" />
                          )}
                          <span className="truncate text-sm font-medium hover:underline">
                            {champions[String(m.opponentChampionId)]?.name ??
                              `Champion ${m.opponentChampionId}`}
                          </span>
                        </Link>
                        <span className="text-xs text-muted">
                          {formatPercent(m.winRate)} ({m.games})
                        </span>
                      </li>
                    ))}
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
