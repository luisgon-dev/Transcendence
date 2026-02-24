import { cn } from "@/lib/cn";
import { formatGames, formatPercent, winRateColorClass } from "@/lib/format";
import { type UITierGrade } from "@/lib/tierlist";

import { TierBadge } from "./TierBadge";

type StatCell = {
  label: string;
  value: string;
  colorClass?: string;
};

export function StatsBar({
  tier,
  winRate,
  pickRate,
  games,
  className
}: {
  tier?: UITierGrade | null;
  winRate?: number | null;
  pickRate?: number | null;
  games?: number | null;
  className?: string;
}) {
  const cells: (StatCell | null)[] = [
    tier
      ? null // rendered separately as TierBadge
      : null,
    winRate != null
      ? {
          label: "Win Rate",
          value: formatPercent(winRate, { decimals: 2 }),
          colorClass: winRateColorClass(winRate)
        }
      : null,
    pickRate != null
      ? { label: "Pick Rate", value: formatPercent(pickRate, { decimals: 1 }) }
      : null,
    games != null
      ? { label: "Matches", value: formatGames(games) }
      : null
  ];

  const validCells = cells.filter(Boolean) as StatCell[];

  return (
    <div
      className={cn(
        "flex flex-wrap items-center gap-3 rounded-lg border border-border/60 bg-surface/35 px-4 py-3",
        className
      )}
    >
      {tier ? (
        <div className="grid gap-1 text-center">
          <span className="text-[10px] uppercase tracking-wider text-muted">
            Tier
          </span>
          <TierBadge tier={tier} size="md" />
        </div>
      ) : null}

      {validCells.map((cell) => (
        <div key={cell.label} className="grid gap-1 text-center">
          <span className="text-[10px] uppercase tracking-wider text-muted">
            {cell.label}
          </span>
          <span className={cn("text-sm font-semibold", cell.colorClass)}>
            {cell.value}
          </span>
        </div>
      ))}
    </div>
  );
}
