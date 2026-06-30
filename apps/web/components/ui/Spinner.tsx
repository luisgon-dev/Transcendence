import { cn } from "@/lib/cn";

/**
 * Small accent spinner for inline pending feedback (filter changes, link
 * navigation). Flat and fast, motion-reduce safe — falls back to a static
 * ring rather than spinning when reduced motion is requested.
 */
export function Spinner({ className }: { className?: string }) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        "inline-block size-3 shrink-0 animate-spin rounded-full border-2 border-primary/25 border-t-primary motion-reduce:animate-none",
        className
      )}
    />
  );
}
