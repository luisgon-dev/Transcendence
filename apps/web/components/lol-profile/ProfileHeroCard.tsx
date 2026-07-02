import Image from "next/image";

import { FavoriteButton } from "@/components/FavoriteButton";
import { Button } from "@/components/ui/Button";
import { Toolbar } from "@/components/ui/Toolbar";
import { formatPercent } from "@/lib/format";
import { profileIconUrl } from "@/lib/staticData";

import {
  friendlyAcceptedMessage,
  rankColorClass,
  type AcceptedResponse,
  type ApiErrorResponse,
  type ChampionStatic,
  type RankInfo,
  type SummonerProfileResponse
} from "@/components/lol-profile/shared";
import { rankTierDisplayLabel } from "@/lib/ranks";

type RankedEntry = {
  label: string;
  rank: RankInfo;
};

// Flat, answer-first header (the "Ladder" Toolbar primitive) — no splash art, no
// frosted-glass metric panel. Identity + rank-at-a-glance + the primary action live
// on one compact line; the full ranked breakdown is progressive-disclosed in the
// sidebar, so rank appears exactly once here.
export function ProfileHeroCard({
  title,
  region,
  gameName,
  tagLine,
  profile,
  championStatic,
  dataAge,
  rankedEntries,
  recentForm,
  accepted,
  error,
  busy,
  onRefresh
}: {
  title: string;
  region: string;
  gameName: string;
  tagLine: string;
  profile: SummonerProfileResponse | null;
  championStatic: ChampionStatic | null;
  dataAge: string;
  rankedEntries: RankedEntry[];
  recentForm: boolean[];
  accepted: AcceptedResponse | null;
  error: ApiErrorResponse | null;
  busy: boolean;
  onRefresh: () => void;
}) {
  const primaryRank = rankedEntries[0]?.rank;
  const rankText = primaryRank
    ? `${rankTierDisplayLabel(primaryRank.tier)} ${primaryRank.division}`
    : "Unranked";

  // Win rate is derived from — and labelled against — the same recent-form window
  // as the pips below, so it never reads as (and never contradicts) the ranked win
  // rate broken out in the sidebar.
  const recentWins = recentForm.filter(Boolean).length;
  const recentWr =
    recentForm.length > 0 ? formatPercent(recentWins / recentForm.length) : null;

  return (
    <div className="flex flex-col gap-3">
      <Toolbar
        eyebrow="League profile"
        title={
          <span className="flex items-center gap-3">
            {profile && championStatic ? (
              <Image
                src={profileIconUrl(championStatic.version, profile.profileIconId)}
                alt={`${title} icon`}
                width={48}
                height={48}
                className="rounded-xl border border-border/70 shadow-soft"
              />
            ) : (
              <span className="h-12 w-12 rounded-xl border border-border/60 bg-surface/70" />
            )}
            <span className="truncate">{title}</span>
          </span>
        }
        meta={
          <>
            <span>Level {profile?.summonerLevel ?? "—"}</span>
            <span>{region.toUpperCase()}</span>
            <span className={rankColorClass(primaryRank?.tier)}>
              {rankText}
              {primaryRank ? ` · ${primaryRank.leaguePoints} LP` : ""}
            </span>
          </>
        }
        actions={
          <>
            <Button variant="outline" onClick={onRefresh} disabled={busy}>
              {busy ? "Starting…" : "Update Now"}
            </Button>
            <FavoriteButton region={region} gameName={gameName} tagLine={tagLine} />
          </>
        }
        filters={
          recentForm.length > 0 ? (
            <>
              <span className="type-overline text-muted">Recent form</span>
              {/* Wins render solid, losses render outlined: a fill/shape cue that
                  survives red/green color blindness, with color as a redundant
                  signal. The whole strip is one labelled image for screen readers;
                  the individual pips are decorative (aria-hidden). */}
              <div
                className="flex flex-wrap items-center gap-1"
                role="img"
                aria-label={`Recent form, latest first: ${recentForm
                  .map((win) => (win ? "Win" : "Loss"))
                  .join(", ")}`}
              >
                {recentForm.map((win, idx) => (
                  <span
                    key={`${win ? "w" : "l"}-${idx}`}
                    aria-hidden="true"
                    className={`h-2.5 w-7 rounded-full ${
                      win ? "bg-win/80" : "border border-loss/70 bg-loss/25"
                    }`}
                    title={win ? "Win" : "Loss"}
                  />
                ))}
              </div>
              <span className="type-caption ml-auto text-muted">
                Last {recentForm.length}
                {recentWr ? ` · ${recentWr} WR` : ""} · {dataAge}
              </span>
            </>
          ) : undefined
        }
      />

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
  );
}
