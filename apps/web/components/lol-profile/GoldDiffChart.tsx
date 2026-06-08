import { cn } from "@/lib/cn";
import type { TimelineFrame } from "@/components/lol-profile/shared";

// Gold lead over time: blue team's gold advantage (blueGold - redGold) plotted
// against a 0 baseline — area above is the blue team's lead, below is the red
// team's. Color encodes which team is ahead (team-blue / team-red), no gold.
export function GoldDiffChart({ frames, className }: { frames: TimelineFrame[]; className?: string }) {
  if (frames.length < 2) return null;

  const W = 320;
  const H = 76;
  const pad = 6;

  const points = frames.map((f) => ({ minute: f.minuteMark, diff: f.blueGold - f.redGold }));
  const maxAbs = Math.max(1, ...points.map((p) => Math.abs(p.diff)));
  const minMinute = points[0].minute;
  const maxMinute = points[points.length - 1].minute;
  const minuteSpan = Math.max(1, maxMinute - minMinute);
  const mid = H / 2;

  const x = (minute: number) => pad + ((minute - minMinute) / minuteSpan) * (W - 2 * pad);
  const y = (diff: number) => mid - (diff / maxAbs) * (mid - pad);

  const line = points.map((p) => `${x(p.minute).toFixed(1)},${y(p.diff).toFixed(1)}`).join(" ");
  const area =
    `M ${x(points[0].minute).toFixed(1)},${mid.toFixed(1)} ` +
    points.map((p) => `L ${x(p.minute).toFixed(1)},${y(p.diff).toFixed(1)}`).join(" ") +
    ` L ${x(points[points.length - 1].minute).toFixed(1)},${mid.toFixed(1)} Z`;

  const finalDiff = points[points.length - 1].diff;
  const ahead = finalDiff >= 0;
  const lineColor = ahead ? "var(--color-team-blue)" : "var(--color-team-red)";

  return (
    <svg
      viewBox={`0 0 ${W} ${H}`}
      preserveAspectRatio="none"
      className={cn("h-[76px] w-full", className)}
      role="img"
      aria-label={`Gold lead curve; ${ahead ? "blue" : "red"} team ahead by ${Math.abs(finalDiff).toLocaleString()} at the end`}
    >
      <defs>
        <linearGradient id="golddiff-grad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="var(--color-team-blue)" stopOpacity="0.32" />
          <stop offset="50%" stopColor="var(--color-team-blue)" stopOpacity="0.04" />
          <stop offset="50%" stopColor="var(--color-team-red)" stopOpacity="0.04" />
          <stop offset="100%" stopColor="var(--color-team-red)" stopOpacity="0.32" />
        </linearGradient>
      </defs>
      <line
        x1={pad}
        y1={mid}
        x2={W - pad}
        y2={mid}
        stroke="var(--color-border-strong)"
        strokeWidth="1"
        strokeDasharray="3 3"
      />
      <path d={area} fill="url(#golddiff-grad)" />
      <polyline points={line} fill="none" stroke={lineColor} strokeWidth="1.5" strokeLinejoin="round" strokeLinecap="round" />
    </svg>
  );
}
