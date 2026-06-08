import Image from "next/image";
import Link from "next/link";

import { LiveGameCard } from "@/components/LiveGameCard";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { DataBar } from "@/components/ui/DataBar";
import { formatPercent, winRateColorClass } from "@/lib/format";
import { rankEmblemUrl, rankTierDisplayLabel } from "@/lib/ranks";

import {
  rankColorClass,
  type ChampionStatic,
  type RankInfo,
  type SummonerProfileResponse
} from "@/components/lol-profile/shared";

type RankedEntry = {
  label: string;
  rank: RankInfo;
};

export function ProfileSidebar({
  profile,
  championStatic,
  rankedEntries,
  unrankedQueues,
  region,
  gameName,
  tagLine
}: {
  profile: SummonerProfileResponse;
  championStatic: ChampionStatic | null;
  rankedEntries: RankedEntry[];
  unrankedQueues: string[];
  region: string;
  gameName: string;
  tagLine: string;
}) {
  return (
    <aside className="grid content-start gap-5 xl:sticky xl:top-24">
      <Card className="profile-section-card p-5">
        <div className="flex items-start justify-between gap-3">
          <div>
            <p className="type-kicker text-muted">Ranked snapshot</p>
            <h2 className="mt-2 type-section">Solo/Duo &amp; Flex</h2>
          </div>
          <Badge className="surface-chip text-fg/72">
            {profile.rankAge?.ageDescription ?? "updated recently"}
          </Badge>
        </div>
        {rankedEntries.length === 0 ? (
          <p className="mt-4 text-sm text-fg/80">
            No ranked results yet. This player is currently unranked in Solo/Duo and Flex.
          </p>
        ) : (
          <div className="mt-4 grid gap-4">
            {rankedEntries.map(({ label, rank }) => {
              const emblem = rankEmblemUrl(rank.tier);
              const totalGames = rank.wins + rank.losses;
              const wr = totalGames > 0 ? (rank.wins / totalGames) * 100 : null;

              return (
                <div
                  key={label}
                  className="surface-subtle grid gap-3 rounded-card px-3 py-3 sm:grid-cols-[96px_minmax(0,1fr)] sm:items-center"
                >
                  {emblem ? (
                    <div className="flex h-24 w-24 items-center justify-center rounded-control border border-border/45 bg-surface/65 p-1.5">
                      <Image
                        src={emblem}
                        alt={`${rankTierDisplayLabel(rank.tier)} emblem`}
                        width={96}
                        height={96}
                        sizes="96px"
                        className="h-full w-full select-none object-contain"
                      />
                    </div>
                  ) : (
                    <div className="h-24 w-24 rounded-control border border-border/60 bg-surface/70" />
                  )}
                  <div className="min-w-0">
                    <p className="type-kicker text-fg/62">{label}</p>
                    <p className={`mt-2 truncate text-lg font-semibold ${rankColorClass(rank.tier)}`}>
                      {rankTierDisplayLabel(rank.tier)} {rank.division}
                    </p>
                    <div className="mt-2 flex flex-wrap items-center gap-2 text-xs text-fg/72">
                      <span>{rank.leaguePoints} LP</span>
                      <span>{rank.wins}W {rank.losses}L</span>
                      {wr != null ? (
                        <span className={winRateColorClass(wr)}>
                          {formatPercent(wr, { input: "percent", decimals: 1 })}
                        </span>
                      ) : null}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}
        {unrankedQueues.length > 0 ? (
          <div className="mt-3 grid gap-1">
            {unrankedQueues.map((label) => (
              <p key={label} className="text-xs text-fg/72">
                {label}
              </p>
            ))}
          </div>
        ) : null}
      </Card>

      <Card className="profile-section-card p-5">
        <div>
          <p className="type-kicker text-muted">Champion pool</p>
          <h2 className="mt-2 type-section">Most played</h2>
        </div>
        <div className="mt-4 grid gap-3">
          {(profile.topChampions ?? []).slice(0, 6).map((championStat, index) => {
            const champion = championStatic?.champions[String(championStat.championId)];
            return (
              <Link
                key={championStat.championId}
                href={`/lol/champions/${championStat.championId}`}
                className="surface-subtle group grid gap-2 rounded-control px-3 py-3 transition hover:border-border-strong hover:bg-surface-2/60"
              >
                <div className="flex items-center justify-between gap-3">
                  <div>
                    <p className="type-kicker text-fg/55">#{index + 1}</p>
                    <p className="mt-1 text-sm font-semibold text-fg group-hover:text-primary">
                      {champion?.name ?? championStat.championName}
                    </p>
                  </div>
                  <DataBar value={championStat.winRate} />
                </div>
                <div className="flex items-center justify-between gap-2 text-xs text-fg/64">
                  <span>{championStat.games} games tracked</span>
                  <span>{championStat.kdaRatio.toFixed(2)} KDA</span>
                </div>
              </Link>
            );
          })}
        </div>
      </Card>

      <LiveGameCard region={region} gameName={gameName} tagLine={tagLine} />
    </aside>
  );
}
