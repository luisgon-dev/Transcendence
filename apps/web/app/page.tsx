import Image from "next/image";
import Link from "next/link";
import { cache, Suspense } from "react";
import type { components } from "@transcendence/api-client/schema";

import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { TierBadge } from "@/components/TierBadge";
import { UpdatedAgo } from "@/components/UpdatedAgo";
import { DataBar } from "@/components/ui/DataBar";
import { ArrowCornerIcon } from "@/components/ui/icons";
import { LaneIcon } from "@/components/ui/LaneIcon";
import { fetchBackendJson } from "@/lib/backendCall";
import { cn } from "@/lib/cn";
import {
  crawledPlatformCount,
  formatCompactCount,
  formatCount,
  type DatasetStats
} from "@/lib/datasetStats";
import { fetchDatasetStats } from "@/lib/datasetStatsServer";
import { getBackendBaseUrl } from "@/lib/env";
import { formatGames, formatPercent } from "@/lib/format";
import { championDisplayName } from "@/lib/gameDisplay";
import { selectStarterPicks } from "@/lib/homeGuidance";
import { fetchLolAnalyticsStatus } from "@/lib/lolAnalyticsStatus";
import { platformRegionToSlug } from "@/lib/lolRegions";
import { DEFAULT_TIERLIST_RANK_TIER } from "@/lib/ranks";
import { encodeRiotIdPath } from "@/lib/riotid";
import { LANE_ROLES } from "@/lib/roles";
import { getAccessTokenOrRefresh } from "@/lib/sessionToken";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";
import { normalizeTierListEntries } from "@/lib/tierlist";
import { getTrnClient } from "@/lib/trnClient";

type TierListResponse = components["schemas"]["TierListResponse"];
type RiotAccountLink = components["schemas"]["RiotAccountLinkDto"];
type LaneRole = (typeof LANE_ROLES)[number];

// Shared focus-ring treatment for the page's bare <Link>s. Globals set
// `*:focus-visible { outline: none }`, so every interactive element must paint
// its own ring; raw links would otherwise be invisible to keyboard users.
const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/35 focus-visible:ring-offset-2 focus-visible:ring-offset-surface";

// Links inside an overflow-hidden hairline grid paint their ring inset, or it gets clipped.
const FOCUS_RING_INSET =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary/45";

const INLINE_LINK = cn(
  "type-ui inline-flex shrink-0 items-center gap-1 whitespace-nowrap rounded-md px-1 -mx-1 py-0.5 font-semibold text-primary transition-colors hover:text-primary/80",
  FOCUS_RING
);

const PICK_ROW = cn(
  "group flex items-center gap-3 -mx-2 rounded-md px-2 transition-colors hover:bg-surface-2/55",
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/30 focus-visible:ring-offset-1 focus-visible:ring-offset-surface"
);

// The names players actually use for each lane ("mid", "bot"), and the noun the hero line needs.
const LANE_COPY: Record<LaneRole, { label: string; player: string }> = {
  TOP: { label: "Top", player: "top laner" },
  JUNGLE: { label: "Jungle", player: "jungler" },
  MIDDLE: { label: "Mid", player: "mid laner" },
  BOTTOM: { label: "Bot", player: "bot laner" },
  UTILITY: { label: "Support", player: "support" }
};

// A famous account that proves the profile surface works without making the
// visitor type anything. The hero search is how a visitor reaches their own.
const SAMPLE_PROFILE = {
  label: "Hide on bush#KR1",
  href: "/lol/summoners/kr/Hide%20on%20bush-KR1"
} as const;

// Entry points in the order a player reaches for them during one game. Tier list, champions,
// leaderboards, and builds live in the header nav and the lane board, so they are not repeated.
const GAME_PHASES = [
  {
    phase: "Champ select",
    title: "Scout your lobby",
    description: "Paste the lobby chat to compare ranks, roles, recent form, and champion pools.",
    href: "/lol/multi-search"
  },
  {
    phase: "Loading screen",
    title: "Read all ten players",
    description: "Ranks, recent form, champion pools, spells, and runes for the game in progress.",
    href: "/lol/live"
  },
  {
    phase: "Post-game",
    title: "Review the match",
    description: `Scoreboard, gold lead, and builds for every game in a profile. Try ${SAMPLE_PROFILE.label}.`,
    href: SAMPLE_PROFILE.href
  }
] as const;

