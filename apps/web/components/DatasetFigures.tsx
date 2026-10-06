import * as React from "react";

import { UpdatedAgo } from "@/components/UpdatedAgo";
import { cn } from "@/lib/cn";
import {
  crawledPlatformCount,
  formatCompactCount,
  formatCount,
  type DatasetStats
} from "@/lib/datasetStats";

type Figure = { label: string; value: React.ReactNode };

// The corpus behind every number on the site: how many matches, how fast they arrive, and from
// where. A flat label/value readout in neutral ink, sized like a table cell rather than a hero
// metric, so it backs up the page instead of competing with it. Figures the backend could not
// determine are left out rather than shown as zero.
export function DatasetFigures({ stats, className }: { stats: DatasetStats; className?: string }) {
  const figures: Figure[] = [
    { label: "Matches stored", value: formatCount(stats.matchesStored) },
    { label: "Last 24 hours", value: formatCount(stats.matchesLast24Hours) },
    { label: "7-day daily avg", value: formatCount(stats.matchesPerDayLast7Days) },
    { label: "Platforms", value: formatCount(crawledPlatformCount(stats)) }
  ];

  if (stats.playersIndexedEstimate != null) {
    figures.push({
      label: "Players indexed",
      value: (
        <>
          <span aria-hidden="true">≈</span>
          <span className="sr-only">about </span>
          {formatCompactCount(stats.playersIndexedEstimate)}
        </>
      )
    });
  }

  if (stats.lastMatchIngestedAtUtc) {
    figures.push({
      label: "Last ingest",
      value: (
        <UpdatedAgo timestamp={stats.lastMatchIngestedAtUtc} prefix="" className="text-fg" />
      )
    });
  }

  return (
    <dl className={cn("grid grid-cols-2 gap-x-6 gap-y-4 sm:grid-cols-3 xl:grid-cols-6", className)}>
      {figures.map((figure) => (
        <div key={figure.label} className="grid min-w-0 content-start gap-1">
          <dt className="type-overline text-muted">{figure.label}</dt>
          {/* min-h holds the row while the client-side relative time mounts. */}
          <dd className="type-tabular min-h-7 text-xl font-semibold leading-7 tabular-nums text-fg">
            {figure.value}
          </dd>
        </div>
      ))}
    </dl>
  );
}
