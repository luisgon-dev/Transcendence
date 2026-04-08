import * as React from "react";

import { cn } from "@/lib/cn";

export function Badge({
  className,
  ...props
}: React.HTMLAttributes<HTMLSpanElement>) {
  return (
    <span
      className={cn(
        "type-caption inline-flex items-center rounded-full border border-border/45 bg-surface/62 px-2.5 py-1 font-semibold text-fg/76 shadow-inset",
        className
      )}
      {...props}
    />
  );
}
