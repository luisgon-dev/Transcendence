import Link from "next/link";

import { Card } from "@/components/ui/Card";

const games = [
  {
    href: "/lol",
    eyebrow: "League of Legends",
    title: "Champion analytics, summoner profiles, matchups, and pro builds.",
    description:
      "Use the LoL surface when you want patch reads, role-specific champion pages, or stored summoner history.",
    links: [
      { label: "Tier List", href: "/lol/tierlist" },
      { label: "Champions", href: "/lol/champions" },
      { label: "Summoners", href: "/lol/summoners/na/Faker-KR1" }
    ]
  },
  {
    href: "/tft",
    eyebrow: "Teamfight Tactics",
    title: "Comps, units, items, augments, traits, and stored summoner match history.",
    description:
      "Use the TFT surface for active-set catalogs, comp summaries, and persisted TFT profile reads.",
    links: [
      { label: "Comps", href: "/tft/comps" },
      { label: "Champions", href: "/tft/champions" },
      { label: "Summoners", href: "/tft/summoners/na/Faker-KR1" }
    ]
  }
] as const;

export default function LandingPage() {
  return (
    <div className="grid gap-6">
      <section className="glass-card mesh-highlight rounded-[2rem] p-6 sm:p-8">
        <p className="text-sm font-medium uppercase tracking-[0.24em] text-primary">Transcendence</p>
        <h1 className="mt-3 max-w-3xl font-[var(--font-sora)] text-4xl font-semibold tracking-tight sm:text-5xl">
          Separate Riot game surfaces, one shared stack.
        </h1>
        <p className="mt-3 max-w-2xl text-sm text-fg/75 sm:text-base">
          League of Legends and Teamfight Tactics now live behind separate routes, APIs, and data
          pipelines, while sharing the same backend, worker, and web app.
        </p>
      </section>

      <section className="grid gap-4 lg:grid-cols-2">
        {games.map((game) => (
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
                Open
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
