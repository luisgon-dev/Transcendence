import Image from "next/image";
import Link from "next/link";
import type { components } from "@transcendence/api-client/schema";

import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { DataBar } from "@/components/ui/DataBar";
import { LaneIcon } from "@/components/ui/LaneIcon";
import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl } from "@/lib/env";
import { TFT_FRONTEND_ENABLED } from "@/lib/featureFlags";
import { fetchLolAnalyticsStatus } from "@/lib/lolAnalyticsStatus";
import { DEFAULT_TIERLIST_RANK_TIER } from "@/lib/ranks";
import { roleDisplayLabel } from "@/lib/roles";
import { championIconUrl, fetchChampionMap } from "@/lib/staticData";
import { normalizeTierListEntries } from "@/lib/tierlist";
import type { TftCompListItem } from "@/lib/tft";

type TierListResponse = components["schemas"]["TierListResponse"];

const EXAMPLE_PROFILE = {
  label: "Hide on bush#KR1",
  lolHref: "/lol/summoners/kr/Hide%20on%20bush-KR1",
  tftHref: "/tft/summoners/kr/Hide%20on%20bush-KR1"
} as const;

const lolGame = {
  href: "/lol",
  eyebrow: "League of Legends",
  title: "Tier lists, champion builds, matchups, pro builds, and player profiles.",
  links: [
    { label: "Tier List", href: "/lol/tierlist" },
    { label: "Champions", href: "/lol/champions" },
    { label: EXAMPLE_PROFILE.label, href: EXAMPLE_PROFILE.lolHref }
  ]
} as const;

const tftGame = {
  href: "/tft",
  eyebrow: "Teamfight Tactics",
  title: "Meta comps, unit and item lookups, augments, traits, and player history.",
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

  return (
    <div className="grid gap-8">
      <section className="page-hero p-6 sm:p-8 md:p-10">
        <p className="type-kicker text-muted">Transcendence</p>
        <h1 className="type-display mt-4 max-w-4xl">
          Fast League and TFT lookups for ranked players.
        </h1>

        <div className="mt-7 grid gap-3 sm:grid-cols-2 sm:items-stretch">
          <Link
            href="/lol/tierlist"
            className="type-ui inline-flex min-h-12 w-full items-center justify-center rounded-control bg-primary px-5 py-3 font-semibold text-primary-fg transition hover:bg-primary/92"
          >
            Open LoL tier list
          </Link>
          <GlobalSearchLauncher className="h-12 w-full px-4 text-left" />
        </div>
      </section>

      {hasLive ? (
        <section className="page-panel grid gap-4 p-5 sm:p-6">
          <p className="type-kicker text-fg/58">Live now</p>

          <div
            className={`grid gap-5 ${
              lolTop.length > 0 && tftTop.length > 0 ? "sm:grid-cols-2" : ""
            }`}
          >
            {lolTop.length > 0 ? (
              <div className="grid gap-3">
                <div className="flex items-center justify-between gap-3">
                  <span className="inline-flex items-center gap-2">
                    <span className="type-kicker text-fg/58">League · Top picks</span>
                    {patch ? (
                      <Badge className="border-primary/40 bg-primary/10 text-primary">
                        Patch {patch}
                      </Badge>
                    ) : null}
                  </span>
                  <Link href="/lol/tierlist" className="type-ui font-semibold text-primary hover:underline">
                    Tier list
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
                        className="flex items-center gap-2.5 border-b border-border/20 py-2 transition last:border-0 hover:border-border/45"
                      >
                        <Image
                          src={championIconUrl(version, champ?.id ?? "Unknown")}
                          alt={name}
                          width={26}
                          height={26}
                          sizes="26px"
                          className="rounded-md"
                        />
                        <span className="type-ui min-w-0 flex-1 truncate text-fg">{name}</span>
                        <LaneIcon role={entry.role} className="h-3.5 w-3.5 shrink-0 text-fg/45" />
                        <span className="sr-only">{roleDisplayLabel(entry.role)} lane, </span>
                        <DataBar value={entry.winRate} decimals={1} />
                      </Link>
                    );
                  })}
                </div>
              </div>
            ) : null}

            {tftTop.length > 0 ? (
              <div className="grid gap-3">
                <div className="flex items-center justify-between gap-3">
                  <p className="type-kicker text-fg/58">TFT · Top comps</p>
                  <Link href="/tft/comps" className="type-ui font-semibold text-primary hover:underline">
                    Comps
                  </Link>
                </div>
                <div className="grid">
                  {tftTop.map((comp) => (
                    <Link
                      key={comp.compSlug}
                      href={`/tft/comps/${comp.compSlug}`}
                      className="flex items-center gap-2.5 border-b border-border/20 py-2 transition last:border-0 hover:border-border/45"
                    >
                      <span className="type-ui min-w-0 flex-1 truncate text-fg">{comp.name}</span>
                      <span className="text-xs text-muted">Avg {comp.avgPlacement.toFixed(2)}</span>
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
            <div>
              <p className="type-kicker text-muted">{game.eyebrow}</p>
              <Link
                href={game.href}
                className="type-title mt-3 inline-block text-fg transition hover:text-primary"
              >
                {game.title}
              </Link>
            </div>
            <div className="flex flex-wrap gap-x-4 gap-y-2 border-t border-border/25 pt-4">
              {game.links.map((link) => (
                <Link
                  key={link.href}
                  href={link.href}
                  className="type-ui inline-flex items-center border-b border-border/45 px-0.5 pb-1 text-fg/76 transition hover:border-primary/35 hover:text-fg"
                >
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
