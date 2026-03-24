import Link from "next/link";

import { Card } from "@/components/ui/Card";
import { TFT_FRONTEND_ENABLED } from "@/lib/featureFlags";

const games = [
  {
    href: "/lol",
    eyebrow: "League of Legends",
    title: "Tier lists, builds, matchup tools, pro builds, and player profiles.",
    description:
      "Open the current board, jump to a champion page, or pull up a player profile in a few clicks.",
    links: [
      { label: "Tier List", href: "/lol/tierlist" },
      { label: "Champions", href: "/lol/champions" },
      { label: "Open Faker#KR1", href: "/lol/summoners/na/Faker-KR1" }
    ]
  }
] as const;

const tftGame = {
  href: "/tft",
  eyebrow: "Teamfight Tactics",
  title: "Meta comps, unit and item lookups, augments, traits, and player history.",
  description:
    "Check the live set, compare top comps, and review player results without digging through tabs.",
  links: [
    { label: "Comps", href: "/tft/comps" },
    { label: "Units", href: "/tft/champions" },
    { label: "Open Faker#KR1", href: "/tft/summoners/na/Faker-KR1" }
  ]
} as const;

export default function LandingPage() {
  const visibleGames = TFT_FRONTEND_ENABLED ? [...games, tftGame] : games;

  return (
    <div className="grid gap-10">
      <section className="page-hero p-6 sm:p-8 md:p-10">
        <p className="type-kicker text-muted">Transcendence</p>
        <h1 className="type-display mt-4 max-w-4xl">
          Fast League and TFT lookups for ranked players.
        </h1>
        <p className="type-lead mt-4 max-w-2xl">
          Open the tier list, jump to a champion page, or pull up a player profile without losing time between games.
        </p>
        <div className="mt-7 flex flex-wrap items-center gap-3">
          <Link
            href="/lol/tierlist"
            className="type-ui inline-flex min-h-12 items-center justify-center rounded-full bg-primary px-5 py-3 font-semibold text-bg transition hover:bg-primary/92"
          >
            Open League tier list
          </Link>
          {TFT_FRONTEND_ENABLED ? (
            <Link
              href="/tft"
              className="type-ui inline-flex items-center border-b border-border/45 px-1 py-2 text-fg/72 transition hover:border-border/70 hover:text-fg"
            >
              Explore TFT
            </Link>
          ) : null}
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
