import { cn } from "@/lib/cn";

export type ButtonVariant = "primary" | "outline" | "ghost";
export type ButtonSize = "sm" | "md";

const base =
  "type-ui inline-flex min-h-11 items-center justify-center gap-2 whitespace-nowrap rounded-control font-semibold touch-manipulation transition-[color,background-color,border-color,box-shadow,transform] duration-150 ease-[cubic-bezier(0.25,1,0.5,1)] motion-reduce:transition-none focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 focus-visible:ring-offset-2 focus-visible:ring-offset-bg disabled:pointer-events-none disabled:translate-y-0 disabled:opacity-55 active:translate-y-px";

const variants: Record<ButtonVariant, string> = {
  primary:
    "bg-primary text-primary-fg shadow-soft hover:bg-primary/92 active:bg-primary/85",
  outline:
    "border border-border bg-surface text-fg/92 hover:border-border-strong hover:bg-surface-2/60 hover:text-fg active:bg-surface-2",
  ghost:
    "text-fg/84 hover:bg-surface-2/60 hover:text-fg active:bg-surface-2/80"
};

const sizes: Record<ButtonSize, string> = {
  sm: "h-9 px-3 text-sm leading-none",
  md: "h-11 px-4 leading-none"
};

/**
 * The button's full class string, usable on a non-button element (e.g. a
 * Next.js `<Link>` used as a primary action). Lives outside the `"use client"`
 * `Button` module so Server Components can call it: keeps link-as-button CTAs on
 * the same contract as `<Button>` (focus ring, active nudge, shadow) instead of
 * a degraded copy-paste.
 */
export function buttonClassName({
  variant = "primary",
  size = "md",
  className
}: {
  variant?: ButtonVariant;
  size?: ButtonSize;
  className?: string;
} = {}): string {
  return cn(base, variants[variant], sizes[size], className);
}
