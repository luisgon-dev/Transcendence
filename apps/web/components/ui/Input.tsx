"use client";

import * as React from "react";

import { cn } from "@/lib/cn";

export type InputProps = React.InputHTMLAttributes<HTMLInputElement>;

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ className, ...props }, ref) => (
    <input
      ref={ref}
      className={cn(
        "type-ui h-12 w-full rounded-control border border-border/60 bg-surface/72 px-4 text-fg shadow-inset outline-none transition-[border-color,background-color,box-shadow] duration-200 ease-[cubic-bezier(0.25,1,0.5,1)] placeholder:text-muted/76 hover:border-border-strong/78 hover:bg-surface/82 focus:border-primary/68 focus:bg-surface/88 focus:ring-2 focus:ring-primary/14 focus:ring-offset-2 focus:ring-offset-bg disabled:cursor-not-allowed disabled:opacity-60",
        className
      )}
      {...props}
    />
  )
);
Input.displayName = "Input";

