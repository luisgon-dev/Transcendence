import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client/schema";

import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { Badge } from "@/components/ui/Badge";
import { DataBar } from "@/components/ui/DataBar";
import { ArrowCornerIcon } from "@/components/ui/icons";
import { LaneIcon } from "@/components/ui/LaneIcon";
import { fetchBackendJson } from "@/lib/backendCall";
import { cn } from "@/lib/cn";
import { getBackendBaseUrl } from "@/lib/env";
import { fetchLolAnalyticsStatus } from "@/lib/lolAnalyticsStatus";
import { DEFAULT_TIERLIST_RANK_TIER } from "@/lib/ranks";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";
import { normalizeTierListEntries } from "@/lib/tierlist";

type TierListResponse = components["schemas"]["TierListResponse"];

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
// repeated here — this list covers the other three surfaces.
const EXPLORE = [
  {
    href: "/lol/champions",
    name: "Champions",
    description: "Win rates, builds, runes, and matchups for every champion and role."
  },
  {
    href: "/lol/pro-builds",
    name: "Pro builds",
    description: "What pros actually buy, rush, and max this patch."
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
  lolTop: ReturnType<typeof normalizeTierListEntries>;
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
      lolTop: tierListRes.ok
        ? normalizeTierListEntries(tierListRes.body?.entries ?? []).slice(0, 5)
        : []
    };
  } catch {
    return { version: "", champions: {}, patch: null, lolTop: [] };
  }
}

export default async function LandingPage() {
  const { version, champions, patch, lolTop } = await loadHomeLiveData();
  const hasPicks = lolTop.length > 0;

  return (
    <div className="grid gap-8">
      <section className="page-hero p-6 sm:p-8 md:p-10">
        <h1 className="type-display max-w-3xl">Look up any League player, champion, or matchup.</h1>
        <p className="type-lead mt-5 max-w-2xl">
          Current-patch tier lists, builds, and live profiles, refreshed continuously from ranked
          games.
        </p>

        <div className="mt-8 grid gap-3">
          <GlobalSearchLauncher className="h-14 w-full max-w-2xl ring-offset-surface" />
          <p className="type-note text-muted">
            Prefer to browse?{" "}
            <Link
              href="/lol/tierlist"
              className={cn(
                "rounded-sm font-semibold text-primary transition-colors hover:text-primary/80",
                FOCUS_RING
              )}
            >
              Open the current-patch tier list
            </Link>
            .
          </p>
        </div>
      </section>

      <section
        className={cn("grid gap-5", hasPicks && "lg:grid-cols-[1.5fr_1fr] lg:items-start")}
      >
        {hasPicks ? (
          <div className="page-panel grid gap-4 p-5 sm:p-6">
            <div className="flex items-center justify-between gap-3">
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
                const name = champ?.name ?? `Champion ${entry.championId}`;
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
          <p className="type-kicker text-muted">Explore</p>
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