type HomeLiveData = {
  version: string;
  champions: Awaited<ReturnType<typeof fetchChampionMap>>["champions"];
  patch: string | null;
  tierEntries: ReturnType<typeof normalizeTierListEntries>;
};

// The meta readout is best-effort. fetchChampionMap throws on a Data Dragon
// outage (unlike fetchBackendJson), so the whole batch is guarded — a CDN hiccup
// degrades the hero and lane board to their fallbacks instead of crashing the
// root page. Rank is pinned to the tier list's default (Emerald+) so the
// preview matches where its links lead.
async function loadHomeLiveData(): Promise<HomeLiveData> {
  try {
    const [{ version, champions }, analyticsStatus, tierListRes] = await Promise.all([
      fetchChampionMap(),
      fetchLolAnalyticsStatus(),
      fetchBackendJson<TierListResponse>(
        `${getBackendBaseUrl()}/api/lol/analytics/tierlist?rankTier=${DEFAULT_TIERLIST_RANK_TIER}`,
        { next: { revalidate: 60 * 60 } }
      )
    ]);

    return {
      version,
      champions,
      patch: analyticsStatus?.patch ?? tierListRes.body?.patch ?? null,
      tierEntries: tierListRes.ok ? normalizeTierListEntries(tierListRes.body?.entries ?? []) : []
    };
  } catch {
    return { version: "", champions: {}, patch: null, tierEntries: [] };
  }
}

const loadVerifiedMain = cache(async (): Promise<RiotAccountLink | null> => {
  try {
    const token = await getAccessTokenOrRefresh();
    if (!token.ok) return null;
    const { data } = await getTrnClient().GET("/api/users/me/riot-account", {
      headers: { authorization: `Bearer ${token.accessToken}` }
    });
    return (data as RiotAccountLink | undefined) ?? null;
  } catch {
    return null;
  }
});

async function MainAccountLink() {
  const main = await loadVerifiedMain();
  if (!main) return null;
  return (
    <Link
      href={`/lol/summoners/${platformRegionToSlug(main.platformRegion)}/${encodeRiotIdPath(main)}`}
      className={INLINE_LINK}
    >
      Jump to {main.gameName}#{main.tagLine}
      <ArrowCornerIcon className="h-3 w-3" />
    </Link>
  );
}

// The corpus behind every number on the page, as one quiet line under the hero. Figures the
// backend could not determine are left out rather than shown as zero.
function DatasetLine({ stats }: { stats: DatasetStats }) {
  const figure = "type-tabular font-semibold text-fg tabular-nums";
  return (
    <div className="relative mt-8 flex flex-wrap items-center justify-between gap-x-6 gap-y-3 border-t border-border/60 pt-5">
      <ul aria-label="Dataset" className="type-note flex flex-wrap gap-x-5 gap-y-1 text-muted">
        <li>
          <span className={figure}>{formatCount(stats.matchesStored)}</span> ranked matches
        </li>
        <li>
          <span className={figure}>{formatCount(stats.matchesLast24Hours)}</span> in the last day
        </li>
        {stats.playersIndexedEstimate != null ? (
          <li>
            <span className={figure}>
              <span aria-hidden="true">≈</span>
              <span className="sr-only">about </span>
              {formatCompactCount(stats.playersIndexedEstimate)}
            </span>{" "}
            players
          </li>
        ) : null}
        <li>
          <span className={figure}>{formatCount(crawledPlatformCount(stats))}</span> platforms
        </li>
        {stats.lastMatchIngestedAtUtc ? (
          <li>
            <UpdatedAgo timestamp={stats.lastMatchIngestedAtUtc} prefix="Last game" />
          </li>
        ) : null}
      </ul>
      <Link href="/about" className={INLINE_LINK}>
        How it&apos;s collected
        <ArrowCornerIcon className="h-3 w-3" />
      </Link>
    </div>
  );
}

