import { cn } from "@/lib/cn";

// A real inline trend line from a series of numbers (e.g. recent win rates or
// placements). Stroke color reflects the net direction. Decorative-but-honest:
// only render when there is genuine series data to show.
export function Sparkline({
  values,
  width = 56,
  height = 18,
  className,
  invert = false
}: {
  values: number[];
  width?: number;
  height?: number;
  className?: string;
  /** Set when lower is better (e.g. a placement where 1st is best). */
  invert?: boolean;
}) {
  const clean = values.filter((v) => Number.isFinite(v));
  if (clean.length < 2) {
    return <span className={cn("type-tabular text-muted", className)}>—</span>;
  }

  const min = Math.min(...clean);
  const max = Math.max(...clean);
  const span = max - min || 1;
  const step = width / (clean.length - 1);

  const points = clean.map((v, i) => {
    const x = i * step;
    const y = height - ((v - min) / span) * height;
    return `${x.toFixed(1)},${y.toFixed(1)}`;
  });

  const first = clean[0];
  const last = clean[clean.length - 1];
  let rising = last > first;
  if (invert) rising = !rising;
  const stroke = last === first ? "var(--t-muted)" : rising ? "var(--t-win)" : "var(--t-loss)";

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      width={width}
      height={height}
      className={cn("overflow-visible", className)}
      role="img"
      aria-label={`Trend ${rising ? "up" : last === first ? "flat" : "down"}`}
    >
      <polyline
        points={points.join(" ")}
        fill="none"
        stroke={stroke}
        strokeWidth="1.5"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <circle cx={points[points.length - 1].split(",")[0]} cy={points[points.length - 1].split(",")[1]} r="1.8" fill={stroke} />
    </svg>
  );
}
