import Link from "next/link";

const GITHUB_REPO_URL = "https://github.com/luisgon-dev/Transcendence";

export function SiteFooter() {
  return (
    <footer className="mt-12 border-t border-border/40">
      <div className="mx-auto w-full max-w-[1440px] px-4 py-8 md:px-6">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <p className="text-xs text-muted">
            &copy; 2026 luisgon-dev &middot; Licensed under{" "}
            <Link
              href={`${GITHUB_REPO_URL}/blob/main/LICENSE`}
              target="_blank"
              rel="noreferrer"
              className="underline underline-offset-2 transition hover:text-fg"
            >
              GPL-3.0
            </Link>
          </p>
          <nav className="flex items-center gap-4 text-xs text-muted">
            <Link
              href="/terms"
              className="underline underline-offset-2 transition hover:text-fg"
            >
              Terms &amp; Conditions
            </Link>
            <Link
              href={GITHUB_REPO_URL}
              target="_blank"
              rel="noreferrer"
              className="underline underline-offset-2 transition hover:text-fg"
            >
              GitHub
            </Link>
          </nav>
        </div>
        <p className="mt-4 max-w-prose text-[11px] leading-relaxed text-muted/70">
          Transcendence isn&apos;t endorsed by Riot Games and doesn&apos;t
          reflect the views or opinions of Riot Games or anyone officially
          involved in producing or managing Riot Games properties. Riot Games,
          and all associated properties are trademarks or registered trademarks
          of Riot Games, Inc.
        </p>
      </div>
    </footer>
  );
}
