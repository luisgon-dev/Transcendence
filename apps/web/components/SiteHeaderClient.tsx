"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

import { BrandMark } from "@/components/BrandMark";
import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { cn } from "@/lib/cn";
import { TFT_FRONTEND_ENABLED } from "@/lib/featureFlags";

const COMPACT_HEADER_PATHS = new Set(["/account/login", "/account/register"]);

type NavLink = { href: string; label: string; mobileLabel?: string };

const LOL_LINKS: NavLink[] = [
  { href: "/lol/tierlist", label: "Tier List" },
  { href: "/lol/champions", label: "Champions" },
  { href: "/lol/matchups", label: "Matchups" },
  { href: "/lol/pro-builds", label: "Pro Builds", mobileLabel: "Pro Builds" }
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
    "type-ui relative whitespace-nowrap rounded-full px-2 py-2 transition",
    isActive ? "font-semibold text-fg" : "text-fg/70 hover:text-fg",
    isActive &&
      "after:absolute after:inset-x-2 after:bottom-0 after:h-px after:rounded-full after:bg-primary/85"
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
  const surfaceLabel = isTft && TFT_FRONTEND_ENABLED ? "TFT" : "League";

  return (
    <header className="sticky top-0 z-40 border-b border-border/60 bg-bg/90 backdrop-blur-sm">
      <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-3 px-4 py-3 md:px-6">
        <div className="flex items-center gap-3">
          <Link href="/" className="group inline-flex min-w-0 shrink-0 items-center gap-3">
            <BrandMark className="h-9 w-9 shrink-0 transition group-hover:scale-105" />
            <div className="grid min-w-0 gap-0.5">
              <span className="font-heading truncate text-[0.98rem] font-semibold tracking-[0.035em] text-fg">
                Transcendence
              </span>
              <span className="hidden items-center gap-2 text-[0.72rem] font-medium uppercase tracking-[0.16em] text-fg/65 sm:inline-flex">
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
              className="w-10 px-0 lg:w-auto lg:px-3"
            />
            <div className="shrink-0">{children}</div>
          </div>
        </div>

        {!compact ? (
          <div className="flex items-center border-t border-border/35 pt-2.5">
            <nav className="flex min-w-0 flex-1 items-center gap-1 overflow-x-auto whitespace-nowrap [scrollbar-width:none] [-ms-overflow-style:none] [&::-webkit-scrollbar]:hidden">
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
