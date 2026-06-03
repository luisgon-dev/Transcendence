import Link from "next/link";

const GITHUB_REPO_URL = "https://github.com/luisgon-dev/Transcendence";

export function SiteFooter() {
  return (
    <footer className="mt-16 border-t border-border/60 bg-surface/30">
      <div className="site-footer-shell mx-auto w-full max-w-[1440px] px-4 py-10 md:px-6">
        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-end">
          <div className="grid gap-3">
            <div>
              <p className="type-kicker text-fg/52">Transcendence</p>
              <p className="type-ui mt-2 max-w-2xl text-fg/74">
                Fast League lookups, current-patch analytics, and player routes that stay readable under pressure.
              </p>
            </div>
            <p className="type-note measure text-fg/64">
              Transcendence isn&apos;t endorsed by Riot Games. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.
            </p>
          </div>

          <div className="grid gap-3 lg:justify-items-end">
            <nav className="type-ui flex flex-wrap items-center gap-x-5 gap-y-2 text-fg/62">
              <Link href="/terms" className="site-footer-link">
                Terms
              </Link>
              <Link
                href={GITHUB_REPO_URL}
                target="_blank"
                rel="noreferrer"
                className="site-footer-link"
              >
                GitHub
              </Link>
              <Link
                href={`${GITHUB_REPO_URL}/blob/main/LICENSE`}
                target="_blank"
                rel="noreferrer"
                className="site-footer-link"
              >
                GPL-3.0
              </Link>
            </nav>
            <p className="type-note text-fg/56">&copy; 2026 luisgon-dev</p>
          </div>
        </div>
      </div>
    </footer>
  );
}
