import Link from "next/link";

import { Card } from "@/components/ui/Card";

const suggestions = [
  { href: "/lol/tierlist", label: "LoL Tier List" },
  { href: "/lol/champions", label: "Champions" },
  { href: "/lol/pro-builds", label: "Pro Builds" }
];

export default function NotFound() {
  return (
    <div className="empty-state-shell">
      <section className="page-hero w-full max-w-4xl p-6 sm:p-8">
        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(18rem,0.72fr)]">
          <div>
            <p className="type-kicker text-muted">Route Missing</p>
            <h1 className="type-panel-title mt-3">
              This page isn&apos;t available here.
            </h1>
            <p className="type-lead mt-4">
              The route may have moved, the URL may be wrong, or the page may not be live on this deployment yet.
            </p>

            <div className="mt-6 flex flex-wrap gap-3">
              <Link
                href="/"
                className="type-ui inline-flex min-h-12 items-center justify-center rounded-control bg-primary px-5 py-3 font-semibold text-primary-fg shadow-soft transition hover:-translate-y-px hover:bg-primary/94 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/28 focus-visible:ring-offset-2 focus-visible:ring-offset-bg"
              >
                Go home
              </Link>
              <Link
                href="/lol/tierlist"
                className="type-ui inline-flex min-h-12 items-center justify-center rounded-full border border-border/60 bg-surface/68 px-5 py-3 font-semibold text-fg/86 shadow-inset transition hover:-translate-y-px hover:border-border-strong hover:bg-surface/84 hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/24 focus-visible:ring-offset-2 focus-visible:ring-offset-bg"
              >
                Open tier list
              </Link>
            </div>
          </div>

          <Card className="grid gap-4 rounded-panel p-5">
            <div>
              <p className="type-kicker text-fg/56">Try Instead</p>
              <p className="type-ui mt-2 text-fg/74">
                These routes are stable entry points when you need to get moving again quickly.
              </p>
            </div>

            <nav>
              <ul className="grid gap-2.5">
                {suggestions.map((suggestion) => (
                  <li key={suggestion.href}>
                    <Link
                      href={suggestion.href}
                      className="type-ui flex items-center justify-between rounded-card border border-border/42 bg-surface/38 px-4 py-3 text-fg/86 transition hover:border-primary/28 hover:bg-surface/58 hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/24 focus-visible:ring-offset-2 focus-visible:ring-offset-bg"
                    >
                      <span>{suggestion.label}</span>
                      <span aria-hidden="true" className="text-fg/38">/</span>
                    </Link>
                  </li>
                ))}
              </ul>
            </nav>
          </Card>
        </div>
      </section>
    </div>
  );
}

