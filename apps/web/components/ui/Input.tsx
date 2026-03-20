"use client";

import * as React from "react";

import { cn } from "@/lib/cn";

export type InputProps = React.InputHTMLAttributes<HTMLInputElement>;

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ className, ...props }, ref) => (
    <input
      ref={ref}
      className={cn(
        "h-11 w-full rounded-xl border border-border/60 bg-surface/42 px-3 text-[0.9375rem] leading-none tracking-[0.01em] text-fg outline-none placeholder:text-muted/80 focus:border-primary/70 focus:ring-2 focus:ring-primary/20",
        className
      )}
      {...props}
    />
  )
);
Input.displayName = "Input";

