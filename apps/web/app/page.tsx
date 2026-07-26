import Image from "next/image";
import Link from "next/link";
import { cache, Suspense } from "react";
import type { components } from "@transcendence/api-client/schema";

import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { Badge } from "@/components/ui/Badge";
import { DataBar } from "@/components/ui/DataBar";
import { ArrowCornerIcon } from "@/components/ui/icons";
import { LaneIcon } from "@/components/ui/LaneIcon";
import { fetchBackendJson } from "@/lib/backendCall";
import { cn } from "@/lib/cn";
import { getBackendBaseUrl } from "@/lib/env";
import { championDisplayName } from "@/lib/gameDisplay";
import { selectStarterPicks } from "@/lib/homeGuidance";
import { fetchLolAnalyticsStatus } from "@/lib/lolAnalyticsStatus";
import { platformRegionToSlug } from "@/lib/lolRegions";
import { DEFAULT_TIERLIST_RANK_TIER } from "@/lib/ranks";
import { encodeRiotIdPath } from "@/lib/riotid";
import { roleDisplayLabel } from "@/lib/roles";
import { getAccessTokenOrRefresh } from "@/lib/sessionToken";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";
import { normalizeTierListEntries } from "@/lib/tierlist";
import { getTrnClient } from "@/lib/trnClient";

type TierListResponse = components["schemas"]["TierListResponse"];
type RiotAccountLink = components["schemas"]["RiotAccountLinkDto"];

// Shared focus-ring treatment for the page's bare <Link>s. Globals set
// `*:focus-visible { outline: none }`, so every interactive element must paint
// its own ring; raw links would otherwise be invisible to keyboard users.
const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/35 focus-visible:ring-offset-2 focus-visible:ring-offset-surface";

// "See all" link in a panel header.
const SEE_ALL_LINK = cn(
  "type-ui inline-flex shrink-0 items-center gap-1 whitespace-nowrap rounded-md px-1 -mx-1 py-0.5 font-semibold text-primary transition-colors hover:text-primary/80",
  FOCUS_RING
);

// Top-picks rows behave like a compact interactive list (hover wash + ring),
// not faint hairline-separated text.
const PICK_ROW = cn(
  "group flex items-center gap-2.5 -mx-2 rounded-md px-2 py-2 transition-colors hover:bg-surface-2/55",
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/30 focus-visible:ring-offset-1 focus-visible:ring-offset-surface"
);

// A famous account that proves the profile surface works without making the
// visitor type anything. It is a sample, not the destination index — the hero
// search is how a visitor reaches their own profile.
const SAMPLE_PROFILE = {
  label: "Hide on bush#KR1",
  href: "/lol/summoners/kr/Hide%20on%20bush-KR1"
} as const;

// The League surfaces a visitor can browse from the home page. Tier List is the
// hero's secondary action and the picks-panel link, so it is intentionally not
// repeated here.
const EXPLORE = [
  {
    href: "/lol/champions",
    name: "Champions",
    description: "Win rates, builds, runes, and matchups for every champion and role."
  },
  {
    href: "/lol/leaderboards",
    name: "Leaderboards",
    description: "Regional ladders and champion specialists across ranked queues."
  },
  {
    href: "/lol/pro-builds",
    name: "Pro solo queue",
    description: "What tracked pros buy, rush, and max in ranked solo queue this patch."
  },
  {
    href: "/lol/items",
    name: "Items & runes",
    description: "Explore pick rates, outcomes, and the champions that use each build choice."
  },
  {
    href: SAMPLE_PROFILE.href,
    name: "Player profiles",
    description: `Match history, mastery, and live games for any summoner. Try ${SAMPLE_PROFILE.label}.`
  }
] as const;

type HomeLiveData = {
  version: string;
  champions: Awaited<ReturnType<typeof fetchChampionMap>>["champions"];
  patch: string | null;
  tierEntries: ReturnType<typeof normalizeTierListEntries>;
};

