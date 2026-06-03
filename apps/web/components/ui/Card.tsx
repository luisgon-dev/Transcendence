import * as React from "react";

import { cn } from "@/lib/cn";

export function Card({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        "surface-card rounded-card transition-[border-color,background-color,box-shadow] duration-150 ease-[cubic-bezier(0.25,1,0.5,1)]",
        className
      )}
      {...props}
    />
  );
}

