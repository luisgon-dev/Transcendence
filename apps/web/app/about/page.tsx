import type { Metadata } from "next";
import Link from "next/link";
import type { ReactNode } from "react";

import { DatasetFigures } from "@/components/DatasetFigures";
import { EmptyState } from "@/components/ui/EmptyState";
import { Table, TableScroll, Td, Th } from "@/components/ui/Table";
import { Toolbar } from "@/components/ui/Toolbar";
import { UpdatedAgo } from "@/components/UpdatedAgo";
import {
  crawledPlatformCount,
  formatBytes,
  formatCount,
  type DatasetStats
} from "@/lib/datasetStats";
import { fetchDatasetStats } from "@/lib/datasetStatsServer";
import { socialImageUrl } from "@/lib/seo";

const title = "About the data";
const description =
  "How many ranked matches back Transcendence, where they come from, and how they become tier lists and builds.";
const image = socialImageUrl(title, "Transcendence", "Matches, platforms, and how they are collected");

export const metadata: Metadata = {
  title,
  description,
  alternates: { canonical: "/about" },
  openGraph: {
    type: "website",
    title,
    description,
    url: "/about",
    images: [{ url: image, width: 1200, height: 630, alt: title }]
  },
  twitter: { card: "summary_large_image", title, description, images: [image] }
};

const GITHUB_REPO_URL = "https://github.com/luisgon-dev/Transcendence";

const linkClass =
  "rounded-sm font-medium text-fg underline decoration-border-strong underline-offset-2 transition-colors hover:decoration-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/35 focus-visible:ring-offset-2 focus-visible:ring-offset-surface";

const share = new Intl.NumberFormat("en-US", { style: "percent", maximumFractionDigits: 1 });

function SectionHeading({ id, children }: { id: string; children: ReactNode }) {
  return (
    <h2 id={id} className="type-section text-fg">
      {children}
    </h2>
  );
}

function Coverage({ stats }: { stats: DatasetStats }) {
  return (
    <section aria-labelledby="coverage-heading" className="page-panel grid gap-5 p-5 sm:p-6">
      <SectionHeading id="coverage-heading">Coverage</SectionHeading>
      <DatasetFigures stats={stats} />
      <div className="border-t border-border/45 pt-4">
        <p className="type-note text-muted">
          {stats.activePatch ? (
            <>
              <span className="type-tabular font-semibold text-fg">
                {formatCount(stats.activePatchMatches)}
              </span>{" "}
              matches on the current patch, {stats.activePatch}.{" "}
            </>
          ) : null}
          {stats.databaseSizeBytes != null ? (
            <>
              <span className="type-tabular font-semibold text-fg">
                {formatBytes(stats.databaseSizeBytes)}
              </span>{" "}
              in PostgreSQL.
            </>
          ) : null}
        </p>
      </div>
    </section>
  );
}

function PlatformTable({ stats }: { stats: DatasetStats }) {
  if (stats.platforms.length === 0) return null;

  return (
    <section aria-labelledby="platforms-heading" className="page-panel overflow-hidden">
      <div className="px-5 pt-5 sm:px-6 sm:pt-6">
        <SectionHeading id="platforms-heading">Matches by platform</SectionHeading>
        <p className="type-note mt-1 text-muted">
          Where each stored match was played, largest first.
        </p>
      </div>
      <TableScroll className="mt-3">
        <Table className="min-w-[320px] text-sm">
          <thead>
            <tr className="border-b border-border/50 bg-surface-2/35">
              <Th className="pl-5 sm:pl-6">Platform</Th>
              <Th align="right">Matches stored</Th>
              {/* Derived from the other columns, so it is the first to go on narrow screens. */}
              <Th align="right" className="hidden sm:table-cell">
                Share
              </Th>
              <Th align="right" className="pr-5 sm:pr-6">
                Last 24 hours
              </Th>
            </tr>
          </thead>
          <tbody>
            {stats.platforms.map((platform) => (
              <tr key={platform.platform} className="border-b border-border/25 last:border-b-0">
                <Td className="pl-5 sm:pl-6">
                  <span className="font-medium text-fg">{platform.label}</span>{" "}
                  <span className="type-caption text-muted">{platform.platform}</span>
                </Td>
                <Td align="right" className="type-tabular tabular-nums text-fg">
                  {formatCount(platform.matchesStored)}
                </Td>
                <Td align="right" className="type-tabular hidden tabular-nums text-muted sm:table-cell">
                  {stats.matchesStored > 0
                    ? share.format(platform.matchesStored / stats.matchesStored)
                    : "—"}
                </Td>
                <Td align="right" className="type-tabular pr-5 tabular-nums text-fg sm:pr-6">
                  {formatCount(platform.matchesLast24Hours)}
                </Td>
              </tr>
            ))}
          </tbody>
        </Table>
      </TableScroll>
    </section>
  );
}