// The "Top picks" strip is best-effort. fetchChampionMap throws on a Data Dragon
// outage (unlike fetchBackendJson), so the whole batch is guarded — a CDN hiccup
// degrades the strip to nothing instead of crashing the root page. Rank is
// pinned to the tier list's default (Emerald+) so the preview matches where its
// links lead.
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

function TierListBrowseLink() {
  return (
    <Link
      href="/lol/tierlist"
      className={cn(
        "rounded-sm font-semibold text-primary transition-colors hover:text-primary/80",
        FOCUS_RING
      )}
    >
      Open the current-patch tier list
    </Link>
  );
}

async function HomeWelcome() {
  const verifiedMain = await loadVerifiedMain();
  return (
    <div className="mb-3 min-h-4">
      {verifiedMain ? (
        <p className="type-kicker text-primary">Welcome back, {verifiedMain.gameName}</p>
      ) : null}
    </div>
  );
}

function HomeBrowsePromptFallback() {
  return (
    <p className="type-note text-muted">
      Prefer to browse? <TierListBrowseLink />.
    </p>
  );
}

async function HomeBrowsePrompt() {
  const verifiedMain = await loadVerifiedMain();
  return (
    <p className="type-note text-muted">
      {verifiedMain ? (
        <>
          <Link
            href={`/lol/summoners/${platformRegionToSlug(verifiedMain.platformRegion)}/${encodeRiotIdPath(verifiedMain)}`}
            className={cn(
              "rounded-sm font-semibold text-primary transition-colors hover:text-primary/80",
              FOCUS_RING
            )}
          >
            Open {verifiedMain.gameName}#{verifiedMain.tagLine}
          </Link>{" "}
          or browse the{" "}
        </>
      ) : (
        <>Prefer to browse? </>
      )}
      <TierListBrowseLink />.
    </p>
  );
}

