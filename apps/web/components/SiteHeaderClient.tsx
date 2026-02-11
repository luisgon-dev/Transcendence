"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

import { BrandMark } from "@/components/BrandMark";
import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { cn } from "@/lib/cn";

const COMPACT_HEADER_PATHS = new Set(["/account/login", "/account/register"]);

export function SiteHeaderClient({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const compact = pathname ? COMPACT_HEADER_PATHS.has(pathname) : false;

  return (
    <header className="sticky top-0 z-40 border-b border-border/55 bg-bg/80 backdrop-blur-md">
      <div className="mx-auto grid max-w-6xl gap-3 px-4 py-3">
        <div className="flex items-center gap-3">
          <Link href="/" className="group inline-flex shrink-0 items-center gap-2">
            <BrandMark className="h-8 w-8" />
            <span className="text-sm font-semibold tracking-wide text-fg">
              Transcendence
            </span>
          </Link>

          <nav className="ml-3 hidden items-center gap-5 md:flex">
            <Link href="/tierlist" className="text-sm text-fg/70 transition hover:text-fg">
              Tier List
            </Link>
            <Link href="/champions" className="text-sm text-fg/70 transition hover:text-fg">
              Champions
            </Link>
          </nav>

          <div className="ml-auto flex min-w-0 items-center gap-2">
            <GlobalSearchLauncher
              variant="header"
              size="sm"
              className={cn(
                "h-9 min-w-[148px]",
                compact ? "max-w-[230px]" : "max-w-[220px]"
              )}
            />
            <div className="shrink-0">{children}</div>
          </div>
        </div>

        <nav className="flex items-center gap-4 border-t border-border/40 pt-2 md:hidden">
          <Link href="/tierlist" className="text-sm text-fg/70 transition hover:text-fg">
            Tier List
          </Link>
          <Link href="/champions" className="text-sm text-fg/70 transition hover:text-fg">
            Champions
          </Link>
        </nav>
      </div>
    </header>
  );
}
