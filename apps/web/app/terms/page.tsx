import type { Metadata } from "next";
import Link from "next/link";

export const metadata: Metadata = {
  title: "Terms & Conditions — Transcendence"
};

const GITHUB_REPO_URL = "https://github.com/luisgon-dev/Transcendence";

export default function TermsPage() {
  return (
    <article className="mx-auto max-w-3xl space-y-10">
      <header className="page-hero p-6 md:p-8">
        <p className="type-kicker text-primary">Legal</p>
        <h1 className="type-title mt-3 md:text-[2.3rem]">
          Terms &amp; Conditions
        </h1>
        <p className="type-ui mt-3 text-muted">Last updated: March 2026</p>
      </header>

      {/* ── Section 1: Riot Games Disclaimer ── */}
      <section className="space-y-3">
        <h2 className="type-section text-fg">
          Riot Games Disclaimer
        </h2>
        <div className="rounded-lg border border-border/60 bg-surface/50 p-4">
          <p className="text-sm leading-relaxed text-fg/90">
            Transcendence isn&apos;t endorsed by Riot Games and doesn&apos;t
            reflect the views or opinions of Riot Games or anyone officially
            involved in producing or managing Riot Games properties. Riot Games,
            and all associated properties are trademarks or registered trademarks
            of Riot Games, Inc.
          </p>
        </div>
        <p className="text-sm leading-relaxed text-muted">
          All game data, including champion information, match history, summoner
          profiles, and related assets, is provided by the{" "}
          <Link
            href="https://developer.riotgames.com"
            target="_blank"
            rel="noreferrer"
            className="underline underline-offset-2 transition hover:text-fg"
          >
            Riot Games API
          </Link>{" "}
          and remains the intellectual property of Riot Games, Inc. Use of this
          data is subject to the{" "}
          <Link
            href="https://developer.riotgames.com/terms"
            target="_blank"
            rel="noreferrer"
            className="underline underline-offset-2 transition hover:text-fg"
          >
            Riot Games API Terms of Service
          </Link>
          .
        </p>
      </section>

      {/* ── Section 2: Terms of Service ── */}
      <section className="space-y-3">
        <h2 className="type-section text-fg">
          Terms of Service
        </h2>
        <div className="space-y-3 text-sm leading-relaxed text-muted">
          <p>
            Transcendence is a free, open-source League of Legends analytics
            platform. By using this service you agree to the following terms.
          </p>

          <h3 className="font-medium text-fg/90">Acceptable Use</h3>
          <ul className="list-inside list-disc space-y-1 pl-1">
            <li>
              The service is provided for informational and analytical purposes
              only.
            </li>
            <li>
              You may not use Transcendence to gain an unfair competitive
              advantage, automate gameplay, or violate the{" "}
              <Link
                href="https://www.riotgames.com/en/terms-of-service"
                target="_blank"
                rel="noreferrer"
                className="underline underline-offset-2 transition hover:text-fg"
              >
                Riot Games Terms of Service
              </Link>
              .
            </li>
            <li>
              Automated scraping, crawling, or bulk data extraction from
              Transcendence is prohibited.
            </li>
          </ul>

          <h3 className="font-medium text-fg/90">Accounts</h3>
          <ul className="list-inside list-disc space-y-1 pl-1">
            <li>
              You are responsible for maintaining the confidentiality of your
              account credentials.
            </li>
            <li>
              Transcendence reserves the right to suspend or terminate accounts
              that violate these terms or engage in abusive behavior.
            </li>
          </ul>

          <h3 className="font-medium text-fg/90">Service Availability</h3>
          <ul className="list-inside list-disc space-y-1 pl-1">
            <li>
              Transcendence is provided on an &ldquo;as is&rdquo; basis without
              warranties of any kind.
            </li>
            <li>
              We reserve the right to modify, suspend, or discontinue the
              service at any time without notice.
            </li>
            <li>
              Data accuracy depends on the Riot Games API and may not always be
              up to date.
            </li>
          </ul>
        </div>
      </section>

      {/* ── Section 3: Privacy Policy ── */}
      <section className="space-y-3">
        <h2 className="type-section text-fg">
          Privacy Policy
        </h2>
        <div className="space-y-3 text-sm leading-relaxed text-muted">
          <h3 className="font-medium text-fg/90">Information We Collect</h3>
          <ul className="list-inside list-disc space-y-1 pl-1">
            <li>
              <span className="text-fg/80">Account data:</span> email address
              and a securely hashed password when you register.
            </li>
            <li>
              <span className="text-fg/80">Summoner data:</span> summoner names,
              regions, and match history retrieved from the Riot Games API when
              you perform searches.
            </li>
            <li>
              <span className="text-fg/80">Favorites:</span> summoner profiles
              you choose to save to your account.
            </li>
            <li>
              <span className="text-fg/80">Session cookies:</span> used for
              authentication and session management.
            </li>
          </ul>

          <h3 className="font-medium text-fg/90">
            Information We Do Not Collect
          </h3>
          <ul className="list-inside list-disc space-y-1 pl-1">
            <li>
              We never store your Riot Games account credentials or passwords in
              plain text.
            </li>
            <li>
              We do not collect payment information, precise location data, or
              device fingerprints.
            </li>
          </ul>

          <h3 className="font-medium text-fg/90">How We Use Your Data</h3>
          <ul className="list-inside list-disc space-y-1 pl-1">
            <li>
              To provide analytics and display summoner profiles and match
              history.
            </li>
            <li>To authenticate your account and manage your session.</li>
            <li>
              We do not sell, rent, or share your personal data with third
              parties.
            </li>
          </ul>

          <h3 className="font-medium text-fg/90">Data Retention &amp; Deletion</h3>
          <ul className="list-inside list-disc space-y-1 pl-1">
            <li>
              Game data sourced from the Riot API is cached to improve
              performance and may be refreshed periodically.
            </li>
            <li>
              You may request deletion of your account and associated data by
              contacting us through the{" "}
              <Link
                href={`${GITHUB_REPO_URL}/issues`}
                target="_blank"
                rel="noreferrer"
                className="underline underline-offset-2 transition hover:text-fg"
              >
                GitHub repository
              </Link>
              .
            </li>
          </ul>
        </div>
      </section>

      {/* ── Section 4: Open Source License ── */}
      <section className="space-y-3">
        <h2 className="type-section text-fg">
          Open Source License
        </h2>
        <p className="text-sm leading-relaxed text-muted">
          Transcendence is open-source software licensed under the{" "}
          <Link
            href={`${GITHUB_REPO_URL}/blob/main/LICENSE`}
            target="_blank"
            rel="noreferrer"
            className="underline underline-offset-2 transition hover:text-fg"
          >
            GNU General Public License v3.0
          </Link>
          . You are free to use, modify, and distribute the source code under
          the terms of that license. The full source code is available on{" "}
          <Link
            href={GITHUB_REPO_URL}
            target="_blank"
            rel="noreferrer"
            className="underline underline-offset-2 transition hover:text-fg"
          >
            GitHub
          </Link>
          .
        </p>
      </section>
    </article>
  );
}
