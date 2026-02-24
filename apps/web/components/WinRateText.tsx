import { cn } from "@/lib/cn";
import { formatGames, formatPercent, winRateColorClass } from "@/lib/format";

export function WinRateText({
  value,
  decimals = 1,
  games,
  className
}: {
  value: number | null | undefined;
  decimals?: number;
  games?: number | null;
  className?: string;
}) {
  return (
    <span className={cn(winRateColorClass(value), className)}>
      {formatPercent(value, { decimals })}
      {games != null && Number.isFinite(games) ? (
        <span className="ml-1 text-muted">({formatGames(games)})</span>
      ) : null}
    </span>
  );
}
