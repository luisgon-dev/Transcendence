import { cn } from "@/lib/cn";
import { formatPercent, toWinPercent } from "@/lib/format";

// Diverging win-rate bar centered on 50%. Above 50 fills right (win color),
// below fills left (loss color); magnitude maps to width over a ±`domain`
// window so small but meaningful deltas stay readable. This is the core data
// language of the refresh — every win rate renders as one of these.
export function DataBar({
  value,
  domain = 8,
  decimals = 1,
  showValue = true,
  className
}: {
  value: number | null | undefined;
  /** Half-window in percentage points that maps to a full half-bar. */
  domain?: number;
  decimals?: number;
  showValue?: boolean;
  className?: string;
}) {
  const pct = toWinPercent(value);

  if (pct == null) {
    return (
      <span className={cn("type-tabular text-muted tabular-nums", className)}>—</span>
    );
  }

  const delta = pct - 50;
  const magnitude = Math.min(Math.abs(delta) / domain, 1) * 50; // 0–50% of track
  const positive = delta >= 0;
  const colorVar = positive ? "var(--t-win)" : "var(--t-loss)";

  return (
    <span className={cn("inline-flex items-center gap-2", className)}>
      <span
        className="relative hidden h-[7px] w-[clamp(56px,9vw,104px)] overflow-hidden rounded-full bg-surface-2 sm:block"
        aria-hidden="true"
      >
        {/* center reference tick */}
        <span className="absolute inset-y-0 left-1/2 w-px -translate-x-1/2 bg-border-strong/70" />
        <span
          className="absolute inset-y-0 rounded-full"
          style={
            positive
              ? { left: "50%", width: `${magnitude}%`, background: colorVar }
              : { right: "50%", width: `${magnitude}%`, background: colorVar }
          }
        />
      </span>
      {showValue ? (
        <span
          className="type-tabular tabular-nums font-semibold"
          style={{ color: Math.abs(delta) < 0.5 ? "var(--t-muted)" : colorVar }}
        >
          {formatPercent(pct, { decimals, input: "percent" })}
        </span>
      ) : null}
    </span>
  );
}