export default async function LandingPage() {
  // Awaited with the rest of the page rather than streamed separately: the dataset line sits in the
  // hero, and a fallback that later collapsed (no snapshot yet) would shift everything below it.
  const [{ version, champions, patch, tierEntries }, datasetStats] = await Promise.all([
    loadHomeLiveData(),
    fetchDatasetStats()
  ]);

  // Thin samples never headline or rank here; strongest first, the same order the tier list uses.
  const strongest = tierEntries
    .filter((entry) => !entry.isLowSample)
    .sort((a, b) => b.strengthScore - a.strengthScore || b.games - a.games);
  const starters = new Map(selectStarterPicks(tierEntries, champions).map((p) => [p.role, p]));
  const lanes = LANE_ROLES.map((role) => ({
    role,
    picks: strongest.filter((entry) => entry.role === role).slice(0, 3),
    starter: starters.get(role)
  })).filter((lane) => lane.picks.length > 0);

  const lead = strongest[0];
  const leadChamp = lead ? champions[String(lead.championId)] : undefined;

  return (
    <div className="grid gap-8 md:gap-10">
      <section aria-labelledby="home-heading" className="landing-hero">
        {leadChamp ? (
          // The art is data too: it is always the strongest pick on the current patch.
          <div aria-hidden className="landing-hero-art">
            <Image
              src={`https://ddragon.leagueoflegends.com/cdn/img/champion/splash/${leadChamp.id}_0.jpg`}
              alt=""
              fill
              priority
              quality={75}
              sizes="(min-width: 1024px) 70vw, 100vw"
              className="object-cover object-[50%_22%]"
            />
          </div>
        ) : null}

        <div className={cn("relative max-w-xl lg:my-auto", leadChamp && "pt-32 sm:pt-40 lg:pt-0")}>
          {lead ? (
            <p className="type-kicker text-muted">
              {patch ? `Patch ${patch} · ` : null}Emerald+
            </p>
          ) : null}
          <h1 id="home-heading" className="type-display mt-4">
            {lead
              ? `${championDisplayName(leadChamp)} is the ${LANE_COPY[lead.role as LaneRole]?.player ?? "pick"} to beat.`
              : "Look yourself up."}
          </h1>

          {lead ? (
            <div className="mt-6 flex flex-wrap items-center gap-x-4 gap-y-2">
              <TierBadge tier={lead.tier} size="md" />
              <DataBar value={lead.winRate} games={lead.games} />
              <span className="type-note type-tabular text-muted tabular-nums">
                {formatGames(lead.games)} games · {formatPercent(lead.pickRate)} pick ·{" "}
                {formatPercent(lead.banRate)} ban
              </span>
              <Link
                href={`/lol/champions/${lead.championId}?role=${lead.role}`}
                className={INLINE_LINK}
              >
                Build and matchups
                <ArrowCornerIcon className="h-3 w-3" />
              </Link>
            </div>
          ) : null}

          <div className="mt-8 grid gap-3">
            <GlobalSearchLauncher className="h-14 w-full ring-offset-surface" />
            <div className="min-h-7">
              <Suspense fallback={null}>
                <MainAccountLink />
              </Suspense>
            </div>
          </div>
        </div>

        {datasetStats ? <DatasetLine stats={datasetStats} /> : null}
      </section>

      {lanes.length > 0 ? (
        <section aria-labelledby="lanes-heading" className="page-panel overflow-hidden">
          <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-2 px-5 pb-4 pt-5 sm:px-6">
            <div>
              <h2 id="lanes-heading" className="type-title text-fg">
                Strongest by lane
              </h2>
              <p className="type-note mt-1 text-muted">
                Graded against each lane&apos;s average, with thin samples pulled toward it.
              </p>
            </div>
            <Link href="/lol/tierlist" className={INLINE_LINK}>
              Full tier list
              <ArrowCornerIcon className="h-3 w-3" />
            </Link>
          </div>

          {/* gap-px over a border-colored track draws the hairlines in every wrap configuration;
              at lg the lanes flow into as many equal columns as there is data for. */}
          <div className="grid gap-px border-t border-border bg-border sm:grid-cols-2 lg:auto-cols-fr lg:grid-flow-col lg:grid-cols-none">
            {lanes.map(({ role, picks, starter }, laneIndex) => {
              // An odd lane out spans both columns of the two-up grid and keeps one-line rows there.
              const spans = laneIndex === lanes.length - 1 && lanes.length % 2 === 1;
              return (
                <div
                  key={role}
                  className={cn(
                    "reveal-up flex flex-col bg-surface p-4 sm:p-5",
                    spans && "sm:col-span-2 lg:col-span-1"
                  )}
                  style={{ animationDelay: `${laneIndex * 60}ms` }}
                >
                  <h3 className="type-ui mb-2 flex items-center gap-2 font-semibold text-fg/80">
                    <LaneIcon role={role} className="h-4 w-4 text-muted" />
                    {LANE_COPY[role].label}
                  </h3>
                  <ol className="grid gap-0.5">
                    {picks.map((entry, index) => {
                      const champ = champions[String(entry.championId)];
                      const featured = index === 0;
                      const size = featured ? 52 : 36;
                      return (
                        <li key={entry.championId}>
                          <Link
                            href={`/lol/champions/${entry.championId}?role=${entry.role}`}
                            className={cn(PICK_ROW, featured ? "py-2" : "py-1.5")}
                          >
                            <Image
                              src={championIconUrl(version, champ?.id ?? "Unknown")}
                              alt=""
                              width={size}
                              height={size}
                              sizes={`${size}px`}
                              className="shrink-0 rounded-md"
                            />
                            {/* One line while a lane has the full width; name over bar once lanes sit side by side. */}
                            <span
                              className={cn(
                                "flex min-w-0 flex-1 items-center justify-between gap-3",
                                spans ? "lg:grid lg:gap-1" : "sm:grid sm:gap-1"
                              )}
                            >
                              <span className="flex min-w-0 items-center gap-2">
                                <span
                                  className={cn(
                                    "truncate font-semibold text-fg transition-colors group-hover:text-primary",
                                    featured ? "type-section" : "type-ui"
                                  )}
                                >
                                  {championDisplayName(champ)}
                                </span>
                                <TierBadge tier={entry.tier} />
                                {entry.championId === starter?.championId ? (
                                  <span className="type-caption text-muted">Easy</span>
                                ) : null}
                              </span>
                              <DataBar value={entry.winRate} games={entry.games} />
                            </span>
                          </Link>
                        </li>
                      );
                    })}
                  </ol>

                  {starter && !picks.some((p) => p.championId === starter.championId) ? (
                    <div className="mt-auto pt-3">
                      <Link
                        href={`/lol/champions/${starter.championId}?role=${starter.role}`}
                        className={cn(
                          "group type-note flex items-center gap-2 rounded-sm border-t border-border/60 pt-3 text-muted",
                          FOCUS_RING
                        )}
                      >
                        Easy to pick up
                        <Image
                          src={championIconUrl(version, starter.champion.id)}
                          alt=""
                          width={22}
                          height={22}
                          sizes="22px"
                          className="rounded"
                        />
                        <span className="font-semibold text-fg transition-colors group-hover:text-primary">
                          {starter.champion.name}
                        </span>
                      </Link>
                    </div>
                  ) : null}
                </div>
              );
            })}
          </div>
        </section>
      ) : null}

      <section aria-labelledby="phases-heading" className="grid gap-4">
        <h2 id="phases-heading" className="type-title text-fg">
          From champ select to post-game
        </h2>
        <ol className="grid gap-px overflow-hidden rounded-panel border border-border bg-border md:grid-cols-3">
          {GAME_PHASES.map((step) => (
            <li key={step.href} className="max-w-none bg-surface">
              <Link
                href={step.href}
                className={cn(
                  "group flex h-full flex-col gap-1.5 p-5 transition-colors hover:bg-surface-2/55 sm:p-6",
                  FOCUS_RING_INSET
                )}
              >
                <span className="type-note text-muted">{step.phase}</span>
                <span className="type-title flex items-start justify-between gap-3 text-fg transition-colors group-hover:text-primary">
                  {step.title}
                  <ArrowCornerIcon className="mt-1.5 h-4 w-4 shrink-0 text-muted transition-[color,transform] duration-150 ease-[cubic-bezier(0.16,1,0.3,1)] group-hover:-translate-y-0.5 group-hover:translate-x-0.5 group-hover:text-primary motion-reduce:transform-none" />
                </span>
                <span className="type-note text-muted">{step.description}</span>
              </Link>
            </li>
          ))}
        </ol>
      </section>
    </div>
  );
}
