import Link from "next/link";

import { AccountNav } from "@/components/AccountNav";
import { SearchBar } from "@/components/SearchBar";

export function SiteHeader() {
  return (
    <header className="sticky top-0 z-40 border-b border-border/60 bg-bg/70 backdrop-blur">
      <div className="mx-auto flex max-w-6xl flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center justify-between gap-3">
          <Link href="/" className="group inline-flex items-center gap-2">
            <span className="h-8 w-8 rounded-lg bg-gradient-to-br from-primary to-primary-2 shadow-glass" />
            <span className="text-sm font-semibold tracking-wide text-fg">
              Transcendence
            </span>
          </Link>

          <nav className="hidden items-center gap-4 sm:flex">
            <Link
              href="/tierlist"
              className="text-sm text-fg/80 hover:text-fg"
            >
              Tier List
            </Link>
            <Link
              href="/champions"
              className="text-sm text-fg/80 hover:text-fg"
            >
              Champions
            </Link>
          </nav>
        </div>

        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-end">
          <SearchBar className="sm:max-w-[520px]" />
          <AccountNav />
        </div>
      </div>
    </header>
  );
}

