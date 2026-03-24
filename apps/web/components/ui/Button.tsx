"use client";

import * as React from "react";

import { cn } from "@/lib/cn";

type Variant = "primary" | "outline" | "ghost";
type Size = "sm" | "md";

export type ButtonProps = React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: Variant;
  size?: Size;
};

const base =
  "type-ui inline-flex min-h-11 items-center justify-center gap-2 whitespace-nowrap rounded-xl font-semibold tracking-[0.01em] touch-manipulation transition-[color,background-color,border-color,box-shadow,transform] duration-200 ease-[cubic-bezier(0.25,1,0.5,1)] motion-reduce:transition-none focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/30 focus-visible:ring-offset-2 focus-visible:ring-offset-bg disabled:pointer-events-none disabled:translate-y-0 disabled:opacity-55 active:translate-y-px";

const variants: Record<Variant, string> = {
  primary:
    "bg-primary text-bg shadow-soft hover:-translate-y-px hover:bg-primary/94 hover:shadow-card active:bg-primary/88",
  outline:
    "border border-border/60 bg-surface/58 text-fg/92 shadow-inset hover:-translate-y-px hover:border-border-strong hover:bg-surface/78 hover:text-fg active:bg-surface/66",
  ghost:
    "text-fg/84 hover:-translate-y-px hover:bg-surface-2/62 hover:text-fg active:bg-surface/62"
};

const sizes: Record<Size, string> = {
  sm: "h-9 px-3 text-sm leading-none",
  md: "h-11 px-4 leading-none"
};

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = "primary", size = "md", ...props }, ref) => (
    <button
      ref={ref}
      className={cn(base, variants[variant], sizes[size], className)}
      {...props}
    />
  )
);
Button.displayName = "Button";

