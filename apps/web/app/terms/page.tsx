import type { Metadata } from "next";
import Link from "next/link";

export const metadata: Metadata = {
  title: "Terms & Conditions — Transcendence"
};

const GITHUB_REPO_URL = "https://github.com/luisgon-dev/Transcendence";

const linkClass =
  "font-medium text-fg underline decoration-border-strong underline-offset-2 transition-colors hover:decoration-primary";

function SectionHeading({ index, title, id }: { index: string; title: string; id: string }) {
  return (
    <div className="flex items-baseline gap-3">
      <span className="type-tabular text-sm font-semibold tabular-nums text-muted">{index}</span>
      <h2 id={id} className="type-title scroll-mt-24 text-fg">
        {title}
      </h2>
    </div>
  );
}

export default function TermsPage() {
  return (
    <article className="mx-auto max-w-2xl">
      <header className="border-b border-border pb-8">
        <p className="type-overline text-muted">Legal</p>
        <h1 className="type-display mt-3">Terms &amp; Conditions</h1>
        <p className="type-note mt-4 text-muted">
          Last updated March 2026 · Transcendence is free and open-source under GPL-3.0.
        </p>
      </header>

      <div className="mt-12 grid gap-12 [&_p]:max-w-none [&_li]:max-w-none">
        {/* 01 — Riot Games Disclaimer */}
        <section className="grid gap-4">
          <SectionHeading index="01" id="riot" title="Riot Games Disclaimer" />
          <div className="surface-subtle rounded-card p-4">
            <p className="type-ui leading-relaxed text-fg/90">
              Transcendence isn&apos;t endorsed by Riot Games and doesn&apos;t reflect the views or opinions
              of Riot Games or anyone officially involved in producing or managing Riot Games properties.
              Riot Games, and all associated properties are trademarks or registered trademarks of Riot
              Games, Inc.
            </p>
          </div>
          <p className="type-note leading-relaxed text-muted">
            All game data — champion information, match history, summoner profiles, and related assets — is
            provided by the{" "}
            <Link href="https://developer.riotgames.com" target="_blank" rel="noreferrer" className={linkClass}>
              Riot Games API
            </Link>{" "}
            and remains the intellectual property of Riot Games, Inc. Use of this data is subject to the{" "}
            <Link href="https://developer.riotgames.com/terms" target="_blank" rel="noreferrer" className={linkClass}>
              Riot Games API Terms of Service
            </Link>
            .
          </p>
        </section>

        {/* 02 — Terms of Service */}
        <section className="grid gap-4 border-t border-border/60 pt-12">
          <SectionHeading index="02" id="terms" title="Terms of Service" />
          <div className="grid gap-4 type-note leading-relaxed text-muted">
            <p>
              Transcendence is a free, open-source League of Legends analytics platform. By using this
              service you agree to the following terms.
            </p>

            <h3 className="type-meta text-fg/80">Acceptable Use</h3>
            <ul className="grid list-disc gap-1.5 pl-5 marker:text-border-strong">
              <li>The service is provided for informational and analytical purposes only.</li>
              <li>
                You may not use Transcendence to gain an unfair competitive advantage, automate gameplay, or
                violate the{" "}
                <Link href="https://www.riotgames.com/en/terms-of-service" target="_blank" rel="noreferrer" className={linkClass}>
                  Riot Games Terms of Service
                </Link>
                .
              </li>
              <li>Automated scraping, crawling, or bulk data extraction from Transcendence is prohibited.</li>
            </ul>

            <h3 className="type-meta text-fg/80">Accounts</h3>
            <ul className="grid list-disc gap-1.5 pl-5 marker:text-border-strong">
              <li>You are responsible for maintaining the confidentiality of your account credentials.</li>
              <li>
                Transcendence reserves the right to suspend or terminate accounts that violate these terms
                or engage in abusive behavior.
              </li>
            </ul>

            <h3 className="type-meta text-fg/80">Service Availability</h3>
            <ul className="grid list-disc gap-1.5 pl-5 marker:text-border-strong">
              <li>Transcendence is provided on an &ldquo;as is&rdquo; basis without warranties of any kind.</li>
              <li>We reserve the right to modify, suspend, or discontinue the service at any time without notice.</li>
              <li>Data accuracy depends on the Riot Games API and may not always be up to date.</li>
            </ul>
          </div>
        </section>

        {/* 03 — Privacy Policy */}
        <section className="grid gap-4 border-t border-border/60 pt-12">
          <SectionHeading index="03" id="privacy" title="Privacy Policy" />
          <div className="grid gap-4 type-note leading-relaxed text-muted">
            <h3 className="type-meta text-fg/80">Information We Collect</h3>
            <ul className="grid list-disc gap-1.5 pl-5 marker:text-border-strong">
              <li><span className="text-fg/85">Account data:</span> email address and a securely hashed password when you register.</li>
              <li><span className="text-fg/85">Summoner data:</span> summoner names, regions, and match history retrieved from the Riot Games API when you perform searches.</li>
              <li><span className="text-fg/85">Favorites:</span> summoner profiles you choose to save to your account.</li>
              <li><span className="text-fg/85">Session cookies:</span> used for authentication and session management.</li>
            </ul>

            <h3 className="type-meta text-fg/80">Information We Do Not Collect</h3>
            <ul className="grid list-disc gap-1.5 pl-5 marker:text-border-strong">
              <li>We never store your Riot Games account credentials or passwords in plain text.</li>
              <li>We do not collect payment information, precise location data, or device fingerprints.</li>
            </ul>

            <h3 className="type-meta text-fg/80">How We Use Your Data</h3>
            <ul className="grid list-disc gap-1.5 pl-5 marker:text-border-strong">
              <li>To provide analytics and display summoner profiles and match history.</li>
              <li>To authenticate your account and manage your session.</li>
              <li>We do not sell, rent, or share your personal data with third parties.</li>
            </ul>

            <h3 className="type-meta text-fg/80">Data Retention &amp; Deletion</h3>
            <ul className="grid list-disc gap-1.5 pl-5 marker:text-border-strong">
              <li>Game data sourced from the Riot API is cached to improve performance and may be refreshed periodically.</li>
              <li>
                You may request deletion of your account and associated data by contacting us through the{" "}
                <Link href={`${GITHUB_REPO_URL}/issues`} target="_blank" rel="noreferrer" className={linkClass}>
                  GitHub repository
                </Link>
                .
              </li>
            </ul>
          </div>
        </section>

        {/* 04 — Open Source License */}
        <section className="grid gap-4 border-t border-border/60 pt-12">
          <SectionHeading index="04" id="license" title="Open Source License" />
          <p className="type-note leading-relaxed text-muted">
            Transcendence is open-source software licensed under the{" "}
            <Link href={`${GITHUB_REPO_URL}/blob/main/LICENSE`} target="_blank" rel="noreferrer" className={linkClass}>
              GNU General Public License v3.0
            </Link>
            . You are free to use, modify, and distribute the source code under the terms of that license.
            The full source code is available on{" "}
            <Link href={GITHUB_REPO_URL} target="_blank" rel="noreferrer" className={linkClass}>
              GitHub
            </Link>
            .
          </p>
        </section>
      </div>
    </article>
  );
}
