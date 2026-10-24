import Image from "next/image";
import Link from "next/link";

import { LiveGameCard } from "@/components/LiveGameCard";
import { Card } from "@/components/ui/Card";
import { DataBar } from "@/components/ui/DataBar";
import { Sparkline } from "@/components/ui/Sparkline";
import { formatPercent, plural, winRateColorClass } from "@/lib/format";
import { championDisplayName } from "@/lib/gameDisplay";
import { rankEmblemUrl, rankTierDisplayLabel, rankToLadderPoints } from "@/lib/ranks";
import { encodeRiotIdPath } from "@/lib/riotid";
import { championIconUrl } from "@/lib/staticData";

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

// One row vocabulary for every sidebar list: flat, a hover wash, a focus ring. No card-in-card.
const LIST_ROW =
  "group -mx-2 flex items-center gap-3 rounded-control px-2 py-2 transition-colors hover:bg-surface-2/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/45";

function SectionHeading({ children, aside }: { children: React.ReactNode; aside?: React.ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <h2 className="type-section text-fg">{children}</h2>
      {aside ? <span className="type-caption text-muted">{aside}</span> : null}
    </div>
  );
}

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
    <aside className="grid min-w-0 content-start gap-5 [&>*]:min-w-0 [&>*]:max-w-full xl:sticky xl:top-24">
      <Card className="profile-section-card p-5">
        <SectionHeading aside={`Updated ${profile.rankAge?.ageDescription ?? "recently"}`}>Ranked</SectionHeading>
        {rankedEntries.length === 0 ? (
          <p className="mt-4 text-sm text-fg/80">
            No ranked results yet. This player is currently unranked in Solo/Duo and Flex.
          </p>
        ) : (
          <div className="mt-3 grid divide-y divide-border/70">
            {rankedEntries.map(({ label, rank }) => {
              const emblem = rankEmblemUrl(rank.tier);
              const totalGames = rank.wins + rank.losses;
              const wr = totalGames > 0 ? (rank.wins / totalGames) * 100 : null;

              return (
                <div key={label} className="grid grid-cols-[72px_minmax(0,1fr)] items-center gap-3 py-3 first:pt-1">
                  {emblem ? (
                    // The communitydragon ranked emblem sits in a large mostly-transparent
                    // canvas (~22% content, centered), so we crop the padding by scaling the
                    // centered crest up inside an overflow-hidden frame.
                    <div className="flex size-[72px] items-center justify-center overflow-hidden">
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
                    <div className="size-[72px] rounded-control border border-border/60 bg-surface-2" />
                  )}
                  <div className="min-w-0">
                    <p className="type-caption text-muted">{label}</p>
                    <p className={`truncate text-lg font-semibold ${rankColorClass(rank.tier)}`}>
                      {rankTierDisplayLabel(rank.tier)} {rank.division}
                    </p>
                    <p className="type-tabular mt-0.5 flex flex-wrap items-center gap-x-2 text-xs tabular-nums text-fg/72">
                      <span>{rank.leaguePoints} LP</span>
                      <span>
                        {rank.wins}W {rank.losses}L
                      </span>
                      {wr != null ? (
                        <span className={winRateColorClass(wr)}>
                          {formatPercent(wr, { input: "percent", decimals: 1 })}
                        </span>
                      ) : null}
                    </p>
                  </div>
                </div>
              );
            })}
          </div>
        )}
        {soloSeries.length >= 2 ? (
          <div className="mt-2 grid gap-2.5 border-t border-border/70 pt-4">
            <div className="flex items-start justify-between gap-3">
              <div>
                <p className="type-caption font-semibold text-fg/85">Solo/Duo climb</p>
                <p className="type-caption text-muted">
                  {soloSeries.length} snapshots · {new Date(soloProgression[0].recordedAt).toLocaleDateString(undefined, { month: "short", day: "numeric" })}–{new Date(soloProgression.at(-1)!.recordedAt).toLocaleDateString(undefined, { month: "short", day: "numeric" })}
                </p>
              </div>
              <p className={soloDelta != null && soloDelta >= 0 ? "type-caption font-semibold text-success" : "type-caption font-semibold text-danger"}>
                {soloDelta != null && soloDelta >= 0 ? "+" : ""}{soloDelta} LP-equivalent
              </p>
            </div>
            <Sparkline values={soloSeries} width={240} height={44} className="h-11 w-full" />
            <p className="type-caption text-muted">
              Observed rank changes, normalized across divisions; not a Riot per-game LP ledger.
            </p>
            <ol className="sr-only">
              {soloProgression.map((point, index) => (
                <li key={`${point.recordedAt}-${index}`}>{new Date(point.recordedAt).toLocaleDateString()}: {point.label}</li>
              ))}
            </ol>
          </div>
        ) : null}

        {unrankedQueues.length > 0 && rankedEntries.length > 0 ? (
          <p className="type-caption mt-2 text-muted">{unrankedQueues.join(" · ")}</p>
        ) : null}
      </Card>

      {/* Time-sensitive scouting belongs immediately after rank context, before mastery/pool/duo
          exploration on mobile and desktop. The card auto-checks on mount. */}
      <LiveGameCard region={region} gameName={gameName} tagLine={tagLine} />

      <Card className="profile-section-card p-5">
        <SectionHeading>Most played</SectionHeading>
        <div className="mt-2 grid">
          {(profile.topChampions ?? []).slice(0, 6).map((championStat) => {
            const champion = championStatic?.champions[String(championStat.championId)];
            return (
              <Link key={championStat.championId} href={`/lol/champions/${championStat.championId}`} className={LIST_ROW}>
                {champion && championStatic ? (
                  <Image
                    src={championIconUrl(championStatic.version, champion.id)}
                    alt=""
                    width={34}
                    height={34}
                    className="shrink-0 rounded-md border border-border/50"
                  />
                ) : (
                  <span className="size-[34px] shrink-0 rounded-md border border-border/50 bg-surface-2" />
                )}
                {/* The bar sits under the name: beside it, a 20rem sidebar leaves the name ~70px. */}
                <span className="grid min-w-0 flex-1 gap-1">
                  <span className="truncate text-sm font-semibold text-fg group-hover:text-primary">
                    {championDisplayName(champion)}
                  </span>
                  <DataBar value={championStat.winRate} games={championStat.games} />
                </span>
                <span className="type-caption type-tabular grid shrink-0 text-right tabular-nums text-muted">
                  <span>
                    {championStat.games} {plural(championStat.games, "game")}
                  </span>
                  <span>{championStat.kdaRatio.toFixed(2)} KDA</span>
                </span>
              </Link>
            );
          })}
        </div>
      </Card>

      {(profile.topMastery?.length ?? 0) > 0 ? (
        <Card className="profile-section-card p-5">
          <SectionHeading>Mastery</SectionHeading>
          <div className="mt-2 grid">
            {profile.topMastery!.map((m) => {
              const champion = championStatic?.champions[String(m.championId)];
              const highMastery = m.championLevel >= 7;
              return (
                <Link key={m.championId} href={`/lol/champions/${m.championId}`} className={LIST_ROW}>
                  {champion && championStatic ? (
                    <Image
                      src={championIconUrl(championStatic.version, champion.id)}
                      alt=""
                      width={30}
                      height={30}
                      className="shrink-0 rounded-md border border-border/50"
                    />
                  ) : (
                    <span className="size-[30px] shrink-0 rounded-md border border-border/50 bg-surface-2" />
                  )}
                  <span className="min-w-0 flex-1 truncate text-sm font-medium text-fg group-hover:text-primary">
                    {championDisplayName(champion)}
                  </span>
                  <span className="type-caption type-tabular tabular-nums text-muted">
                    {m.championPoints.toLocaleString()}
                  </span>
                  <span
                    className={`type-caption w-9 text-right font-semibold tabular-nums ${highMastery ? "text-fg" : "text-muted"}`}
                  >
                    M{m.championLevel}
                  </span>
                </Link>
              );
            })}
          </div>
        </Card>
      ) : null}

      {(profile.frequentlyPlayedWith?.length ?? 0) > 0 ? (
        <Card className="profile-section-card p-5">
          <SectionHeading>Played with</SectionHeading>
          <div className="mt-2 grid">
            {profile.frequentlyPlayedWith!.map((mate) => {
              // Only show a same-team win-rate once the sample is meaningful (avoids "100% · 1 game" noise).
              const wr = mate.sameTeamGames >= 2 ? (mate.sameTeamWins / mate.sameTeamGames) * 100 : null;
              return (
                <Link
                  key={mate.summonerId}
                  href={`/lol/summoners/${region}/${encodeRiotIdPath({ gameName: mate.gameName, tagLine: mate.tagLine })}`}
                  className={LIST_ROW}
                >
                  <span className="grid min-w-0 flex-1">
                    <span className="truncate text-sm font-medium text-fg group-hover:text-primary">
                      {mate.gameName}
                      <span className="text-muted">#{mate.tagLine}</span>
                    </span>
                    <span className="type-caption text-muted">
                      {mate.gamesTogether} {plural(mate.gamesTogether, "game")} together
                      {mate.sameTeamGames > 0 ? ` · ${mate.sameTeamGames} as duo` : ""}
                    </span>
                  </span>
                  {wr != null ? <DataBar value={wr} games={mate.sameTeamGames} /> : null}
                </Link>
              );
            })}
          </div>
        </Card>
      ) : null}
    </aside>
  );
}
