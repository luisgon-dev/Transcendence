import Link from "next/link";

import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { Card } from "@/components/ui/Card";

export default function HomePage() {
  return (
    <section className="grid min-h-[72vh] place-items-center">
      <div className="relative w-full max-w-3xl">
        <div className="pointer-events-none absolute -inset-x-10 -inset-y-8 bg-[radial-gradient(circle_at_center,rgba(80,120,255,0.20),transparent_65%)] blur-2xl" />

        <Card className="relative border-border/60 bg-surface/45 p-6 shadow-[0_14px_46px_rgba(0,0,0,0.45)] sm:p-8">
          <p className="text-center text-xs font-medium uppercase tracking-[0.14em] text-fg/45">
            Global Search
          </p>
          <h1 className="mt-3 text-center font-[var(--font-sora)] text-3xl font-semibold tracking-tight sm:text-4xl">
            Find What You Need
          </h1>
          <p className="mt-2 text-center text-sm text-fg/68 sm:text-base">
            One search for champions, summoners, and tier list.
          </p>

          <GlobalSearchLauncher
            variant="hero"
            className="mt-6 h-14 w-full px-4 text-left"
          />

          <div className="mt-5 flex flex-wrap items-center justify-center gap-2">
            <Link
              className="rounded-full border border-border/65 bg-white/[0.03] px-3 py-1.5 text-sm text-fg/75 transition hover:bg-white/[0.08] hover:text-fg"
              href="/tierlist"
            >
              Tier List
            </Link>
            <Link
              className="rounded-full border border-border/65 bg-white/[0.03] px-3 py-1.5 text-sm text-fg/75 transition hover:bg-white/[0.08] hover:text-fg"
              href="/champions"
            >
              Champions
            </Link>
            <Link
              className="rounded-full border border-border/65 bg-white/[0.03] px-3 py-1.5 text-sm text-fg/75 transition hover:bg-white/[0.08] hover:text-fg"
              href="/account/favorites"
            >
              Favorites
            </Link>
          </div>

          <p className="mt-4 text-center text-xs text-muted">
            Shortcut works anywhere: Ctrl/Cmd+K
          </p>
        </Card>
      </div>
    </section>
  );
}
