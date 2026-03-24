import * as React from "react";

import { cn } from "@/lib/cn";

export function Card({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        "surface-card rounded-2xl transition-[border-color,background-color,box-shadow] duration-200 ease-[cubic-bezier(0.25,1,0.5,1)]",
        className
      )}
      {...props}
    />
  );
}

