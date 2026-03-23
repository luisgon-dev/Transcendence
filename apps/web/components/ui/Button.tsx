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
  "type-ui inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-xl font-semibold tracking-[0.01em] transition duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:pointer-events-none disabled:opacity-60";

const variants: Record<Variant, string> = {
  primary:
    "bg-primary text-bg hover:bg-primary/90 active:bg-primary/80",
  outline:
    "border border-border/60 bg-surface/38 text-fg hover:border-border-strong hover:bg-surface/52 active:bg-surface/38",
  ghost: "text-fg/90 hover:bg-surface-2/60 active:bg-surface/55"
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

