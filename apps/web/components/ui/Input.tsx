"use client";

import * as React from "react";

import { cn } from "@/lib/cn";

export type InputProps = React.InputHTMLAttributes<HTMLInputElement>;

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ className, ...props }, ref) => (
    <input
      ref={ref}
      className={cn(
        "type-ui h-12 w-full rounded-control border border-border bg-surface px-4 text-fg outline-none transition-[border-color,box-shadow] duration-150 ease-[cubic-bezier(0.25,1,0.5,1)] placeholder:text-muted hover:border-border-strong focus:border-primary/60 focus:ring-2 focus:ring-primary/30 focus:ring-offset-2 focus:ring-offset-bg disabled:cursor-not-allowed disabled:opacity-60",
        className
      )}
      {...props}
    />
  )
);
Input.displayName = "Input";

