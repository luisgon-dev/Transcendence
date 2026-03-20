"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

import { BrandMark } from "@/components/BrandMark";
import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { cn } from "@/lib/cn";
import { TFT_FRONTEND_ENABLED } from "@/lib/featureFlags";
import { getGameSwitcherItems } from "@/lib/siteHeader";

const COMPACT_HEADER_PATHS = new Set(["/account/login", "/account/register"]);
const GITHUB_REPO_URL = "https://github.com/luisgon-dev/Transcendence";

type NavLink = { href: string; label: string; mobileLabel?: string };

const LOL_LINKS: NavLink[] = [
  { href: "/lol/tierlist", label: "Tier List" },
  { href: "/lol/champions", label: "Champions" },
  { href: "/lol/matchups", label: "Matchups" },
  { href: "/lol/pro-builds", label: "Pro Builds", mobileLabel: "Pro" }
];

const TFT_LINKS: NavLink[] = [
  { href: "/tft/comps", label: "Comps" },
  { href: "/tft/champions", label: "Units" },
  { href: "/tft/items", label: "Items" },
  { href: "/tft/traits", label: "Traits" },
  { href: "/tft/augments", label: "Augments" }
];

function GameSwitcher({ pathname }: { pathname: string | null }) {
  const items = getGameSwitcherItems(pathname, TFT_FRONTEND_ENABLED);

  return (
    <div className="flex items-center rounded-lg border border-border/50 bg-surface/30 p-0.5">
      {items.map((item) =>
        item.href ? (
          <Link
            key={item.label}
            href={item.href}
            className={cn(
              "rounded-md px-2.5 py-1 text-xs font-semibold transition",
              item.isActive
                ? "bg-primary/15 text-primary shadow-sm"
                : "text-fg/55 hover:text-fg"
            )}
          >
            {item.label}
          </Link>
        ) : (
          <span
            key={item.label}
            aria-disabled="true"
            className="inline-flex cursor-not-allowed items-center gap-1.5 rounded-md border border-border/50 bg-surface/60 px-2.5 py-1 text-xs font-semibold text-fg/40"
          >
            <span>{item.label}</span>
            {item.comingSoon ? (
              <span className="rounded-full border border-border/45 bg-white/5 px-1.5 py-0.5 text-[9px] font-semibold uppercase tracking-[0.14em] text-fg/35">
                Coming soon
              </span>
            ) : null}
          </span>
        )
      )}
    </div>
  );
}

function navLinkClass(pathname: string | null, prefix: string): string {
  const isActive = pathname?.startsWith(prefix) ?? false;
  return cn(
    "text-sm transition",
    isActive ? "text-fg font-semibold" : "text-fg/70 hover:text-fg"
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
  const isTft = pathname?.startsWith("/tft") ?? false;
  const contextLinks = isTft && TFT_FRONTEND_ENABLED ? TFT_LINKS : LOL_LINKS;

  return (
    <header className="sticky top-0 z-40 border-b border-border/60 bg-bg/75 backdrop-blur-xl">
      <div className="mx-auto grid w-full max-w-[1440px] gap-3 px-4 py-3 md:px-6">
        <div className="flex items-center gap-3">
          <Link href="/" className="group inline-flex shrink-0 items-center gap-2.5">
            <BrandMark className="h-9 w-9 transition group-hover:scale-105" />
            <span className="font-[var(--font-sora)] text-sm font-semibold tracking-wide text-fg">
              Transcendence
            </span>
          </Link>

          {/* Desktop nav */}
          <nav className="ml-3 hidden items-center gap-4 md:flex">
            <GameSwitcher pathname={pathname} />

            <div className="h-4 w-px bg-border/50" />

            {contextLinks.map((link) => (
              <Link key={link.href} href={link.href} className={navLinkClass(pathname, link.href)}>
                {link.label}
              </Link>
            ))}

            {patch && (
              <span className="rounded-full border border-primary/35 bg-primary/10 px-2.5 py-0.5 text-[11px] font-medium text-primary">
                Patch {patch}
              </span>
            )}
            <Link
              href={GITHUB_REPO_URL}
              target="_blank"
              rel="noreferrer"
              aria-label="Open Transcendence GitHub repository"
              className="inline-flex h-8 w-8 items-center justify-center rounded-full border border-border/70 bg-surface/50 text-fg/75 transition hover:scale-105 hover:bg-white/10 hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/50"
            >
              <GitHubIcon className="h-4 w-4" />
            </Link>
          </nav>

          <div className="ml-auto flex min-w-0 items-center gap-2">
            <GlobalSearchLauncher
              variant="header"
              size="sm"
              className={cn(
                "h-9 min-w-0 sm:min-w-[148px]",
                compact ? "max-w-[230px]" : "max-w-[220px]"
              )}
            />
            <div className="shrink-0">{children}</div>
          </div>
        </div>

        {/* Mobile nav */}
        <nav className="flex flex-wrap items-center gap-1.5 border-t border-border/40 pt-2 md:hidden">
          <GameSwitcher pathname={pathname} />

          {contextLinks.map((link) => {
            const isActive = pathname?.startsWith(link.href) ?? false;
            return (
              <Link
                key={link.href}
                href={link.href}
                className={cn(
                  "inline-flex min-h-[36px] items-center rounded-lg px-2.5 text-sm transition",
                  isActive ? "bg-white/[0.06] font-semibold text-fg" : "text-fg/70 active:bg-white/[0.04]"
                )}
              >
                {link.mobileLabel ?? link.label}
              </Link>
            );
          })}

          {patch && (
            <span className="rounded-full border border-primary/40 bg-primary/10 px-2 py-0.5 text-[11px] font-medium text-primary">
              {patch}
            </span>
          )}
          <Link
            href={GITHUB_REPO_URL}
            target="_blank"
            rel="noreferrer"
            aria-label="Open Transcendence GitHub repository"
            className="ml-auto inline-flex h-9 w-9 items-center justify-center rounded-full border border-border/70 bg-surface/40 text-fg/75 transition active:bg-white/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/50"
          >
            <GitHubIcon className="h-4 w-4" />
          </Link>
        </nav>
      </div>
    </header>
  );
}

function GitHubIcon({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 16 16"
      aria-hidden="true"
      className={className}
      fill="currentColor"
    >
      <path d="M8 0C3.58 0 0 3.67 0 8.2c0 3.62 2.29 6.7 5.47 7.79.4.08.55-.18.55-.39 0-.19-.01-.83-.01-1.5-2.01.38-2.53-.5-2.69-.96-.09-.24-.48-.96-.82-1.16-.28-.16-.68-.57-.01-.58.63-.01 1.08.59 1.23.83.72 1.25 1.87.9 2.33.69.07-.53.28-.9.51-1.11-1.78-.21-3.64-.92-3.64-4.07 0-.9.31-1.64.82-2.22-.08-.21-.36-1.04.08-2.16 0 0 .67-.22 2.2.85.64-.18 1.32-.27 2-.27s1.36.09 2 .27c1.53-1.07 2.2-.85 2.2-.85.44 1.12.16 1.95.08 2.16.51.58.82 1.31.82 2.22 0 3.16-1.87 3.86-3.65 4.07.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.47.55.39A8.225 8.225 0 0 0 16 8.2C16 3.67 12.42 0 8 0Z" />
    </svg>
  );
}
