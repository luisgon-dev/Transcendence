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
            Patch-aware stats
          </Badge>
          <Badge className="border-primary-2/40 bg-primary-2/10 text-primary-2">
            Match history
          </Badge>
          <Badge>Builds and counters</Badge>
        </div>

        <h1 className="text-balance font-[var(--font-sora)] text-4xl font-semibold tracking-tight sm:text-5xl">
          Find players. Analyze matches.{" "}
          <span className="bg-gradient-to-r from-primary to-primary-2 bg-clip-text text-transparent">
            Move faster.
          </span>
        </h1>

        <p className="max-w-2xl text-pretty text-base text-fg/80">
          Summoner profiles, match history, tier lists, builds, matchups, and
          live game insights, organized for quick decisions.
        </p>

        <Card className="p-4">
          <SearchBar />
          <p className="mt-3 text-sm text-muted">
            Tip: if we don&apos;t have data for a player yet, we&apos;ll start an
            update and this page will fill in automatically as it completes.
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

