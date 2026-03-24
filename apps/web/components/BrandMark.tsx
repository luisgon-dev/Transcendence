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
          <stop offset="0%" stopColor="#e8a83a" />
          <stop offset="100%" stopColor="#d4624a" />
        </linearGradient>
      </defs>

      <rect x="4" y="4" width="56" height="56" rx="16" fill="url(#trn-mark-gradient)" />

      {/* Stylized T — geometric, Space Grotesk-inspired */}
      <path
        d="M16 16 h32 v8 h-12 v24 h-8 V24 H16 z"
        fill="white"
      />
    </svg>
  );
}
