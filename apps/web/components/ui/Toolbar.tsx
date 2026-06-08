import * as React from "react";

import { cn } from "@/lib/cn";

// The compact page header that replaces the old tall hero. Title (+ optional
// eyebrow and inline meta) on the left, actions on the right, with an optional
// second row for filters. Answer-first: this stays short so data leads.
export function Toolbar({
  eyebrow,
  title,
  meta,
  actions,
  filters,
  className
}: {
  eyebrow?: React.ReactNode;
  title: React.ReactNode;
  meta?: React.ReactNode;
  actions?: React.ReactNode;
  filters?: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={cn("toolbar flex flex-col gap-3 px-4 py-3.5 sm:px-5", className)}>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        {/* basis-full on small screens forces the title onto its own line so the
            actions wrap below instead of overflowing; flex-1 from sm: up. */}
        <div className="min-w-0 grow basis-full sm:basis-0">
          {eyebrow ? <p className="type-overline text-muted">{eyebrow}</p> : null}
          <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
            <h1 className="type-page-title text-fg">{title}</h1>
            {meta ? (
              <div className="flex flex-wrap items-center gap-x-3 gap-y-1 type-caption text-muted">
                {meta}
              </div>
            ) : null}
          </div>
        </div>
        {actions ? <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div> : null}
      </div>
      {filters ? (
        <div className="flex flex-wrap items-center gap-2 border-t border-border/70 pt-3">
          {filters}
        </div>
      ) : null}
    </div>
  );
}
