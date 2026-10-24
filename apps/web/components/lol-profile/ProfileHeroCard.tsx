import Image from "next/image";

import { FavoriteButton } from "@/components/FavoriteButton";
import { Button } from "@/components/ui/Button";
import { Skeleton } from "@/components/ui/Skeleton";
import { formatPercent, winRateColorClass } from "@/lib/format";
import { profileIconUrl } from "@/lib/staticData";

import {
  rankColorClass,
  RECENT_FORM_SLOTS,
  type RefreshNotice,
  type ApiErrorResponse,
  type ChampionStatic,
  type RankInfo,
  type SummonerProfileResponse
} from "@/components/lol-profile/shared";
import { rankEmblemUrl, rankTierDisplayLabel } from "@/lib/ranks";

type RankedEntry = {
  label: string;
  rank: RankInfo;
};

// Identity header. Like the League client's profile banner, it carries a splash of the
// player's signature champion (top mastery, else most played) behind the name, masked into the
// surface. Rank sits on the identity line once; the full ranked breakdown stays in the sidebar.
// Every row exists from first paint with fixed geometry, so data arriving never reflows the page.
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
  accepted: RefreshNotice | null;
  error: ApiErrorResponse | null;
  busy: boolean;
  onRefresh: () => void;
}) {
  const primary = rankedEntries[0];
  const primaryRank = primary?.rank;
  const emblem = rankEmblemUrl(primaryRank?.tier);
  const rankGames = primaryRank ? primaryRank.wins + primaryRank.losses : 0;
  const rankWr = rankGames > 0 ? (primaryRank!.wins / rankGames) * 100 : null;

  const signatureChampionId =
    profile?.topMastery?.[0]?.championId ?? profile?.topChampions?.[0]?.championId ?? null;
  const signatureChampion =
    signatureChampionId != null ? championStatic?.champions[String(signatureChampionId)] : undefined;

  // Win rate is derived from — and labelled against — the same recent-form window
  // as the pips below, so it never reads as (and never contradicts) the ranked win
  // rate on the identity line.
  const recentWins = recentForm.filter(Boolean).length;
  const recentWr =
    recentForm.length > 0 ? formatPercent(recentWins / recentForm.length) : null;

  return (
    <div className="flex flex-col gap-3">
      <header className="toolbar relative isolate overflow-hidden px-4 py-4 sm:px-5">
        {signatureChampion ? (
          <div aria-hidden className="splash-backdrop">
            <Image
              src={`https://ddragon.leagueoflegends.com/cdn/img/champion/splash/${signatureChampion.id}_0.jpg`}
              alt=""
              fill
              priority
              quality={55}
              sizes="(min-width: 1024px) 60rem, 100vw"
              className="object-cover object-[50%_20%]"
            />
          </div>
        ) : null}

        <div className="flex flex-wrap items-center gap-x-5 gap-y-4">
          <div className="relative shrink-0">
            {profile && championStatic ? (
              <Image
                src={profileIconUrl(championStatic.version, profile.profileIconId)}
                alt={`${title} icon`}
                width={72}
                height={72}
                priority
                className="size-[72px] rounded-card border border-border/70 shadow-soft"
              />
            ) : (
              <span className="block size-[72px] rounded-card border border-border/60 bg-surface-2" />
            )}
            <span className="type-caption absolute -bottom-2 left-1/2 min-w-9 -translate-x-1/2 rounded-md border border-border bg-surface px-1.5 text-center font-semibold tabular-nums text-fg">
              {profile?.summonerLevel ?? "—"}
            </span>
          </div>

          <div className="min-w-0 flex-1 basis-56">
            <h1 className="type-page-title truncate text-fg">
              {gameName}
              <span className="font-semibold text-muted">#{tagLine}</span>
            </h1>
            <p className="type-ui mt-1.5 flex min-h-8 flex-wrap items-center gap-x-3 gap-y-1">
              {emblem ? (
                // The ranked emblem sits in a mostly transparent canvas; scale the crest up inside
                // a clipped frame, the same crop the sidebar uses.
                <span className="flex size-8 shrink-0 items-center justify-center overflow-hidden">
                  <Image
                    src={emblem}
                    alt=""
                    width={128}
                    height={128}
                    sizes="128px"
                    className="size-full scale-[3.3] select-none object-contain"
                  />
                </span>
              ) : null}
              <span className={`font-semibold ${rankColorClass(primaryRank?.tier)}`}>
                {primaryRank ? `${rankTierDisplayLabel(primaryRank.tier)} ${primaryRank.division}` : "Unranked"}
              </span>
              {primaryRank ? (
                <span className="type-tabular tabular-nums text-fg/85">{primaryRank.leaguePoints} LP</span>
              ) : null}
              {primaryRank ? (
                <span className="type-tabular tabular-nums text-muted">
                  {primaryRank.wins}W {primaryRank.losses}L
                  {rankWr != null ? (
                    <span className={`ml-1.5 ${winRateColorClass(rankWr)}`}>
                      {formatPercent(rankWr, { input: "percent", decimals: 1 })}
                    </span>
                  ) : null}
                </span>
              ) : null}
              <span className="text-muted">
                {primary ? `${primary.label} · ` : ""}
                {region.toUpperCase()}
              </span>
            </p>
          </div>

          <div className="flex shrink-0 flex-wrap items-center gap-2">
            <Button variant="outline" onClick={onRefresh} disabled={busy}>
              {busy ? "Starting…" : "Update Now"}
            </Button>
            <FavoriteButton region={region} gameName={gameName} tagLine={tagLine} />
          </div>
        </div>

        {/* Gated on the profile, not on recentForm: form is derived from match history, which is
            fetched on the client and is empty on the first paint. The row exists for the whole
            lifetime of a profile and only its contents swap, so its arrival never shifts the page
            (this route's CLS was p75 0.77 when the row was inserted late). */}
        {profile ? (
          <div className="mt-4 flex flex-wrap items-center gap-x-3 gap-y-2 border-t border-border/70 pt-3">
            <span className="type-caption font-semibold text-muted">Last {RECENT_FORM_SLOTS}</span>
            {/* Wins render solid, losses render outlined: a fill/shape cue that survives red/green
                color blindness, with color as a redundant signal. The whole strip is one labelled
                image for screen readers; the individual pips are decorative (aria-hidden). */}
            {recentForm.length > 0 ? (
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
            ) : (
              // Same pip geometry as the loaded strip, so the swap is a repaint and not a reflow.
              <div className="flex flex-wrap items-center gap-1" aria-hidden="true">
                {Array.from({ length: RECENT_FORM_SLOTS }).map((_, idx) => (
                  <Skeleton key={idx} className="h-2.5 w-7 rounded-full" />
                ))}
              </div>
            )}
            {/* Same element and typography in both states so the line box is identical. */}
            <span className="type-caption ml-auto text-muted">
              {recentForm.length > 0 && recentWr ? `${recentWr} WR · ${dataAge}` : dataAge}
            </span>
          </div>
        ) : null}
      </header>

      {accepted?.message ? (
        <p className="rounded-card border border-info/30 bg-info/10 px-4 py-3 text-sm text-fg/84">
          {accepted.message}
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
