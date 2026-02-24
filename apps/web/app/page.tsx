import Link from "next/link";

import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { fetchChampionMap } from "@/lib/staticData";

const ROLE_LINKS = [
  { label: "S Tier", href: "/tierlist" },
  { label: "Top", href: "/tierlist?role=TOP" },
  { label: "Jungle", href: "/tierlist?role=JUNGLE" },
  { label: "Mid", href: "/tierlist?role=MIDDLE" },
  { label: "Bot", href: "/tierlist?role=BOTTOM" },
  { label: "Support", href: "/tierlist?role=UTILITY" }
] as const;

export default async function HomePage() {
  const { version } = await fetchChampionMap();
  const patch = version.split(".").slice(0, 2).join(".");

  return (
    <section className="grid min-h-[72vh] place-items-center">
      <div className="relative w-full max-w-3xl">
        <div className="pointer-events-none absolute -inset-x-10 -inset-y-8 bg-[radial-gradient(circle_at_center,rgba(80,120,255,0.20),transparent_65%)] blur-2xl" />

        <Card className="relative border-border/60 bg-surface/45 p-6 shadow-[0_14px_46px_rgba(0,0,0,0.45)] sm:p-8">
          <div className="flex items-center justify-center gap-2">
            <Badge className="border-primary/40 bg-primary/10 text-primary">
              Patch {patch}
            </Badge>
          </div>

          <h1 className="mt-3 text-center font-[var(--font-sora)] text-3xl font-semibold tracking-tight sm:text-4xl">
            Transcendence
          </h1>
          <p className="mt-2 text-center text-sm text-fg/68 sm:text-base">
            League of Legends Win Rates, Builds &amp; Tier Lists
          </p>

          <GlobalSearchLauncher
            variant="hero"
            className="mt-6 h-14 w-full px-4 text-left"
          />

          <div className="mt-5 flex flex-wrap items-center justify-center gap-2">
            {ROLE_LINKS.map((link) => (
              <Link
                key={link.href}
                className="rounded-full border border-border/65 bg-white/[0.03] px-3 py-1.5 text-sm text-fg/75 transition hover:bg-white/[0.08] hover:text-fg"
                href={link.href}
              >
                {link.label}
              </Link>
            ))}
            <Link
              className="rounded-full border border-border/65 bg-white/[0.03] px-3 py-1.5 text-sm text-fg/75 transition hover:bg-white/[0.08] hover:text-fg"
              href="/champions"
            >
              Champions
            </Link>
          </div>

          <p className="mt-4 text-center text-xs text-muted">
            Press Ctrl/Cmd+K anywhere to search
          </p>
        </Card>
      </div>
    </section>
  );
}
