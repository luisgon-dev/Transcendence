import { cn } from "@/lib/cn";

import type { MatchPerformance } from "@/components/lol-profile/shared";

const LABEL_CLASS: Record<string, string> = {
  MVP: "border-tier-s/40 bg-tier-s/12 text-tier-s",
  ACE: "border-info/40 bg-info/10 text-info"
};

function percentage(value: number): string {
  return `${Math.round(value * 100)}%`;
}

export function performanceExplanation(performance: MatchPerformance): string {
  const rank = `Team rank ${performance.teamRank} of ${performance.teamSize}`;
  const inputs = [
    `${percentage(performance.killParticipation)} kill participation`,
    `${percentage(performance.damageShare)} damage share`,
    `${percentage(performance.visionShare)} vision share`,
    `${performance.csPerMin.toFixed(1)} CS/min`
  ].join(", ");
  return `${rank}. ${performance.score.toFixed(1)} impact score from team-relative combat, economy, vision, farm, and survival. ${inputs}.`;
}

export function PerformanceIndicator({
  performance,
  compact = false,
  className
}: {
  performance?: MatchPerformance | null;
  compact?: boolean;
  className?: string;
}) {
  if (!performance || !Number.isFinite(performance.score) || performance.teamSize < 2) return null;

  const label = performance.label ?? `#${performance.teamRank} team`;
  const labelClass = performance.label
    ? LABEL_CLASS[performance.label] ?? "border-border-strong bg-surface-2 text-fg"
    : "border-border/80 bg-surface-2/70 text-fg/78";

  return (
    <span
      className={cn(
        "inline-flex w-fit items-center rounded-control border font-semibold tabular-nums",
        compact ? "gap-1 px-1.5 py-0.5 type-caption" : "gap-1.5 px-2 py-1 text-xs",
        labelClass,
        className
      )}
      title={performanceExplanation(performance)}
      aria-label={`${label}, ${performance.score.toFixed(1)} impact score. ${performanceExplanation(performance)}`}
      data-testid="performance-indicator"
    >
      <span>{label}</span>
      <span className="opacity-75">{performance.score.toFixed(1)}</span>
    </span>
  );
}