function Pipeline({ platformCount }: { platformCount: number | null }) {
  const platforms = platformCount ? `${platformCount} platforms` : "every crawled platform";
  return (
    <section aria-labelledby="pipeline-heading" className="page-panel grid gap-4 p-5 sm:p-6">
      <SectionHeading id="pipeline-heading">How the data is collected</SectionHeading>
      <ol className="grid gap-4">
        <li className="grid gap-1">
          <span className="type-ui font-semibold text-fg">Crawl</span>
          <span className="type-note text-muted">
            Every two hours a worker reads the Challenger, Grandmaster, and Master solo queue
            ladders on {platforms}, skipping players whose rank has not moved.
          </span>
        </li>
        <li className="grid gap-1">
          <span className="type-ui font-semibold text-fg">Ingest</span>
          <span className="type-note text-muted">
            Every two minutes it pulls recent ranked games, starting with tracked pros and
            one-tricks, then favorited, Emerald+ and other active players, stalest first. A
            per-region rate gate keeps every call within Riot&apos;s API limits. Ranked Solo/Duo
            games also store their timelines.
          </span>
        </li>
        <li className="grid gap-1">
          <span className="type-ui font-semibold text-fg">Compute</span>
          <span className="type-note text-muted">
            Scheduled jobs turn those games into tier grades, builds, matchups, and item and rune
            stats. Win rates are shrunk toward each role&apos;s baseline, so a champion with a
            handful of games cannot top the list on luck.
          </span>
        </li>
        <li className="grid gap-1">
          <span className="type-ui font-semibold text-fg">Serve</span>
          <span className="type-note text-muted">
            Pages read precomputed snapshots and caches. Viewing a page never calls Riot or
            recounts the database; refreshes run as background jobs.
          </span>
        </li>
      </ol>
    </section>
  );
}

function Method() {
  return (
    <section aria-labelledby="method-heading" className="page-panel grid gap-3 p-5 sm:p-6">
      <SectionHeading id="method-heading">How these figures are counted</SectionHeading>
      <ul className="type-note grid list-disc gap-2 pl-5 text-muted">
        <li>
          A worker job recounts them every five minutes in one pass over the match table and
          stores the result. This page shows that stored result.
        </li>
        <li>
          Matches stored counts every successfully fetched match, across all queues. The 24-hour
          and daily figures use the time each match was fetched; the daily average covers the
          last seven complete UTC days.
        </li>
        <li>
          Players indexed is PostgreSQL&apos;s own estimate of the player table, which holds
          everyone seen in a stored match, not only crawled ladder players.
        </li>
        <li>
          The code is open source on{" "}
          <Link href={GITHUB_REPO_URL} target="_blank" rel="noreferrer" className={linkClass}>
            GitHub
          </Link>
          .
        </li>
      </ul>
    </section>
  );
}

export default async function AboutPage() {
  const stats = await fetchDatasetStats();

  return (
    <div className="grid gap-4">
      <Toolbar
        eyebrow="About"
        title="The data behind Transcendence"
        meta={
          stats ? (
            <>
              {stats.activePatch ? <span>Patch {stats.activePatch}</span> : null}
              {stats.activePatch ? <span aria-hidden="true">·</span> : null}
              <UpdatedAgo timestamp={stats.computedAtUtc} />
            </>
          ) : null
        }
      />

      {stats ? (
        <>
          <Coverage stats={stats} />
          <PlatformTable stats={stats} />
        </>
      ) : (
        <EmptyState
          title="Dataset figures are not available yet"
          description="A worker job recounts them every five minutes. Check back shortly."
        />
      )}

      <div className="grid gap-4 lg:grid-cols-2 lg:items-start">
        <Pipeline platformCount={stats ? crawledPlatformCount(stats) : null} />
        <Method />
      </div>
    </div>
  );
}
