import Link from "next/link";

import { Card } from "@/components/ui/Card";
import { TFT_FRONTEND_ENABLED } from "@/lib/featureFlags";

const games = [
  {
    href: "/lol",
    eyebrow: "League of Legends",
    title: "Tier lists, builds, matchup tools, pro builds, and player profiles.",
    description:
      "Jump into patch winners, champion pages by role, and saved player match history.",
    links: [
      { label: "Tier List", href: "/lol/tierlist" },
      { label: "Champions", href: "/lol/champions" },
      { label: "Player Search", href: "/lol/summoners/na/Faker-KR1" }
    ]
  }
] as const;

const tftGame = {
  href: "/tft",
  eyebrow: "Teamfight Tactics",
  title: "Meta comps, unit and item lookups, augments, traits, and player history.",
  description:
    "Track the live set, compare top comps, and check how players are performing match by match.",
  links: [
    { label: "Comps", href: "/tft/comps" },
    { label: "Units", href: "/tft/champions" },
    { label: "Player Search", href: "/tft/summoners/na/Faker-KR1" }
  ]
} as const;

export default function LandingPage() {
  const visibleGames = TFT_FRONTEND_ENABLED ? [...games, tftGame] : games;

  return (
    <div className="grid gap-10">
      <section className="glass-card mesh-highlight rounded-[2rem] p-6 sm:p-8">
        <p className="text-sm font-medium uppercase tracking-[0.24em] text-primary">Transcendence</p>
        <h1 className="mt-4 max-w-4xl font-[var(--font-sora)] text-4xl font-semibold tracking-tight sm:text-5xl">
          Patch-ready League and TFT tools built for players first.
        </h1>
        <p className="mt-3 max-w-2xl text-sm text-fg/75 sm:text-base">
          Check what is winning, compare builds and comps, and pull up player pages without digging through menus.
        </p>
        <div className="mt-6 flex flex-wrap gap-2">
          <Link
            href="/lol"
            className="rounded-full border border-primary/45 bg-primary/12 px-4 py-2 text-sm font-medium text-primary"
          >
            Explore LoL
          </Link>
          {TFT_FRONTEND_ENABLED ? (
            <Link
              href="/tft"
              className="rounded-full border border-border/60 px-4 py-2 text-sm text-fg/80 transition hover:bg-white/8 hover:text-fg"
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
              <p className="text-xs font-medium uppercase tracking-[0.24em] text-primary">
                {game.eyebrow}
              </p>
              <p className="mt-2 text-lg font-semibold text-fg">{game.title}</p>
              <p className="mt-2 text-sm text-fg/75">{game.description}</p>
            </div>
            <div className="flex flex-wrap gap-2">
              <Link
                href={game.href}
                className="rounded-full border border-primary/45 bg-primary/12 px-4 py-2 text-sm font-medium text-primary"
              >
                Open {game.eyebrow === "League of Legends" ? "LoL" : "TFT"}
              </Link>
              {game.links.map((link) => (
                <Link
                  key={link.href}
                  href={link.href}
                  className="rounded-full border border-border/60 px-4 py-2 text-sm text-fg/80 transition hover:bg-white/8 hover:text-fg"
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
