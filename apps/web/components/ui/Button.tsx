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
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-xl font-medium transition duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:pointer-events-none disabled:opacity-60";

const variants: Record<Variant, string> = {
  primary:
    "bg-gradient-to-r from-primary to-primary-2 text-slate-950 shadow-glow hover:scale-[1.01] hover:brightness-110 active:scale-[0.99]",
  outline:
    "border border-border/80 bg-surface/45 text-fg shadow-glass hover:border-border-strong hover:bg-surface/70 active:bg-surface/45",
  ghost: "text-fg/90 hover:bg-white/10 active:bg-white/5"
};

const sizes: Record<Size, string> = {
  sm: "h-9 px-3 text-sm",
  md: "h-11 px-4 text-sm"
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

