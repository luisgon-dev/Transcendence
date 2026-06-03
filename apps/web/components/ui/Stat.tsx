import * as React from "react";

import { cn } from "@/lib/cn";

type StatTone = "neutral" | "win" | "loss" | "primary";

const toneClass: Record<StatTone, string> = {
  neutral: "text-fg",
  win: "text-win",
  loss: "text-loss",
  primary: "text-primary"
};

// A single metric: small uppercase label over a large tabular value, optional
// delta/suffix. Flat — no card chrome — so it composes into stat rows without
// nesting containers.
export function Stat({
  label,
  value,
  hint,
  tone = "neutral",
  className
}: {
  label: React.ReactNode;
  value: React.ReactNode;
  hint?: React.ReactNode;
  tone?: StatTone;
  className?: string;
}) {
  return (
    <div className={cn("grid gap-1", className)}>
      <span className="type-overline text-muted">{label}</span>
      <span className={cn("type-tabular text-2xl font-bold tabular-nums leading-none", toneClass[tone])}>
        {value}
      </span>
      {hint ? <span className="type-caption text-muted">{hint}</span> : null}
    </div>
  );
}
