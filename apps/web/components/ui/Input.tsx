"use client";

import * as React from "react";

import { cn } from "@/lib/cn";

export type InputProps = React.InputHTMLAttributes<HTMLInputElement>;

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ className, ...props }, ref) => (
    <input
      ref={ref}
      className={cn(
        "h-11 w-full rounded-md border border-border/70 bg-surface/35 px-3 text-sm text-fg shadow-glass outline-none placeholder:text-muted/70 focus:border-primary/70 focus:ring-2 focus:ring-primary/25",
        className
      )}
      {...props}
    />
  )
);
Input.displayName = "Input";

