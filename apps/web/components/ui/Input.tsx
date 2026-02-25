"use client";

import * as React from "react";

import { cn } from "@/lib/cn";

export type InputProps = React.InputHTMLAttributes<HTMLInputElement>;

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ className, ...props }, ref) => (
    <input
      ref={ref}
      className={cn(
        "h-11 w-full rounded-xl border border-border/80 bg-surface/50 px-3 text-sm text-fg shadow-glass outline-none placeholder:text-muted/80 focus:border-primary/70 focus:ring-2 focus:ring-primary/25",
        className
      )}
      {...props}
    />
  )
);
Input.displayName = "Input";

