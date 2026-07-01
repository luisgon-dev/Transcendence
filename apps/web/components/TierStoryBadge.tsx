"use client";

import { TierBadge } from "@/components/TierBadge";
import { Tooltip } from "@/components/ui/Tooltip";
import { cn } from "@/lib/cn";
import type { UITierGrade } from "@/lib/tierlist";

/**
 * Tier badge + empirical-Bayes strength tooltip, as a client island.
 *
 * The champion hero meta is a streamed async *server* component; rendering the
 * Radix `Tooltip` (a client component) directly inside it produced a hydration
 * mismatch every load (the subtree was discarded and regenerated client-side).
 * Isolating the Radix tree in its own client component — the same way the tier
 * list renders its EB tooltip from inside a "use client" table — keeps SSR and
 * hydration in lockstep.
 */
export function TierStoryBadge({
  tier,
  story,
  delta,
  lowSample
}: {
  tier: UITierGrade;
  /** Formatted EB story for the tooltip; omit to render the badge without one. */
  story?: string | null;
  /** Formatted strength delta vs the role average, e.g. "+3.6%". */
  delta?: string | null;
  lowSample?: boolean;
}) {
  const inner = (
    <span className={cn("flex items-center gap-2", story && "cursor-help")}>
      <TierBadge tier={tier} size="md" />
      {delta ? (
        <span className="type-tabular tabular-nums text-xs text-muted">
          vs role avg {delta}
          {lowSample ? " · low sample" : ""}
        </span>
      ) : null}
    </span>
  );

  return story ? <Tooltip content={story}>{inner}</Tooltip> : inner;
}
