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
  const label = `${formatGames(games)} games · ${confidenceSampleLabel(level)} sample`;
  const filledTone = level === "low" ? "bg-warning" : "bg-fg/70";

  return (
    <Tooltip content={label}>
      <span
        role="img"
        aria-label={label}
        tabIndex={0}
        className={cn(
          "inline-flex items-end gap-[2px] rounded-[2px] align-middle outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
          className
        )}
      >
        {PIP_HEIGHTS.map((height, i) => (
          <span
            key={i}
            aria-hidden="true"
            className={cn(
              "w-[3px] rounded-[1px]",
              height,
              i < filled ? filledTone : "bg-border"
            )}
          />
        ))}
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
