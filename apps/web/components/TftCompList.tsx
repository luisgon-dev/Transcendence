"use client";

import Link from "next/link";
import { useMemo, useState } from "react";

import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import {
  compTierColorClass,
  compTierLabel,
  formatTftPercent,
  type TftCompListItem
} from "@/lib/tft";

type SortOption = "AVG_PLACEMENT" | "WIN_RATE" | "TOP4_RATE" | "PICK_RATE";

export function TftCompList({
  comps,
  qs
}: {
  comps: TftCompListItem[];
  qs: string;
}) {
  const [sort, setSort] = useState<SortOption>("AVG_PLACEMENT");

  const sorted = useMemo(() => {
    const items = comps.slice();
    if (sort === "WIN_RATE") items.sort((a, b) => b.winRate - a.winRate);
    else if (sort === "TOP4_RATE") items.sort((a, b) => b.top4Rate - a.top4Rate);
    else if (sort === "PICK_RATE") items.sort((a, b) => b.pickRate - a.pickRate);
    else items.sort((a, b) => a.avgPlacement - b.avgPlacement);
    return items;
  }, [comps, sort]);

  const sortOptions: Array<{ value: SortOption; label: string }> = [
    { value: "AVG_PLACEMENT", label: "Avg Placement" },
    { value: "WIN_RATE", label: "Win Rate" },
    { value: "TOP4_RATE", label: "Top 4 Rate" },
    { value: "PICK_RATE", label: "Pick Rate" }
  ];

  return (
    <div className="grid gap-4">
      <div className="flex items-center gap-2">
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
      </div>

      {sorted.length === 0 ? (
        <Card className="p-5">
          <p className="text-sm text-muted">No comps match the current filters yet.</p>
        </Card>
      ) : null}

      <div className="grid gap-3 lg:grid-cols-2">
        {sorted.map((comp) => {
          const tier = compTierLabel(comp.avgPlacement);
          const tierColor = compTierColorClass(tier);
          return (
            <Link key={comp.compSlug} href={`/tft/comps/${comp.compSlug}${qs ? `?${qs}` : ""}`}>
              <Card className="grid gap-3 p-5 transition hover:bg-white/8">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="text-lg font-semibold text-fg">{comp.name}</p>
                    <p className="text-xs text-muted">
                      {comp.setCoreName ?? `Set ${comp.setNumber ?? "?"}`} · {comp.patch ?? "Unknown patch"}
                    </p>
                  </div>
                  <Badge className={`rounded-md px-2 py-0.5 text-xs font-bold ${tierColor}`}>
                    {tier}
                  </Badge>
                </div>

                <div className="grid grid-cols-2 gap-2 text-sm text-fg/80">
                  <p>Avg Place <span className="font-medium text-fg">{comp.avgPlacement.toFixed(2)}</span></p>
                  <p>Top 4 <span className="font-medium text-fg">{formatTftPercent(comp.top4Rate)}</span></p>
                  <p>Win <span className="font-medium text-fg">{formatTftPercent(comp.winRate)}</span></p>
                  <p>Pick <span className="font-medium text-fg">{formatTftPercent(comp.pickRate)}</span></p>
                </div>

                <div className="flex flex-wrap gap-1">
                  {comp.units.map((unit) => (
                    <span key={unit.characterId} className="type-caption rounded border border-primary/25 bg-primary/8 px-1.5 py-0.5 font-medium text-primary">
                      {unit.name ?? unit.characterId}
                    </span>
                  ))}
                </div>

                <div className="flex flex-wrap gap-1">
                  {comp.traits.map((trait) => (
                    <span key={trait.name} className="type-caption rounded-full border border-border/50 bg-surface/40 px-1.5 py-0.5 text-fg/60">
                      {trait.name} {trait.numUnits}
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
