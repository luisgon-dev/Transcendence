"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

import { BrandMark } from "@/components/BrandMark";
import { GlobalSearchLauncher } from "@/components/GlobalSearchLauncher";
import { cn } from "@/lib/cn";

const COMPACT_HEADER_PATHS = new Set(["/account/login", "/account/register"]);
const GITHUB_REPO_URL = "https://github.com/luisgon-dev/Transcendence";

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
            <Link
              href={GITHUB_REPO_URL}
              target="_blank"
              rel="noreferrer"
              aria-label="Open Transcendence GitHub repository"
              className="inline-flex h-8 w-8 items-center justify-center rounded-full border border-border/70 bg-surface/40 text-fg/75 transition hover:bg-white/10 hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/50"
            >
              <GitHubIcon className="h-4 w-4" />
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
          <Link
            href={GITHUB_REPO_URL}
            target="_blank"
            rel="noreferrer"
            aria-label="Open Transcendence GitHub repository"
            className="ml-auto inline-flex h-8 w-8 items-center justify-center rounded-full border border-border/70 bg-surface/40 text-fg/75 transition hover:bg-white/10 hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/50"
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
