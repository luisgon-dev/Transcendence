import { cn } from "@/lib/cn";

export function BrandMark({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 64 64"
      aria-hidden="true"
      className={cn("h-8 w-8", className)}
    >
      <defs>
        <linearGradient id="trn-mark-gradient" x1="10" y1="8" x2="54" y2="56">
          <stop offset="0%" stopColor="#f0a030" />
          <stop offset="100%" stopColor="#e06545" />
        </linearGradient>
      </defs>

      <rect x="4" y="4" width="56" height="56" rx="16" fill="url(#trn-mark-gradient)" />
    </svg>
  );
}
