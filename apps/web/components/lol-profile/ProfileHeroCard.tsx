import Image from "next/image";

import { FavoriteButton } from "@/components/FavoriteButton";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { formatPercent } from "@/lib/format";
import { profileIconUrl } from "@/lib/staticData";

import {
  friendlyAcceptedMessage,
  rankColorClass,
  type AcceptedResponse,
  type ApiErrorResponse,
  type ChampionStatic,
  type PagedResultDto,
  type ProfileChampionStat,
  type RankInfo,
  type SummonerProfileResponse,
  type MatchSummary
} from "@/components/lol-profile/shared";
import { rankTierDisplayLabel } from "@/lib/ranks";

type QuickStats = {
  total: number;
  winRate: number;
  avgKda: number;
};

type RankedEntry = {
  label: string;
  rank: RankInfo;
};

export function ProfileHeroCard({
  title,
  region,
  gameName,
  tagLine,
  profile,
  backgroundUrl,
  championStatic,
  history,
  dataAge,
  rankedEntries,
  quickStats,
  recentForm,
  accepted,
  error,
  featuredChampion,
  featuredChampionName,
  busy,
  onRefresh
}: {
  title: string;
  region: string;
  gameName: string;
  tagLine: string;
  profile: SummonerProfileResponse | null;
  backgroundUrl?: string | null;
  championStatic: ChampionStatic | null;
  history: PagedResultDto<MatchSummary> | null;
  dataAge: string;
  rankedEntries: RankedEntry[];
  quickStats: QuickStats | null;
  recentForm: boolean[];
  accepted: AcceptedResponse | null;
  error: ApiErrorResponse | null;
  featuredChampion?: ProfileChampionStat;
  featuredChampionName: string | null;
  busy: boolean;
  onRefresh: () => void;
}) {
  const primaryRank = rankedEntries[0]?.rank;

  return (
    <Card className="profile-hero-card relative overflow-hidden rounded-hero p-5 md:p-8">
      {backgroundUrl ? (
        <div
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 opacity-25"
          style={{
            backgroundImage: `linear-gradient(to right, var(--t-bg) 12%, color-mix(in oklch, var(--t-bg), transparent 35%) 55%, transparent 100%), url(${backgroundUrl})`,
            backgroundSize: "cover",
            backgroundPosition: "top right"
          }}
        />
      ) : null}
      <div className="relative grid gap-6 xl:grid-cols-[minmax(0,1.35fr)_minmax(300px,0.78fr)] xl:items-end">
        <div className="grid gap-5">
          <div className="flex min-w-0 flex-col gap-5 sm:flex-row sm:items-center">
            {profile && championStatic ? (
              <Image
                src={profileIconUrl(championStatic.version, profile.profileIconId)}
                alt={`${title} icon`}
                width={88}
                height={88}
                className="rounded-panel border border-border/80 shadow-media"
              />
            ) : (
              <div className="h-[88px] w-[88px] rounded-panel border border-border/70 bg-surface/70" />
            )}
            <div className="min-w-0">
              <p className="type-kicker text-muted">League profile</p>
              <h1 className="type-hero-title mt-2 truncate">
                {title}
              </h1>
              <p className="mt-2 type-ui text-fg/78">
                {profile ? `Level ${profile.summonerLevel} · ${dataAge}` : region.toUpperCase()}
              </p>
              <div className="mt-4 flex flex-wrap gap-2">
                <span className="profile-stat-pill">
                  <span className="type-kicker text-muted">Region</span>
                  <span className="type-ui text-fg">{region.toUpperCase()}</span>
                </span>
                <span className="profile-stat-pill">
                  <span className="type-kicker text-muted">Ranked</span>
                  <span className={`type-ui ${rankColorClass(primaryRank?.tier)}`}>
                    {primaryRank
                      ? `${rankTierDisplayLabel(primaryRank.tier)} ${primaryRank.division}`
                      : "Unranked"}
                  </span>
                </span>
                {quickStats ? (
                  <span className="profile-stat-pill">
                    <span className="type-kicker text-muted">Recent WR</span>
                    <span className="type-ui text-fg">{formatPercent(quickStats.winRate)}</span>
                  </span>
                ) : null}
              </div>
            </div>
          </div>

          {recentForm.length > 0 ? (
            <div className="grid gap-2">
              <div className="flex items-center justify-between gap-3">
                <p className="type-kicker text-fg/68">Recent Form</p>
                <p className="text-xs text-fg/55">Latest {recentForm.length} games</p>
              </div>
              <div className="flex flex-wrap items-center gap-1.5" aria-label="Recent match outcomes (latest first)">
                {recentForm.map((win, idx) => (
                  <span
                    key={`${win ? "w" : "l"}-${idx}`}
                    className={`h-3 w-9 rounded-full transition-transform duration-200 ${
                      win ? "bg-success/75" : "bg-danger/70"
                    }`}
                    aria-label={win ? "Win" : "Loss"}
                    title={win ? "Win" : "Loss"}
                  />
                ))}
              </div>
            </div>
          ) : null}

          {accepted?.message ? (
            <p className="rounded-card border border-info/30 bg-info/10 px-4 py-3 text-sm text-fg/84">
              {friendlyAcceptedMessage(accepted.message)}
            </p>
          ) : null}
          {error?.message ? (
            <p className="rounded-card border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
              {error.message}
            </p>
          ) : null}
        </div>

        <div className="surface-card grid gap-3 rounded-panel p-4">
          <div className="flex items-center justify-between gap-3">
            <div>
              <p className="type-kicker text-muted">Snapshot</p>
              <p className="mt-1 text-sm text-fg/66">Fast read on current ranked form.</p>
            </div>
            <Badge className="surface-chip text-fg/72">
              {history ? `${history.totalCount.toLocaleString()} tracked` : "Awaiting history"}
            </Badge>
          </div>
          <div className="grid gap-3 sm:grid-cols-3 xl:grid-cols-1">
            <div className="profile-metric-tile">
              <p className="type-kicker text-muted">Ranked</p>
              <p className={`mt-2 text-xl font-semibold ${rankColorClass(primaryRank?.tier)}`}>
                {primaryRank
                  ? `${rankTierDisplayLabel(primaryRank.tier)} ${primaryRank.division}`
                  : "Unranked"}
              </p>
              <p className="mt-1 text-sm text-fg/66">
                {primaryRank ? `${primaryRank.leaguePoints} LP` : "No ranked ladder games yet"}
              </p>
            </div>
            <div className="profile-metric-tile">
              <p className="type-kicker text-muted">Recent sample</p>
              <p className="mt-2 text-xl font-semibold text-fg">
                {quickStats ? formatPercent(quickStats.winRate) : "Pending"}
              </p>
              <p className="mt-1 text-sm text-fg/66">
                {quickStats
                  ? `${quickStats.total} games · ${quickStats.avgKda.toFixed(2)} avg KDA`
                  : "Waiting for recent matches"}
              </p>
            </div>
            <div className="profile-metric-tile">
              <p className="type-kicker text-muted">Champion focus</p>
              <p className="mt-2 text-xl font-semibold text-fg">{featuredChampionName ?? "Loading"}</p>
              <p className="mt-1 text-sm text-fg/66">
                {featuredChampion
                  ? `${featuredChampion.games} games · ${formatPercent(featuredChampion.winRate)} win rate`
                  : "Top champion pool updating"}
              </p>
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-2 pt-1">
            <Button variant="outline" onClick={onRefresh} disabled={busy}>
              {busy ? "Starting..." : "Update Now"}
            </Button>
            <FavoriteButton region={region} gameName={gameName} tagLine={tagLine} />
          </div>
        </div>
      </div>
    </Card>
  );
}
