import * as React from "react";

import { cn } from "@/lib/cn";

export function Skeleton({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        "animate-shimmer rounded-md bg-gradient-to-r from-white/5 via-white/12 to-white/5 bg-[length:200%_100%]",
        className
      )}
      {...props}
    />
  );
}

