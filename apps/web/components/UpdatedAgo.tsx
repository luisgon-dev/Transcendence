"use client";

import * as React from "react";

import { cn } from "@/lib/cn";

// Subtle "Updated 23 min ago" freshness label for precomputed analytics.
// Client component so it can compute relative time from the viewer's clock.
// Renders nothing when the timestamp is missing — informational metadata only,
// so it stays grayscale/muted (no accent, no badge, no border).

function formatRelative(fromMs: number, nowMs: number): string {
  const diffMs = Math.max(0, nowMs - fromMs);
  const minutes = Math.floor(diffMs / 60_000);

  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes} min ago`;

  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} hr ago`;

  const days = Math.floor(hours / 24);
  return `${days} ${days === 1 ? "day" : "days"} ago`;
}

export function UpdatedAgo({
  timestamp,
  className
}: {
  timestamp?: string | null;
  className?: string;
}) {
  const fromMs = React.useMemo(() => {
    if (!timestamp) return null;
    const parsed = Date.parse(timestamp);
    return Number.isFinite(parsed) ? parsed : null;
  }, [timestamp]);

  // Recompute on mount (and tick once a minute) so the label stays fresh without
  // a server round-trip. Starts null so SSR and first client paint agree.
  const [nowMs, setNowMs] = React.useState<number | null>(null);
  React.useEffect(() => {
    if (fromMs == null) return;
    setNowMs(Date.now());
    const id = setInterval(() => setNowMs(Date.now()), 60_000);
    return () => clearInterval(id);
  }, [fromMs]);

  if (fromMs == null || nowMs == null) return null;

  return (
    <span className={cn("text-muted", className)}>Updated {formatRelative(fromMs, nowMs)}</span>
  );
}
