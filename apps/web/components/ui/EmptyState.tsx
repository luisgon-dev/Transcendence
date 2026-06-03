import * as React from "react";

import { cn } from "@/lib/cn";

// A teaching empty state: title + explanation of what populates the view + an
// optional action. Used wherever a list/table has nothing to show, instead of a
// bare "no data" line.
export function EmptyState({
  title,
  description,
  action,
  icon,
  className
}: {
  title: React.ReactNode;
  description?: React.ReactNode;
  action?: React.ReactNode;
  icon?: React.ReactNode;
  className?: string;
}) {
  return (
    <div
      className={cn(
        "grid justify-items-center gap-3 rounded-card border border-dashed border-border px-6 py-12 text-center",
        className
      )}
    >
      {icon ? <div className="text-muted">{icon}</div> : null}
      <p className="type-section text-fg">{title}</p>
      {description ? (
        <p className="type-ui max-w-md text-muted">{description}</p>
      ) : null}
      {action ? <div className="mt-1">{action}</div> : null}
    </div>
  );
}
