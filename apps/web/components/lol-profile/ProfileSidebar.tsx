import Image from "next/image";
import Link from "next/link";

import { LiveGameCard } from "@/components/LiveGameCard";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { DataBar } from "@/components/ui/DataBar";
import { Sparkline } from "@/components/ui/Sparkline";
import { championIconUrl } from "@/lib/staticData";
import { formatPercent, plural, winRateColorClass } from "@/lib/format";
import { rankEmblemUrl, rankTierDisplayLabel, rankToLadderPoints } from "@/lib/ranks";
import { encodeRiotIdPath } from "@/lib/riotid";

import {
  rankColorClass,
  type ChampionStatic,
  type RankHistoryEntry,
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
  rankHistory,
  region,
  gameName,
  tagLine
}: {
  profile: SummonerProfileResponse;
  championStatic: ChampionStatic | null;
  rankedEntries: RankedEntry[];
  unrankedQueues: string[];
  rankHistory: RankHistoryEntry[] | null;
  region: string;
  gameName: string;
  tagLine: string;
}) {
  // Solo/Duo progression uses the durable app-observed snapshots, normalized across
  // tier/division boundaries into one monotonic ladder-points scale.
  const soloRank = rankedEntries.find((entry) => entry.label === "Solo/Duo")?.rank;
  const soloHistoryPoints = (rankHistory ?? [])
    .filter((entry) => (entry.queueType ?? "").toUpperCase().includes("SOLO"))
    .map((entry) => ({
      value: rankToLadderPoints(entry.tier, entry.rankNumber, entry.leaguePoints),
      label: `${rankTierDisplayLabel(entry.tier)} ${entry.rankNumber ?? ""} · ${entry.leaguePoints} LP`,
      recordedAt: entry.dateRecorded
    }))
    .filter((point): point is { value: number; label: string; recordedAt: string } => point.value != null);
  const currentLadderPoints = soloRank
    ? rankToLadderPoints(soloRank.tier, soloRank.division, soloRank.leaguePoints)
    : null;
  const lastObserved = soloHistoryPoints.at(-1)?.value;
  const soloProgression = currentLadderPoints != null && currentLadderPoints !== lastObserved
    ? [
        ...soloHistoryPoints,
        {
          value: currentLadderPoints,
          label: `${rankTierDisplayLabel(soloRank?.tier)} ${soloRank?.division ?? ""} · ${soloRank?.leaguePoints ?? 0} LP (current)`,
          recordedAt: new Date().toISOString()
        }
      ]
    : soloHistoryPoints;
  const soloSeries = soloProgression.map((point) => point.value);
  const soloDelta = soloSeries.length >= 2 ? soloSeries.at(-1)! - soloSeries[0] : null;

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
                    // The communitydragon ranked emblem sits in a large mostly-transparent
                    // canvas (~22% content, centered), so we crop the padding by scaling the
                    // centered crest up inside an overflow-hidden frame.
                    <div className="flex h-24 w-24 items-center justify-center overflow-hidden rounded-control border border-border/45 bg-surface/65">
                      <Image
                        src={emblem}
                        alt={`${rankTierDisplayLabel(rank.tier)} emblem`}
                        width={256}
                        height={256}
                        sizes="256px"
                        className="h-full w-full origin-center scale-[3.3] select-none object-contain"
                      />
                    </div>
                  ) : (
                    <div className="h-24 w-24 rounded-control border border-border/60 bg-surface/70" />
                  )}
                  <div className="min-w-0">
                    <p className="type-kicker text-muted">{label}</p>
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
        {soloSeries.length >= 2 ? (
          <div className="surface-subtle mt-4 grid gap-3 rounded-card px-3 py-3">
            <div className="flex items-start justify-between gap-3">
              <div>
                <p className="type-overline text-muted">Solo/Duo progression</p>
                <p className="type-caption text-muted">
                  {soloSeries.length} observed snapshots · {new Date(soloProgression[0].recordedAt).toLocaleDateString(undefined, { month: "short", day: "numeric" })}–{new Date(soloProgression.at(-1)!.recordedAt).toLocaleDateString(undefined, { month: "short", day: "numeric" })}
                </p>
              </div>
              <p className={soloDelta != null && soloDelta >= 0 ? "type-caption font-semibold text-success" : "type-caption font-semibold text-danger"}>
                {soloDelta != null && soloDelta >= 0 ? "+" : ""}{soloDelta} LP-equivalent
              </p>
            </div>
            <Sparkline values={soloSeries} width={240} height={44} className="h-11 w-full" />
            <div>
              <p className="type-note text-muted">
                Observed rank changes, normalized across divisions; not a Riot per-game LP ledger.
              </p>
              <ol className="sr-only">
                {soloProgression.map((point, index) => (
                  <li key={`${point.recordedAt}-${index}`}>{new Date(point.recordedAt).toLocaleDateString()}: {point.label}</li>
                ))}
              </ol>
            </div>
          </div>
        ) : null}

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

      {(profile.topMastery?.length ?? 0) > 0 ? (
        <Card className="profile-section-card p-5">
          <div>
            <p className="type-kicker text-muted">Mastery</p>
            <h2 className="mt-2 type-section">Top mastery</h2>
          </div>
          <div className="mt-4 grid gap-2">
            {profile.topMastery!.map((m) => {
              const champion = championStatic?.champions[String(m.championId)];
              const highMastery = m.championLevel >= 7;
              return (
                <Link
                  key={m.championId}
                  href={`/lol/champions/${m.championId}`}
                  className="surface-subtle group flex items-center gap-3 rounded-control px-3 py-2.5 transition hover:border-border-strong hover:bg-surface-2/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/45"
                >
                  {champion && championStatic ? (
                    <Image
                      src={championIconUrl(championStatic.version, champion.id)}
                      alt={champion.name}
                      width={32}
                      height={32}
                      className="rounded-lg border border-border/50"
                    />
                  ) : (
                    <div className="h-8 w-8 rounded-lg border border-border/50 bg-surface/70" />
                  )}
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium text-fg group-hover:text-primary">
                      {champion?.name ?? `Champion ${m.championId}`}
                    </p>
                    <p className="type-caption text-muted tabular-nums">{m.championPoints.toLocaleString()} pts</p>
                  </div>
                  <span
                    className={`type-overline rounded-full border px-2 py-0.5 tabular-nums ${
                      highMastery ? "border-border-strong bg-surface-2 font-semibold text-fg" : "border-border/50 text-muted"
                    }`}
                  >
                    M{m.championLevel}
                  </span>
                </Link>
              );
            })}
          </div>
        </Card>
      ) : null}

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
                className="surface-subtle group grid gap-2 rounded-control px-3 py-3 transition hover:border-border-strong hover:bg-surface-2/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/45"
              >
                <div className="flex items-center justify-between gap-3">
                  <div>
                    <p className="type-kicker text-muted">#{index + 1}</p>
                    <p className="mt-1 text-sm font-semibold text-fg group-hover:text-primary">
                      {champion?.name ?? `Champion ${championStat.championId}`}
                    </p>
                  </div>
                  <DataBar value={championStat.winRate} games={championStat.games} />
                </div>
                <div className="flex items-center justify-between gap-2 text-xs text-muted">
                  <span>{championStat.games} {plural(championStat.games, "game")} tracked</span>
                  <span>{championStat.kdaRatio.toFixed(2)} KDA</span>
                </div>
              </Link>
            );
          })}
        </div>
      </Card>

      {(profile.frequentlyPlayedWith?.length ?? 0) > 0 ? (
        <Card className="profile-section-card p-5">
          <div>
            <p className="type-kicker text-muted">Duo signals</p>
            <h2 className="mt-2 type-section">Played with</h2>
          </div>
          <div className="mt-4 grid gap-2">
            {profile.frequentlyPlayedWith!.map((mate) => {
              // Only show a same-team win-rate once the sample is meaningful (avoids "100% · 1 game" noise).
              const wr = mate.sameTeamGames >= 2 ? (mate.sameTeamWins / mate.sameTeamGames) * 100 : null;
              return (
                <Link
                  key={mate.summonerId}
                  href={`/lol/summoners/${region}/${encodeRiotIdPath({ gameName: mate.gameName, tagLine: mate.tagLine })}`}
                  className="surface-subtle group grid gap-1.5 rounded-control px-3 py-2.5 transition hover:border-border-strong hover:bg-surface-2/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/45"
                >
                  <div className="flex items-center justify-between gap-3">
                    <p className="min-w-0 truncate text-sm font-medium text-fg group-hover:text-primary">
                      {mate.gameName}
                      <span className="text-muted">#{mate.tagLine}</span>
                    </p>
                    {wr != null ? <DataBar value={wr} games={mate.sameTeamGames} /> : null}
                  </div>
                  <div className="flex items-center justify-between gap-2 text-xs text-muted">
                    <span>{mate.gamesTogether} {plural(mate.gamesTogether, "game")} together</span>
                    {mate.sameTeamGames > 0 ? <span>{mate.sameTeamGames} as duo</span> : null}
                  </div>
                </Link>
              );
            })}
          </div>
        </Card>
      ) : null}

      <LiveGameCard region={region} gameName={gameName} tagLine={tagLine} />
    </aside>
  );
}
