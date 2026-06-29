import { cn } from "@/lib/cn";
import { formatGames, formatPercent, winRateColorClass } from "@/lib/format";

export function WinRateText({
  value,
  decimals = 1,
  games,
  pickRate,
  className
}: {
  value: number | null | undefined;
  decimals?: number;
  games?: number | null;
  /** 0-1 share of games on this variant; rendered only when present and > 0. */
  pickRate?: number | null;
  className?: string;
}) {
  const showPick = pickRate != null && Number.isFinite(pickRate) && pickRate > 0;
  return (
    <span className={cn(winRateColorClass(value), className)}>
      {formatPercent(value, { decimals })}
      {games != null && Number.isFinite(games) ? (
        <span className="ml-1 text-muted">({formatGames(games)})</span>
      ) : null}
      {showPick ? (
        <span className="ml-1.5 text-muted">· {formatPercent(pickRate, { decimals: 0 })} pick</span>
      ) : null}
    </span>
  );
}