export default async function LandingPage() {
  const { version, champions, patch, tierEntries } = await loadHomeLiveData();
  const lolTop = tierEntries.slice(0, 5);
  const starterPicks = selectStarterPicks(tierEntries, champions);
  const hasPicks = lolTop.length > 0;

  return (
    <div className="grid gap-8">
      <section className="page-hero p-6 sm:p-8 md:p-10">
        <Suspense fallback={<div aria-hidden className="mb-3 min-h-4" />}>
          <HomeWelcome />
        </Suspense>
        <h1 className="type-display max-w-3xl">Look up any League player or champion.</h1>
        <p className="type-lead mt-5 max-w-2xl">
          Current-patch tier lists, builds, and live profiles, refreshed continuously from ranked
          games.
        </p>

        <div className="mt-8 grid gap-3">
          <GlobalSearchLauncher className="h-14 w-full max-w-2xl ring-offset-surface" />
          <Suspense fallback={<HomeBrowsePromptFallback />}>
            <HomeBrowsePrompt />
          </Suspense>
        </div>
      </section>

      {starterPicks.length > 0 ? (
        <section aria-labelledby="starter-picks-heading" className="page-panel p-5 sm:p-6">
          <div className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
            <div>
              <p className="type-kicker text-muted">New to ranked</p>
              <h2 id="starter-picks-heading" className="type-section mt-1 text-fg">
                Approachable picks for every role
              </h2>
            </div>
            <p className="type-note max-w-xl text-muted sm:text-right">
              Suggestions favor lower-complexity champions with a stable sample, then rank them by
              strength against their role peers.
            </p>
          </div>

          <div className="mt-4 grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
            {starterPicks.map((pick) => (
              <Link
                key={pick.role}
                href={`/lol/champions/${pick.championId}?role=${pick.role}`}
                className={cn(
                  "group flex items-center gap-3 rounded-lg border border-border/55 bg-surface-2/35 p-3 transition-colors hover:border-border hover:bg-surface-2/70",
                  FOCUS_RING
                )}
              >
                <Image
                  src={championIconUrl(version, pick.champion.id)}
                  alt=""
                  width={38}
                  height={38}
                  sizes="38px"
                  className="rounded-md"
                />
                <span className="min-w-0">
                  <span className="type-note flex items-center gap-1.5 text-muted">
                    <LaneIcon role={pick.role} className="h-3.5 w-3.5" />
                    {roleDisplayLabel(pick.role)}
                  </span>
                  <span className="type-ui mt-0.5 block truncate font-semibold text-fg group-hover:text-primary">
                    {pick.champion.name}
                  </span>
                </span>
              </Link>
            ))}
          </div>

          <p className="type-note mt-4 border-t border-border/45 pt-4 text-muted">
            How tiers work: win rates are adjusted toward each role&apos;s baseline when samples are
            small, then graded by the remaining performance gap. Pick and ban pressure are shown
            separately.
          </p>
        </section>
      ) : null}

      <section
        className={cn("grid gap-5", hasPicks && "lg:grid-cols-[1.5fr_1fr] lg:items-start")}
      >
        {hasPicks ? (
          <div className="page-panel grid gap-4 p-5 sm:p-6">
            <div className="flex min-h-7 items-center justify-between gap-3">
              <span className="inline-flex flex-wrap items-center gap-2">
                <span className="type-kicker text-muted">Top picks · Emerald+</span>
                {patch ? <Badge>Patch {patch}</Badge> : null}
              </span>
              <Link href="/lol/tierlist" className={SEE_ALL_LINK}>
                Tier list
                <ArrowCornerIcon className="h-3 w-3" />
              </Link>
            </div>
            <div className="grid">
              {lolTop.map((entry, index) => {
                const champ = champions[String(entry.championId)];
                const name = championDisplayName(champ);
                return (
                  <Link
                    key={`${entry.championId}-${entry.role}`}
                    href={`/lol/champions/${entry.championId}?role=${entry.role}`}
                    className={PICK_ROW}
                  >
                    <span className="type-tabular w-4 shrink-0 text-right text-sm font-semibold tabular-nums text-muted">
                      {index + 1}
                    </span>
                    <Image
                      src={championIconUrl(version, champ?.id ?? "Unknown")}
                      alt=""
                      width={30}
                      height={30}
                      sizes="30px"
                      className="rounded-md"
                    />
                    <span className="type-ui min-w-0 flex-1 truncate font-medium text-fg">{name}</span>
                    <LaneIcon
                      role={entry.role}
                      className="h-3.5 w-3.5 shrink-0 text-fg/65 transition-colors group-hover:text-fg/85"
                    />
                    <span className="sr-only">{roleDisplayLabel(entry.role)} lane, </span>
                    <DataBar value={entry.winRate} games={entry.games} decimals={1} />
                  </Link>
                );
              })}
            </div>
          </div>
        ) : null}

        <div className="page-panel grid content-start gap-4 p-5 sm:p-6">
          <p className="type-kicker flex min-h-7 items-center text-muted">Explore</p>
          <div className="grid">
            {EXPLORE.map((dest, index) => (
              <Link
                key={dest.href}
                href={dest.href}
                className={cn(
                  "group flex items-start gap-3 -mx-2 rounded-md px-2 py-4 transition-colors hover:bg-surface-2/55",
                  index > 0 && "border-t border-border/40",
                  FOCUS_RING
                )}
              >
                <span className="min-w-0 flex-1">
                  <span className="type-section text-fg transition-colors group-hover:text-primary">
                    {dest.name}
                  </span>
                  <span className="type-note mt-0.5 block text-muted">{dest.description}</span>
                </span>
                <ArrowCornerIcon className="mt-1 h-4 w-4 shrink-0 text-muted transition-[color,transform] duration-150 ease-[cubic-bezier(0.16,1,0.3,1)] group-hover:-translate-y-0.5 group-hover:translate-x-0.5 group-hover:text-primary motion-reduce:transform-none" />
              </Link>
            ))}
          </div>
        </div>
      </section>
    </div>
  );
}
