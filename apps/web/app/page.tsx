import Link from "next/link";

import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { Card } from "@/components/ui/Card";
import { TFT_FRONTEND_ENABLED } from "@/lib/featureFlags";

const EXAMPLE_PROFILE = {
  label: "Hide on bush#KR1",
  lolHref: "/lol/summoners/kr/Hide%20on%20bush-KR1",
  tftHref: "/tft/summoners/kr/Hide%20on%20bush-KR1"
} as const;

const games = [
  {
    href: "/lol",
    eyebrow: "League of Legends",
    title: "Tier lists, builds, matchup tools, pro builds, and player profiles.",
    description:
      "Open the current board, jump to a champion page, or pull up a player profile in a few clicks.",
    starter:
      "Best first move: open the tier list when you need a fast read on what is strong right now.",
    links: [
      { label: "Tier List", href: "/lol/tierlist" },
      { label: "Champions", href: "/lol/champions" },
      { label: `Open ${EXAMPLE_PROFILE.label}`, href: EXAMPLE_PROFILE.lolHref }
    ]
  }
] as const;

const tftGame = {
  href: "/tft",
  eyebrow: "Teamfight Tactics",
  title: "Meta comps, unit and item lookups, augments, traits, and player history.",
  description:
    "Check the live set, compare top comps, and review player results without digging through tabs.",
  starter:
    "Best first move: open comps when you want a stable starting board before queueing.",
  links: [
    { label: "Comps", href: "/tft/comps" },
    { label: "Units", href: "/tft/champions" },
    { label: `Open ${EXAMPLE_PROFILE.label}`, href: EXAMPLE_PROFILE.tftHref }
  ]
} as const;

export default function LandingPage() {
  const visibleGames = TFT_FRONTEND_ENABLED ? [...games, tftGame] : games;

  return (
    <div className="grid gap-10">
      <section className="page-hero p-6 sm:p-8 md:p-10">
        <div className="grid gap-6 xl:grid-cols-[minmax(0,1.3fr)_minmax(18rem,0.8fr)] xl:items-start">
          <div>
            <p className="type-kicker text-muted">Transcendence</p>
            <h1 className="type-display mt-4 max-w-4xl">
              Fast League and TFT lookups for ranked players.
            </h1>
            <p className="type-lead mt-4 max-w-2xl">
              Start with the live board when you need direction. Use search when you already know the player, champion, or page you want.
            </p>

            <div className="mt-7 grid gap-3 lg:grid-cols-[minmax(0,16rem)_minmax(0,1fr)] lg:items-center">
              <Link
                href="/lol/tierlist"
                className="type-ui inline-flex min-h-12 items-center justify-center rounded-full bg-primary px-5 py-3 font-semibold text-bg transition hover:bg-primary/92"
              >
                Start with LoL tier list
              </Link>
              <GlobalSearchLauncher className="h-12 w-full px-4 text-left lg:max-w-none" />
            </div>

            <div className="mt-6 grid gap-3 md:grid-cols-2">
              <div className="page-panel p-4">
                <p className="type-kicker text-fg/58">Start from browse</p>
                <p className="type-title mt-2 text-fg">Need the current meta first?</p>
                <p className="type-ui mt-2 text-fg/76">
                  Open tier lists or comps to get oriented before you commit to a game.
                </p>
                <div className="mt-4 flex flex-wrap gap-3">
                  <Link
                    href="/lol/tierlist"
                    className="type-ui inline-flex items-center border-b border-border/45 px-0.5 pb-1 text-fg/80 transition hover:border-border/72 hover:text-fg"
                  >
                    League tier list
                  </Link>
                  {TFT_FRONTEND_ENABLED ? (
                    <Link
                      href="/tft/comps"
                      className="type-ui inline-flex items-center border-b border-border/45 px-0.5 pb-1 text-fg/80 transition hover:border-border/72 hover:text-fg"
                    >
                      TFT comps
                    </Link>
                  ) : null}
                </div>
              </div>

              <div className="page-panel p-4">
                <p className="type-kicker text-fg/58">Start from direct lookup</p>
                <p className="type-title mt-2 text-fg">Already know the name?</p>
                <p className="type-ui mt-2 text-fg/76">
                  Use global search for player profiles, champion pages, and route shortcuts without digging through navigation.
                </p>
                <p className="type-ui mt-4 text-fg/62">
                  Working example:
                  {" "}
                  <Link href={EXAMPLE_PROFILE.lolHref} className="font-semibold text-primary transition hover:text-primary/80">
                    {EXAMPLE_PROFILE.label}
                  </Link>
                  {" "}
                  on KR.
                </p>
              </div>
            </div>
          </div>

          <aside className="page-panel grid gap-4 p-5">
            <div>
              <p className="type-kicker text-fg/58">Start Here</p>
              <h2 className="type-title mt-2 text-fg">Two fast ways to get value</h2>
            </div>
            <div className="grid gap-3">
              <div className="rounded-2xl border border-border/28 bg-surface/40 p-4">
                <p className="type-kicker text-primary/88">01</p>
                <p className="type-ui mt-2 font-semibold text-fg">Browse the live board</p>
                <p className="type-ui mt-1 text-fg/72">
                  Best for first-time visits, patch checks, and quick pre-queue decisions.
                </p>
              </div>
              <div className="rounded-2xl border border-border/28 bg-surface/40 p-4">
                <p className="type-kicker text-primary/88">02</p>
                <p className="type-ui mt-2 font-semibold text-fg">Jump straight to a name</p>
                <p className="type-ui mt-1 text-fg/72">
                  Best when you already have a Riot ID, champion, comp, or page in mind.
                </p>
              </div>
            </div>
            <div className="border-t border-border/25 pt-4">
              <p className="type-kicker text-fg/58">Suggested first click</p>
              <div className="mt-2 flex flex-wrap gap-3">
                <Link
                  href="/lol"
                  className="type-ui inline-flex items-center border-b border-border/45 px-0.5 pb-1 text-fg/78 transition hover:border-border/72 hover:text-fg"
                >
                  Open League
                </Link>
                {TFT_FRONTEND_ENABLED ? (
                  <Link
                    href="/tft"
                    className="type-ui inline-flex items-center border-b border-border/45 px-0.5 pb-1 text-fg/78 transition hover:border-border/72 hover:text-fg"
                  >
                    Open TFT
                  </Link>
                ) : null}
              </div>
            </div>
          </aside>
        </div>
      </section>

      <section className="grid gap-5 lg:grid-cols-2">
        {visibleGames.map((game) => (
          <Card key={game.href} className="grid gap-5 p-6">
            <div>
              <p className="type-kicker text-muted">
                {game.eyebrow}
              </p>
              <p className="type-title mt-3 text-fg">{game.title}</p>
              <p className="type-ui measure mt-3 text-fg/75">{game.description}</p>
              <p className="type-ui mt-4 text-fg/60">{game.starter}</p>
            </div>
            <div className="grid gap-3 border-t border-border/25 pt-4">
              <Link
                href={game.href}
                className="type-ui inline-flex w-fit items-center gap-2 font-semibold text-primary transition hover:text-primary/80"
              >
                Open {game.eyebrow === "League of Legends" ? "LoL" : "TFT"}
                <span aria-hidden="true">/</span>
              </Link>
              <div className="flex flex-wrap gap-x-4 gap-y-2">
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
            </div>
          </Card>
        ))}
      </section>
    </div>
  );
}
