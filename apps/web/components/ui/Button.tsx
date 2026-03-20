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
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-xl font-semibold tracking-[0.01em] transition duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:pointer-events-none disabled:opacity-60";

const variants: Record<Variant, string> = {
  primary:
    "bg-gradient-to-r from-primary to-primary-2 text-bg shadow-glow hover:brightness-105 active:brightness-95",
  outline:
    "border border-border/60 bg-surface/38 text-fg hover:border-border-strong hover:bg-surface/52 active:bg-surface/38",
  ghost: "text-fg/90 hover:bg-white/10 active:bg-white/5"
};

const sizes: Record<Size, string> = {
  sm: "h-9 px-3 text-[0.875rem] leading-none",
  md: "h-11 px-4 text-[0.9375rem] leading-none"
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

