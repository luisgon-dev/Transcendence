"use client";

import Image from "next/image";
import Link from "next/link";
import { useMemo, useState } from "react";

import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { normalizeAnalyticsSample } from "@/lib/analyticsSample";
import {
  compTierColorClass,
  compTierLabel,
  formatTftPercent,
  formatTftSigned,
  getTftBot4Rate,
  getTftEffectiveSampleSize,
  getTftMetaScore,
  formatTftCompName,
  formatTftLabel,
  formatTftUnitLabel,
  tftIconUrl,
  type TftEntityMap,
  type TftLabelMap,
  type TftCompListItem
} from "@/lib/tft";

type SortOption =
  | "META_SCORE"
  | "AVG_PLACEMENT"
  | "TOP4_RATE"
  | "WIN_RATE"
  | "CONTEST_RATE";

function scopeLabel(comp: TftCompListItem, requestedRankTier: string, requestedRegion: string) {
  const fallbackBits: string[] = [];

  if (requestedRankTier !== "ALL" && comp.rankScopeUsed && comp.rankScopeUsed !== requestedRankTier) {
    fallbackBits.push(`rank ${comp.rankScopeUsed}`);
  }
  if (requestedRegion !== "ALL" && comp.regionScopeUsed && comp.regionScopeUsed !== requestedRegion) {
    fallbackBits.push(`region ${comp.regionScopeUsed}`);
  }

  return fallbackBits.length > 0 ? `Fallback: ${fallbackBits.join(" · ")}` : null;
}

function sampleBadgeClass(comp: TftCompListItem) {
  const sample = normalizeAnalyticsSample(comp);
  if (!sample) return "surface-chip text-fg/72";
  if (sample.status === "no_data") return "border-danger/40 bg-danger/12 text-danger";
  if (sample.status === "low_sample") return "border-warning/40 bg-warning/12 text-warning";
  return "border-success/35 bg-success/10 text-success";
}

