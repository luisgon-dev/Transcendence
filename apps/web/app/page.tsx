import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client/schema";

import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { Badge } from "@/components/ui/Badge";
import { buttonClassName } from "@/components/ui/buttonStyles";
import { Card } from "@/components/ui/Card";
import { DataBar } from "@/components/ui/DataBar";
import { ArrowCornerIcon } from "@/components/ui/icons";
import { LaneIcon } from "@/components/ui/LaneIcon";
import { fetchBackendJson } from "@/lib/backendCall";
import { cn } from "@/lib/cn";
import { getBackendBaseUrl } from "@/lib/env";
import { TFT_FRONTEND_ENABLED } from "@/lib/featureFlags";
import { fetchLolAnalyticsStatus } from "@/lib/lolAnalyticsStatus";
import { DEFAULT_TIERLIST_RANK_TIER } from "@/lib/ranks";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";
import { normalizeTierListEntries } from "@/lib/tierlist";
import type { TftCompListItem } from "@/lib/tft";

type TierListResponse = components["schemas"]["TierListResponse"];

// Shared focus-ring treatment for the page's bare <Link>s. Globals set
// `*:focus-visible { outline: none }`, so every interactive element must paint
// its own ring; raw links would otherwise be invisible to keyboard users.
const FOCUS_RING =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/35 focus-visible:ring-offset-2 focus-visible:ring-offset-surface";

// "See all" link at the top of each live-now column.
const SEE_ALL_LINK = cn(
  "type-ui inline-flex items-center gap-1 rounded-md px-1 -mx-1 py-0.5 font-semibold text-primary transition-colors hover:text-primary/80",
  FOCUS_RING
);

// Live-now rows behave like a compact interactive list (hover wash + ring),
// not faint hairline-separated text.
const LIVE_ROW = cn(
  "group flex items-center gap-2.5 -mx-2 rounded-md px-2 py-2 transition-colors hover:bg-surface-2/55",
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/30 focus-visible:ring-offset-1 focus-visible:ring-offset-surface"
);

// Underline-style quick link in the game cards.
const QUICK_LINK = cn(
  "type-ui inline-flex items-center rounded-sm border-b border-border/45 px-0.5 pb-1 text-fg/76 transition-colors hover:border-primary/40 hover:text-fg",
  FOCUS_RING
);

const EXAMPLE_PROFILE = {
  label: "Hide on bush#KR1",
  lolHref: "/lol/summoners/kr/Hide%20on%20bush-KR1",
  tftHref: "/tft/summoners/kr/Hide%20on%20bush-KR1"
} as const;

const lolGame = {
  href: "/lol",
  name: "League of Legends",
  description: "Tier lists, champion builds, matchups, pro builds, and player profiles.",
  links: [
    { label: "Tier List", href: "/lol/tierlist" },
    { label: "Champions", href: "/lol/champions" },
    { label: EXAMPLE_PROFILE.label, href: EXAMPLE_PROFILE.lolHref }
  ]
} as const;

const tftGame = {
  href: "/tft",
  name: "Teamfight Tactics",
  description: "Meta comps, unit and item lookups, augments, traits, and player history.",
  links: [
    { label: "Comps", href: "/tft/comps" },
    { label: "Units", href: "/tft/champions" },
    { label: EXAMPLE_PROFILE.label, href: EXAMPLE_PROFILE.tftHref }
  ]
} as const;

type HomeLiveData = {
  version: string;
  champions: Awaited<ReturnType<typeof fetchChampionMap>>["champions"];
  patch: string | null;
  lolTop: ReturnType<typeof normalizeTierListEntries>;
  tftTop: TftCompListItem[];
};

// The "Live now" strip is best-effort. fetchChampionMap throws on a Data Dragon
// outage (unlike fetchBackendJson), so the whole batch is guarded — a CDN hiccup
// degrades the strip to nothing instead of crashing the root page. Rank is
// pinned to the tier list's default (Emerald+) so the preview matches where its
// links lead.
async function loadHomeLiveData(): Promise<HomeLiveData> {
  try {
    const [{ version, champions }, analyticsStatus, tierListRes, tftCompsRes] = await Promise.all([
      fetchChampionMap(),
      fetchLolAnalyticsStatus(),
      fetchBackendJson<TierListResponse>(
        `${getBackendBaseUrl()}/api/lol/analytics/tierlist?rankTier=${DEFAULT_TIERLIST_RANK_TIER}`,
        { next: { revalidate: 60 * 60 } }
      ),
      TFT_FRONTEND_ENABLED
        ? fetchBackendJson<TftCompListItem[]>(`${getBackendBaseUrl()}/api/tft/analytics/comps`, {
            next: { revalidate: 60 * 10 }
          })
        : Promise.resolve(null)
    ]);

    return {
      version,
      champions,
      patch: analyticsStatus?.patch ?? tierListRes.body?.patch ?? null,
      lolTop: tierListRes.ok
        ? normalizeTierListEntries(tierListRes.body?.entries ?? []).slice(0, 3)
        : [],
      tftTop: tftCompsRes && tftCompsRes.ok ? (tftCompsRes.body ?? []).slice(0, 3) : []
    };
  } catch {
    return { version: "", champions: {}, patch: null, lolTop: [], tftTop: [] };
  }
}

