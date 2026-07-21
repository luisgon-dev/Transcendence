"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

import { BrandMark } from "@/components/BrandMark";
import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { ThemeToggle } from "@/components/ui/ThemeToggle";
import { cn } from "@/lib/cn";

const COMPACT_HEADER_PATHS = new Set(["/account/login", "/account/register"]);

type NavLink = { href: string; label: string; mobileLabel?: string };

const NAV_LINKS: NavLink[] = [
  { href: "/lol/tierlist", label: "Tier List" },
  { href: "/lol/leaderboards", label: "Leaderboards", mobileLabel: "Ranks" },
  { href: "/lol/champions", label: "Champions" },
  { href: "/lol/items", label: "Build Atlas", mobileLabel: "Items" },
  { href: "/lol/multi-search", label: "Multi-Search", mobileLabel: "Scout" },
  { href: "/lol/live", label: "Live Game", mobileLabel: "Live" },
  { href: "/lol/pro-builds", label: "Pro Builds", mobileLabel: "Pro" }
];

function navLinkClass(pathname: string | null, prefix: string): string {
  const isActive = pathname?.startsWith(prefix) ?? false;
  return cn(
    "type-ui relative inline-flex min-h-11 items-center whitespace-nowrap rounded-md px-2 py-2 transition-colors duration-150 ease-[cubic-bezier(0.25,1,0.5,1)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/30 focus-visible:ring-offset-2 focus-visible:ring-offset-bg sm:px-2.5",
    isActive ? "font-semibold text-fg" : "text-fg/65 hover:text-fg",
    isActive &&
      "after:absolute after:inset-x-2 after:bottom-0 after:h-0.5 after:rounded-full after:bg-primary"
  );
}

export function SiteHeaderClient({
  children,
  patch
}: {
  children: React.ReactNode;
  patch?: string | null;
}) {
  const pathname = usePathname();
  const compact = pathname ? COMPACT_HEADER_PATHS.has(pathname) : false;

  return (
    <header className="sticky top-0 z-40 border-b border-border/55 bg-bg/88 backdrop-blur-md">
      <div className="mx-auto flex w-full max-w-[1440px] flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3 md:px-6">
        <Link
          href="/"
          className="group -ml-1 inline-flex min-h-11 min-w-0 shrink-0 items-center gap-3 rounded-full px-1 py-1 touch-manipulation transition-transform duration-200 ease-[cubic-bezier(0.25,1,0.5,1)] hover:-translate-y-px focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/26 focus-visible:ring-offset-2 focus-visible:ring-offset-bg"
        >
          <BrandMark className="h-9 w-9 shrink-0 transition group-hover:scale-105" />
          <div className="grid min-w-0 gap-0.5">
            <span className="type-wordmark truncate text-fg">Transcendence</span>
            <span className="type-overline hidden items-center gap-2 text-muted sm:inline-flex">
              <span>League of Legends</span>
              {patch ? <span className="type-tabular text-fg/70">Patch {patch}</span> : null}
            </span>
          </div>
        </Link>

        <div className="order-2 ml-auto flex min-w-0 items-center gap-1.5 sm:gap-2 md:order-3">
          <GlobalSearchLauncher
            variant="header"
            size="sm"
            className="w-11 px-0 lg:w-auto lg:px-3"
          />
          <ThemeToggle />
          <div className="shrink-0">{children}</div>
        </div>

        {!compact ? (
          <nav className="order-3 flex w-full min-w-0 items-center gap-1 overflow-x-auto whitespace-nowrap border-t border-border/30 pt-2 [scrollbar-width:none] [-ms-overflow-style:none] [&::-webkit-scrollbar]:hidden md:order-2 md:w-auto md:flex-1 md:border-t-0 md:pt-0 md:pl-3">
            {NAV_LINKS.map((link) => (
              <Link key={link.href} href={link.href} className={navLinkClass(pathname, link.href)}>
                <span className="sm:hidden">{link.mobileLabel ?? link.label}</span>
                <span className="hidden sm:inline">{link.label}</span>
              </Link>
            ))}
          </nav>
        ) : null}
      </div>
    </header>
  );
}
