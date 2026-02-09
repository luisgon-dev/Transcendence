import Image from "next/image";
import Link from "next/link";

import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { getBackendBaseUrl } from "@/lib/env";
import { formatPercent } from "@/lib/format";
import {
  championIconUrl,
  fetchChampionMap,
  fetchItemMap,
  fetchRunesReforged,
  itemIconUrl,
  runeIconUrl
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

export default async function ChampionDetailPage({
  params,
  searchParams
}: {
  params: { championId: string };
  searchParams?: { role?: string; rankTier?: string };
}) {
  const championId = Number(params.championId);
  const role = (searchParams?.role ?? "MIDDLE").toUpperCase();
  const rankTier = searchParams?.rankTier;

  const qsTier = rankTier ? `&rankTier=${encodeURIComponent(rankTier)}` : "";

  const [staticData, itemStatic, runeStatic, winRes, buildRes, matchupRes] =
    await Promise.all([
    fetchChampionMap(),
    fetchItemMap(),
    fetchRunesReforged(),
    fetch(`${getBackendBaseUrl()}/api/analytics/champions/${championId}/winrates`, {
      next: { revalidate: 60 * 60 }
    }),
    fetch(
      `${getBackendBaseUrl()}/api/analytics/champions/${championId}/builds?role=${encodeURIComponent(
        role
      )}${qsTier}`,
      { next: { revalidate: 60 * 60 } }
    ),
    fetch(
      `${getBackendBaseUrl()}/api/analytics/champions/${championId}/matchups?role=${encodeURIComponent(
        role
      )}${qsTier}`,
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

  const winrates = winRes.ok ? ((await winRes.json()) as ChampionWinRateSummary) : null;
  const builds = buildRes.ok ? ((await buildRes.json()) as ChampionBuildsResponse) : null;
  const matchups = matchupRes.ok
    ? ((await matchupRes.json()) as ChampionMatchupsResponse)
    : null;

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
          <Badge>Role: {role}</Badge>
          <Badge>Tier: {rankTier ?? "all"}</Badge>
          {winrates ? <Badge className="border-primary/40 bg-primary/10 text-primary">Patch {winrates.patch}</Badge> : null}
        </div>

        <nav className="flex flex-wrap gap-2">
          {ROLES.map((r) => (
            <Link
              key={r}
              href={`/champions/${championId}?role=${r}${rankTier ? `&rankTier=${encodeURIComponent(rankTier)}` : ""}`}
              className={`rounded-md border px-3 py-1 text-sm ${
                r === role
                  ? "border-primary/50 bg-primary/15 text-primary"
                  : "border-border/70 bg-white/5 text-fg/80 hover:bg-white/10"
              }`}
            >
              {r}
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
                      <td className="py-2 pr-4 font-medium">{w.role}</td>
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

                    <div className="flex items-center gap-1.5">
                      {b.primaryRunes?.[0] && runeById[String(b.primaryRunes[0])] ? (
                        <Image
                          src={runeIconUrl(runeById[String(b.primaryRunes[0])]!.icon)}
                          alt={runeById[String(b.primaryRunes[0])]!.name}
                          title={runeById[String(b.primaryRunes[0])]!.name}
                          width={22}
                          height={22}
                          className="rounded bg-black/20 p-0.5"
                        />
                      ) : null}
                      {styleById[String(b.primaryStyleId)] ? (
                        <Image
                          src={runeIconUrl(styleById[String(b.primaryStyleId)]!.icon)}
                          alt={styleById[String(b.primaryStyleId)]!.name}
                          title={styleById[String(b.primaryStyleId)]!.name}
                          width={22}
                          height={22}
                          className="rounded bg-black/20 p-0.5"
                        />
                      ) : null}
                      {styleById[String(b.subStyleId)] ? (
                        <Image
                          src={runeIconUrl(styleById[String(b.subStyleId)]!.icon)}
                          alt={styleById[String(b.subStyleId)]!.name}
                          title={styleById[String(b.subStyleId)]!.name}
                          width={22}
                          height={22}
                          className="rounded bg-black/20 p-0.5"
                        />
                      ) : null}
                    </div>
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