export function TftCompList({
  comps,
  qs,
  requestedRankTier,
  requestedRegion,
  traitLabels,
  unitLabels,
  unitEntities
}: {
  comps: TftCompListItem[];
  qs: string;
  requestedRankTier: string;
  requestedRegion: string;
  traitLabels?: TftLabelMap;
  unitLabels?: TftLabelMap;
  unitEntities?: TftEntityMap;
}) {
  const [sort, setSort] = useState<SortOption>("META_SCORE");
  const hasAdvancedAnalytics = comps.some(
    (comp) =>
      typeof comp.metaScore === "number" ||
      typeof comp.contestRate === "number" ||
      typeof comp.forceabilityIndex === "number" ||
      typeof comp.consistencyScore === "number" ||
      typeof comp.effectiveSampleSize === "number"
  );
  const hasContestRate = comps.some((comp) => typeof comp.contestRate === "number");

  const sorted = useMemo(() => {
    const items = comps.slice();
    const sampleStrength = (comp: TftCompListItem) => {
      const sample = normalizeAnalyticsSample(comp);
      if (!sample) return 0;
      if (sample.status === "sufficient") return 2;
      if (sample.status === "low_sample") return 1;
      return 0;
    };

    if (sort === "WIN_RATE") items.sort((a, b) => b.winRate - a.winRate);
    else if (sort === "TOP4_RATE") items.sort((a, b) => b.top4Rate - a.top4Rate);
    else if (sort === "CONTEST_RATE") {
      items.sort((a, b) => {
        const aContest = typeof a.contestRate === "number" ? a.contestRate : Number.POSITIVE_INFINITY;
        const bContest = typeof b.contestRate === "number" ? b.contestRate : Number.POSITIVE_INFINITY;
        return aContest - bContest;
      });
    }
    else if (sort === "AVG_PLACEMENT") items.sort((a, b) => a.avgPlacement - b.avgPlacement);
    else {
      items.sort((a, b) => {
        const sampleDelta = sampleStrength(b) - sampleStrength(a);
        if (sampleDelta !== 0) return sampleDelta;
        const effectiveSampleDelta = getTftEffectiveSampleSize(b) - getTftEffectiveSampleSize(a);
        if (effectiveSampleDelta !== 0) return effectiveSampleDelta;
        const metaDelta = getTftMetaScore(b) - getTftMetaScore(a);
        if (metaDelta !== 0) return metaDelta;
        return a.avgPlacement - b.avgPlacement;
      });
    }
    return items;
  }, [comps, sort]);
  const stableCompCount = comps.filter((comp) => normalizeAnalyticsSample(comp)?.status === "sufficient").length;

  const sortOptions: Array<{ value: SortOption; label: string }> = [
    { value: "META_SCORE", label: hasAdvancedAnalytics ? "Meta Score" : "Top 4 Snapshot" },
    { value: "AVG_PLACEMENT", label: "Avg Placement" },
    { value: "TOP4_RATE", label: "Top 4 Rate" },
    { value: "WIN_RATE", label: "Win Rate" },
    ...(hasContestRate ? [{ value: "CONTEST_RATE" as const, label: "Least Contested" }] : [])
  ];

  return (
    <div className="grid gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <span className="type-meta text-fg/60">Sort by</span>
        <select
          value={sort}
          onChange={(e) => setSort(e.target.value as SortOption)}
          className="control-select h-10 max-w-[180px] text-sm"
        >
          {sortOptions.map((opt) => (
            <option key={opt.value} value={opt.value}>{opt.label}</option>
          ))}
        </select>
        <span className="type-ui text-fg/55">
          Rankings favor patch-recent, data-backed boards with lower uncertainty.
        </span>
      </div>

      {sorted.length === 0 ? (
        <Card className="p-5">
          <p className="text-sm text-muted">No comps match the current filters yet.</p>
        </Card>
      ) : null}

      {sorted.length > 0 && stableCompCount === 0 ? (
        <Card className="border-warning/35 bg-warning/8 p-5">
          <p className="text-sm font-medium text-fg">Current TFT comp sample is still thin.</p>
          <p className="mt-1 text-sm text-fg/70">
            Boards below are exploratory. Rankings are weighted toward sample size before raw placement so early one-off finishes do not read like a settled meta.
          </p>
        </Card>
      ) : null}

      <div className="grid gap-3 lg:grid-cols-2">
        {sorted.map((comp) => {
          const tier = compTierLabel(comp.avgPlacement);
          const tierColor = compTierColorClass(tier);
          const scope = scopeLabel(comp, requestedRankTier, requestedRegion);
          const sample = normalizeAnalyticsSample(comp);
          const sampleLabel = (comp.sampleStatus ?? sample?.status ?? "sufficient").replace(/_/g, " ");
          const metaScore = getTftMetaScore(comp);
          const statItems = [
            `Avg Place ${comp.avgPlacement.toFixed(2)}`,
            `Top 4 ${formatTftPercent(comp.top4Rate)}`,
            `Win ${formatTftPercent(comp.winRate)}`,
            ...(typeof comp.bot4Rate === "number" ? [`Bot 4 ${formatTftPercent(getTftBot4Rate(comp))}`] : []),
            ...(typeof comp.contestRate === "number" ? [`Contest ${formatTftPercent(comp.contestRate)}`] : []),
            ...(typeof comp.placementDelta === "number"
              ? [`Placement Δ ${formatTftSigned(comp.placementDelta)}`]
              : []),
            `Games ${Math.round(getTftEffectiveSampleSize(comp)).toLocaleString()}`
          ];
          const detailStats = [
            ...(typeof comp.forceabilityIndex === "number"
              ? [{ label: "Forceability", value: formatTftPercent(comp.forceabilityIndex) }]
              : []),
            ...(typeof comp.consistencyScore === "number"
              ? [{ label: "Consistency", value: formatTftPercent(comp.consistencyScore) }]
              : []),
            ...(typeof comp.effectiveSampleSize === "number"
              ? [{ label: "Effective Sample", value: Math.round(comp.effectiveSampleSize).toLocaleString() }]
              : [])
          ];

          return (
            <Link key={comp.compSlug} href={`/tft/comps/${comp.compSlug}${qs ? `?${qs}` : ""}`}>
              <Card className="grid gap-3 p-5 transition hover:bg-white/8">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <p className="text-lg font-semibold text-fg">
                        {formatTftCompName(comp.name, { traitLabels, unitLabels })}
                      </p>
                      <Badge className={`rounded-md px-2 py-0.5 text-xs font-bold ${tierColor}`}>
                        {tier}
                      </Badge>
                      <Badge className={`rounded-md px-2 py-0.5 text-[11px] font-semibold ${sampleBadgeClass(comp)}`}>
                        {sampleLabel}
                      </Badge>
                    </div>
                    <p className="text-xs text-muted">
                      {comp.setCoreName ?? `Set ${comp.setNumber ?? "?"}`} · {comp.patch ?? "Unknown patch"}
                      {typeof comp.patchAgeHours === "number"
                        ? ` · patch age ${Math.max(0, Math.round(comp.patchAgeHours))}h`
                        : ""}
                    </p>
                    {scope ? <p className="mt-1 text-xs text-warning">{scope}</p> : null}
                  </div>
                  <div className="text-right">
                    <p className="type-kicker text-fg/55">{hasAdvancedAnalytics ? "Meta Score" : "Top 4 Snapshot"}</p>
                    <p className="text-xl font-semibold text-fg">
                      {hasAdvancedAnalytics ? metaScore.toFixed(2) : formatTftPercent(comp.top4Rate)}
                    </p>
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-2 text-sm text-fg/80 xl:grid-cols-3">
                  {statItems.map((item) => (
                    <p key={item}>{item}</p>
                  ))}
                </div>

                {detailStats.length > 0 ? (
                  <div className={`grid gap-2 text-[11px] text-fg/60 ${detailStats.length === 1 ? "grid-cols-1" : detailStats.length === 2 ? "grid-cols-2" : "grid-cols-3"}`}>
                    {detailStats.map((item) => (
                      <div key={item.label} className="rounded-lg border border-border/50 bg-surface/35 px-2.5 py-2">
                        <p>{item.label}</p>
                        <p className="mt-1 text-sm font-semibold text-fg">{item.value}</p>
                      </div>
                    ))}
                  </div>
                ) : null}

                <div className="flex flex-wrap gap-1">
                  {comp.units.map((unit) => (
                    <span key={unit.characterId} className="type-caption inline-flex items-center gap-1.5 rounded border border-primary/25 bg-primary/8 px-1.5 py-1 font-medium text-primary">
                      {tftIconUrl(unitEntities?.[unit.characterId]?.icon) ? (
                        <Image
                          src={tftIconUrl(unitEntities?.[unit.characterId]?.icon)!}
                          alt={formatTftUnitLabel(unit, unitLabels)}
                          width={20}
                          height={20}
                          sizes="20px"
                          className="h-5 w-5 rounded object-cover"
                        />
                      ) : null}
                      {formatTftUnitLabel(unit, unitLabels)}
                    </span>
                  ))}
                </div>

                <div className="flex flex-wrap gap-1">
                  {comp.traits.map((trait) => (
                    <span key={trait.name} className="type-caption rounded-full border border-border/50 bg-surface/40 px-1.5 py-0.5 text-fg/60">
                      {formatTftLabel(trait.name, traitLabels)} {trait.numUnits}
                    </span>
                  ))}
                </div>
              </Card>
            </Link>
          );
        })}
      </div>
    </div>
  );
}
