"use client";

import { Badge } from "@/components/ui/Badge";
import { Tooltip } from "@/components/ui/Tooltip";
import { cn } from "@/lib/cn";
import {
  confidenceLevel,
  confidenceSampleLabel,
  type ConfidenceLevel
} from "@/lib/confidence";
import { formatGames } from "@/lib/format";

// Small, data-driven "how much should I trust this?" markers. Marked "use client"
// so the Radix Tooltip it renders hydrates inside this client boundary: rendering
// the Tooltip straight from a streamed server component mismatches on hydration
// and regenerates the subtree client-side. Confidence is DATA, never decoration —
// neutral tones, with a warning accent reserved for genuinely thin samples.

const FILLED_PIPS: Record<ConfidenceLevel, number> = {
  high: 3,
  moderate: 2,
  low: 1
};

// Staggered heights read as a signal-strength meter without adding chrome.
const PIP_HEIGHTS = ["h-2", "h-2.5", "h-3"] as const;

type ConfidenceInput = {
  games: number;
  isLowSample?: boolean;
  minGames?: number;
  className?: string;
};

export function Confidence({ games, isLowSample, minGames, className }: ConfidenceInput) {
  const level = confidenceLevel({ games, isLowSample, minGames });
  const filled = FILLED_PIPS[level];
  const sampleLabel = confidenceSampleLabel(level);
  const label = `${formatGames(games)} games · ${sampleLabel} sample`;
  const filledTone = level === "low" ? "bg-warning" : "bg-fg/70";

  // Not a tab stop: the marker restates the adjacent games count, so it stayed out of the
  // reading flow's tab order (WCAG 2.4.3 — it was adding dozens of low-value stops on dense
  // pages). Screen readers still announce it via role="img" + aria-label; the tooltip stays a
  // pointer affordance and the inline label below makes the level legible without hover.
  return (
    <Tooltip content={label}>
      <span
        role="img"
        aria-label={label}
        className={cn("inline-flex items-center gap-1.5 align-middle", className)}
      >
        <span aria-hidden="true" className="inline-flex items-end gap-[2px]">
          {PIP_HEIGHTS.map((height, i) => (
            <span
              key={i}
              className={cn(
                "w-[3px] rounded-[1px]",
                height,
                i < filled ? filledTone : "bg-border"
              )}
            />
          ))}
        </span>
        <span
          aria-hidden="true"
          className={cn(
            "hidden text-[11px] font-medium leading-none sm:inline",
            level === "low" ? "text-warning" : "text-muted"
          )}
        >
          {sampleLabel}
        </span>
      </span>
    </Tooltip>
  );
}

export function ConfidenceBadge({
  games,
  isLowSample,
  minGames,
  className
}: ConfidenceInput) {
  const level = confidenceLevel({ games, isLowSample, minGames });

  return (
    <Badge
      className={cn(
        level !== "high" && "border-warning/40 bg-warning/20 text-fg",
        className
      )}
    >
      {confidenceSampleLabel(level)}
    </Badge>
  );
}
