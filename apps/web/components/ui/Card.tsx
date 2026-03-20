import * as React from "react";

import { cn } from "@/lib/cn";

export function Card({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        "surface-card rounded-2xl",
        className
      )}
      {...props}
    />
  );
}