export default async function LandingPage() {
  const visibleGames = TFT_FRONTEND_ENABLED ? [lolGame, tftGame] : [lolGame];
  const { version, champions, patch, lolTop, tftTop } = await loadHomeLiveData();
  const hasLive = lolTop.length > 0 || tftTop.length > 0;
  const bothColumns = lolTop.length > 0 && tftTop.length > 0;

  return (
    <div className="grid gap-8">
      <section className="page-hero p-6 sm:p-8 md:p-10">
        <h1 className="type-display max-w-4xl">
          Fast League and TFT lookups for ranked players.
        </h1>
        <p className="type-lead mt-5">
          Current-patch analytics, refreshed continuously from ranked games.
        </p>

        <div className="mt-7 grid gap-3 sm:grid-cols-2 sm:items-stretch">
          <Link
            href="/lol/tierlist"
            className={buttonClassName({ className: "h-12 w-full ring-offset-surface" })}
          >
            Open LoL tier list
          </Link>
          <GlobalSearchLauncher className="h-12 w-full px-4 text-left ring-offset-surface" />
        </div>
      </section>

      {hasLive ? (
        <section className="page-panel grid gap-4 p-5 sm:p-6">
          <p className="type-kicker text-muted">Live now</p>

          <div className={cn("grid gap-5", bothColumns && "sm:grid-cols-2 sm:gap-0")}>
            {lolTop.length > 0 ? (
              <div className={cn("grid gap-3", bothColumns && "sm:pr-6")}>
                <div className="flex items-center justify-between gap-3">
                  <span className="inline-flex flex-wrap items-center gap-2">
                    <span className="type-kicker text-muted">League · Top picks</span>
                    {patch ? <Badge>Patch {patch}</Badge> : null}
                  </span>
                  <Link href="/lol/tierlist" className={SEE_ALL_LINK}>
                    Tier list
                    <ArrowCornerIcon className="h-3 w-3" />
                  </Link>
                </div>
                <div className="grid">
                  {lolTop.map((entry) => {
                    const champ = champions[String(entry.championId)];
                    const name = champ?.name ?? `Champion ${entry.championId}`;
                    return (
                      <Link
                        key={`${entry.championId}-${entry.role}`}
                        href={`/lol/champions/${entry.championId}?role=${entry.role}`}
                        className={LIVE_ROW}
                      >
                        <Image
                          src={championIconUrl(version, champ?.id ?? "Unknown")}
                          alt=""
                          width={26}
                          height={26}
                          sizes="26px"
                          className="rounded-md"
                        />
                        <span className="type-ui min-w-0 flex-1 truncate text-fg">{name}</span>
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

            {tftTop.length > 0 ? (
              <div
                className={cn(
                  "grid gap-3",
                  bothColumns && "sm:border-l sm:border-border/50 sm:pl-6"
                )}
              >
                <div className="flex items-center justify-between gap-3">
                  <p className="type-kicker text-muted">TFT · Top comps</p>
                  <Link href="/tft/comps" className={SEE_ALL_LINK}>
                    Comps
                    <ArrowCornerIcon className="h-3 w-3" />
                  </Link>
                </div>
                <div className="grid">
                  {tftTop.map((comp) => (
                    <Link key={comp.compSlug} href={`/tft/comps/${comp.compSlug}`} className={LIVE_ROW}>
                      <span className="type-ui min-w-0 flex-1 truncate text-fg">{comp.name}</span>
                      <span className="type-note shrink-0 text-muted">
                        Avg{" "}
                        <span className="type-tabular tabular-nums font-semibold text-fg/80">
                          {comp.avgPlacement.toFixed(2)}
                        </span>
                      </span>
                    </Link>
                  ))}
                </div>
              </div>
            ) : null}
          </div>
        </section>
      ) : null}

      <section className="grid gap-5 lg:grid-cols-2">
        {visibleGames.map((game) => (
          <Card key={game.href} className="grid gap-4 p-6">
            <div className="grid gap-2">
              <Link
                href={game.href}
                className={cn(
                  "group inline-flex w-fit items-center gap-2 rounded-md text-fg transition-colors hover:text-primary",
                  FOCUS_RING
                )}
              >
                <span className="type-title">{game.name}</span>
                <ArrowCornerIcon className="h-4 w-4 shrink-0 text-muted transition-[color,transform] duration-150 ease-[cubic-bezier(0.16,1,0.3,1)] group-hover:-translate-y-0.5 group-hover:translate-x-0.5 group-hover:text-primary motion-reduce:transform-none" />
              </Link>
              <p className="type-ui text-muted">{game.description}</p>
            </div>
            <div className="flex flex-wrap gap-x-4 gap-y-2 border-t border-border/40 pt-4">
              {game.links.map((link) => (
                <Link key={link.href} href={link.href} className={QUICK_LINK}>
                  {link.label}
                </Link>
              ))}
            </div>
          </Card>
        ))}
      </section>
    </div>
  );
}
