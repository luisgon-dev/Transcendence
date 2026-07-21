import { Card } from "@/components/ui/Card";
import { formatPercent } from "@/lib/format";

export type ChampionTrendPoint = {
  patch: string;
  releasedAtUtc: string;
  tier: number | string;
  games: number;
  winRate: number;
  pickRate: number;
  banRate: number;
  strengthScore: number;
  isLowSample: boolean;
};

export type ChampionTrend = {
  queueFamily: string;
  role: string;
  rankScope: string;
  region: string;
  points: ChampionTrendPoint[];
};

const WIDTH = 720;
const HEIGHT = 190;
const PAD_X = 28;
const PAD_TOP = 18;
const PAD_BOTTOM = 38;

export function ChampionTrendChart({
  championName,
  trend
}: {
  championName: string;
  trend: ChampionTrend | null | undefined;
}) {
  const points = trend?.points?.filter((point) => Number.isFinite(point.winRate)) ?? [];
  if (points.length < 2) return null;

  const rates = points.map((point) => point.winRate);
  const rawMin = Math.min(...rates);
  const rawMax = Math.max(...rates);
  const spread = Math.max(0.02, rawMax - rawMin);
  const minRate = Math.max(0, rawMin - spread * 0.35);
  const maxRate = Math.min(1, rawMax + spread * 0.35);
  const chartHeight = HEIGHT - PAD_TOP - PAD_BOTTOM;
  const chartWidth = WIDTH - PAD_X * 2;
  const xFor = (index: number) => PAD_X + (index * chartWidth) / (points.length - 1);
  const yFor = (value: number) => PAD_TOP + ((maxRate - value) / (maxRate - minRate)) * chartHeight;
  const path = points
    .map((point, index) => `${index === 0 ? "M" : "L"} ${xFor(index).toFixed(1)} ${yFor(point.winRate).toFixed(1)}`)
    .join(" ");
  const first = points[0];
  const latest = points.at(-1)!;
  const deltaPoints = (latest.winRate - first.winRate) * 100;
  const deltaLabel = `${deltaPoints >= 0 ? "+" : ""}${deltaPoints.toFixed(1)} pp`;

  return (
    <Card className="overflow-hidden p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="type-kicker text-muted">Patch history</p>
          <h2 className="mt-1 type-section">Win-rate trend</h2>
          <p className="mt-1 text-xs text-muted">
            Global {trend?.rankScope === "EMERALD_PLUS" ? "Emerald+" : "all-rank"} aggregate · {points.length} patches
          </p>
        </div>
        <div className="text-right">
          <p className="text-lg font-semibold tabular-nums text-fg">
            {formatPercent(latest.winRate, { input: "ratio", decimals: 1 })}
          </p>
          <p className={deltaPoints >= 0 ? "text-xs font-medium text-success" : "text-xs font-medium text-danger"}>
            {deltaLabel} since {first.patch}
          </p>
        </div>
      </div>

      <figure className="mt-4" aria-label={`${championName} win rate across ${points.length} patches`}>
        <svg
          viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
          role="img"
          aria-label={`${championName} win rate moved from ${formatPercent(first.winRate, { input: "ratio", decimals: 1 })} on patch ${first.patch} to ${formatPercent(latest.winRate, { input: "ratio", decimals: 1 })} on patch ${latest.patch}`}
          className="h-auto w-full overflow-visible"
        >
          {[0, 0.5, 1].map((position) => {
            const y = PAD_TOP + position * chartHeight;
            const value = maxRate - position * (maxRate - minRate);
            return (
              <g key={position}>
                <line x1={PAD_X} x2={WIDTH - PAD_X} y1={y} y2={y} stroke="currentColor" className="text-border/45" />
                <text x={PAD_X} y={y - 5} fill="currentColor" className="text-[10px] text-muted">
                  {(value * 100).toFixed(1)}%
                </text>
              </g>
            );
          })}
          <path d={path} fill="none" stroke="var(--t-primary)" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
          {points.map((point, index) => (
            <g key={point.patch}>
              <circle
                cx={xFor(index)}
                cy={yFor(point.winRate)}
                r={point.isLowSample ? 3 : 4.5}
                fill="var(--t-bg)"
                stroke="var(--t-primary)"
                strokeWidth="2.5"
              >
                <title>{`Patch ${point.patch}: ${formatPercent(point.winRate, { input: "ratio", decimals: 2 })} across ${point.games.toLocaleString()} games${point.isLowSample ? " (low sample)" : ""}`}</title>
              </circle>
              {(index === 0 || index === points.length - 1 || points.length <= 6) ? (
                <text
                  x={xFor(index)}
                  y={HEIGHT - 12}
                  textAnchor={index === 0 ? "start" : index === points.length - 1 ? "end" : "middle"}
                  fill="currentColor"
                  className="text-[10px] text-muted"
                >
                  {point.patch}
                </text>
              ) : null}
            </g>
          ))}
        </svg>
        <figcaption className="sr-only">
          {points.map((point) => `Patch ${point.patch}: ${(point.winRate * 100).toFixed(2)} percent over ${point.games} games`).join(". ")}
        </figcaption>
      </figure>
    </Card>
  );
}
