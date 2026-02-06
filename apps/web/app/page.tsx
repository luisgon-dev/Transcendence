import Link from "next/link";

import { Card } from "@/components/ui/Card";
import { SearchBar } from "@/components/SearchBar";
import { Badge } from "@/components/ui/Badge";

export default function HomePage() {
  return (
    <div className="grid gap-10">
      <section className="grid gap-6">
        <div className="flex flex-wrap items-center gap-3">
          <Badge className="border-primary/40 bg-primary/10 text-primary">
            SSR-first
          </Badge>
          <Badge className="border-primary-2/40 bg-primary-2/10 text-primary-2">
            OpenAPI client
          </Badge>
          <Badge>Fast refresh polling</Badge>
        </div>

        <h1 className="text-balance font-[var(--font-sora)] text-4xl font-semibold tracking-tight sm:text-5xl">
          Find players. Analyze matches.{" "}
          <span className="bg-gradient-to-r from-primary to-primary-2 bg-clip-text text-transparent">
            Move faster.
          </span>
        </h1>

        <p className="max-w-2xl text-pretty text-base text-fg/80">
          Transcendence is a snappy League analytics front end powered by your
          API: summoner profiles, match history, tier lists, builds, and live
          game insights.
        </p>

        <Card className="p-4">
          <SearchBar />
          <p className="mt-3 text-sm text-muted">
            Tip: if a summoner isn&apos;t in the database yet, we&apos;ll queue a
            background refresh and auto-poll until it lands.
          </p>
        </Card>
      </section>

      <section className="grid gap-4 md:grid-cols-3">
        <Card className="p-5">
          <h2 className="font-[var(--font-sora)] text-lg font-semibold">
            Tier List
          </h2>
          <p className="mt-2 text-sm text-fg/75">
            Patch-aware rankings with win/pick rates and movement.
          </p>
          <Link
            className="mt-4 inline-flex text-sm text-primary hover:underline"
            href="/tierlist"
          >
            Browse tier list
          </Link>
        </Card>
        <Card className="p-5">
          <h2 className="font-[var(--font-sora)] text-lg font-semibold">
            Champions
          </h2>
          <p className="mt-2 text-sm text-fg/75">
            Explore builds, matchups, and win rates per role.
          </p>
          <Link
            className="mt-4 inline-flex text-sm text-primary hover:underline"
            href="/champions"
          >
            Explore champions
          </Link>
        </Card>
        <Card className="p-5">
          <h2 className="font-[var(--font-sora)] text-lg font-semibold">
            Favorites
          </h2>
          <p className="mt-2 text-sm text-fg/75">
            Save your most-viewed players and preferences.
          </p>
          <Link
            className="mt-4 inline-flex text-sm text-primary hover:underline"
            href="/account/favorites"
          >
            View favorites
          </Link>
        </Card>
      </section>
    </div>
  );
}

