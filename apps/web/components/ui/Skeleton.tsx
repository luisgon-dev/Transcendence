import * as React from "react";

import { cn } from "@/lib/cn";

// Flat placeholder — a quiet opacity pulse, no shimmer sweep in data areas.
export function Skeleton({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        "animate-pulse rounded-md border border-border/60 bg-surface-2/70",
        className
      )}
      {...props}
    />
  );
}
