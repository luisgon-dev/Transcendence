"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

import { BrandMark } from "@/components/BrandMark";
import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { cn } from "@/lib/cn";
import { TFT_FRONTEND_ENABLED, TFT_PUBLIC_ENABLED, TFT_ROUTES_ENABLED } from "@/lib/featureFlags";
import { getGameSwitcherItems } from "@/lib/siteHeader";

const COMPACT_HEADER_PATHS = new Set(["/account/login", "/account/register"]);

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

function navLinkClass(pathname: string | null, prefix: string): string {
  const isActive = pathname?.startsWith(prefix) ?? false;
  return cn(
    "type-ui relative inline-flex min-h-11 items-center whitespace-nowrap rounded-full px-2.5 py-2 transition-[color,background-color,transform,box-shadow] duration-200 ease-[cubic-bezier(0.25,1,0.5,1)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/26 focus-visible:ring-offset-2 focus-visible:ring-offset-bg sm:px-3",
    isActive
      ? "bg-surface/72 font-semibold text-fg shadow-inset"
      : "text-fg/70 hover:-translate-y-px hover:bg-surface/42 hover:text-fg",
    isActive &&
      "after:absolute after:inset-x-2 after:bottom-0 after:h-px after:rounded-full after:bg-primary/85"
  );
}

function GameSwitcher({ pathname }: { pathname: string | null }) {
  const items = getGameSwitcherItems(pathname, TFT_PUBLIC_ENABLED);

  return (
    <div className="flex items-center gap-1 rounded-full border border-border/45 bg-surface/30 p-1">
      {items.map((item) =>
        item.href ? (
          <Link
            key={item.label}
            href={item.href}
            className={cn(
              "type-ui inline-flex min-h-9 items-center rounded-full px-3 py-1.5 transition",
              item.isActive
                ? "bg-surface/78 font-semibold text-fg shadow-inset"
                : "text-fg/62 hover:bg-surface/50 hover:text-fg"
            )}
          >
            {item.label}
          </Link>
        ) : (
          <span
            key={item.label}
            aria-disabled="true"
            className="type-ui inline-flex min-h-9 cursor-not-allowed items-center gap-1.5 rounded-full px-3 py-1.5 text-fg/42"
          >
            <span>{item.label}</span>
            {item.comingSoon ? (
              <span className="type-kicker rounded-full border border-border/40 bg-surface/50 px-2 py-0.5 text-fg/40">
                Soon
              </span>
            ) : null}
          </span>
        )
      )}
    </div>
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
  const contextLinks = isTft && TFT_ROUTES_ENABLED ? TFT_LINKS : LOL_LINKS;
  const surfaceLabel = isTft && TFT_ROUTES_ENABLED ? "TFT" : "League";

  return (
    <header className="sticky top-0 z-40 border-b border-border/55 bg-bg/88 backdrop-blur-md">
      <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-3 px-4 py-3.5 md:px-6">
        <div className="flex items-center gap-3">
          <Link
            href="/"
            className="group -ml-1 inline-flex min-h-11 min-w-0 shrink-0 items-center gap-3 rounded-full px-1 py-1 touch-manipulation transition-transform duration-200 ease-[cubic-bezier(0.25,1,0.5,1)] hover:-translate-y-px focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/26 focus-visible:ring-offset-2 focus-visible:ring-offset-bg"
          >
            <BrandMark className="h-9 w-9 shrink-0 transition group-hover:scale-105" />
            <div className="grid min-w-0 gap-0.5">
              <span className="type-wordmark truncate text-fg">
                Transcendence
              </span>
              <span className="type-overline hidden items-center gap-2 text-fg/65 sm:inline-flex">
                <span className="text-primary/88">{surfaceLabel}</span>
                {patch ? <span className="type-tabular">Patch {patch}</span> : null}
                {!TFT_FRONTEND_ENABLED ? <span>TFT soon</span> : null}
              </span>
            </div>
          </Link>

          <div className="ml-auto flex min-w-0 items-center gap-1.5 sm:gap-2">
            <GlobalSearchLauncher
              variant="header"
              size="sm"
              className="w-11 px-0 lg:w-auto lg:px-3"
            />
            <div className="shrink-0">{children}</div>
          </div>
        </div>

        {!compact ? (
          <div className="flex items-center border-t border-border/30 pt-2.5">
            <nav className="flex min-w-0 flex-1 items-center gap-1 overflow-x-auto whitespace-nowrap [scrollbar-width:none] [-ms-overflow-style:none] [&::-webkit-scrollbar]:hidden">
              <div className="mr-2 shrink-0">
                <GameSwitcher pathname={pathname} />
              </div>
              {contextLinks.map((link) => (
                <Link key={link.href} href={link.href} className={navLinkClass(pathname, link.href)}>
                  {link.mobileLabel ?? link.label}
                </Link>
              ))}
            </nav>
          </div>
        ) : null}
      </div>
    </header>
  );
}
