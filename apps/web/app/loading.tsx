import { Skeleton } from "@/components/ui/Skeleton";

// Root fallback for segments without their own loading.tsx — primarily the
// landing (meta hero + lane board + game-phase strip), plus the LoL index and
// account/admin pages. Mirrors the landing's structure so streamed content
// lands in place instead of replacing a differently-shaped skeleton.
// Flat placeholders, no shimmer (per the "Ladder" aesthetic).
export default function Loading() {
  return (
    <div className="grid gap-8 md:gap-10">
      <section className="landing-hero">
        {/* Splash band on narrow screens, right-hand art on desktop. */}
        <Skeleton className="landing-hero-art rounded-none border-0" />
        <div className="relative max-w-xl pt-32 sm:pt-40 lg:my-auto lg:pt-0">
          <Skeleton className="h-3 w-32 rounded-md" />
          {/* Two lines of display type, tracking the clamp()-sized headline. */}
          <div className="mt-4 grid gap-3 sm:gap-3.5 md:gap-4">
            <Skeleton className="h-10 w-full rounded-lg sm:h-12 md:h-14" />
            <Skeleton className="h-10 w-3/5 rounded-lg sm:h-12 md:h-14" />
          </div>
          <Skeleton className="mt-6 h-8 w-4/5 rounded-md" />
          <div className="mt-8 grid gap-3">
            <Skeleton className="h-14 w-full rounded-control" />
            <div className="min-h-7" />
          </div>
        </div>
        {/* Dataset line. */}
        <div className="relative mt-8 border-t border-border/60 pt-5">
          <Skeleton className="h-4 w-full max-w-3xl rounded-md" />
        </div>
      </section>

      <div className="page-panel overflow-hidden">
        <div className="grid gap-2 px-5 pb-4 pt-5 sm:px-6">
          <Skeleton className="h-7 w-52 rounded-md" />
          <Skeleton className="h-3.5 w-80 max-w-full rounded-md" />
        </div>
        <div className="grid gap-px border-t border-border bg-border sm:grid-cols-2 lg:grid-cols-5">
          {Array.from({ length: 5 }).map((_, i) => (
            <div
              key={i}
              className="grid content-start gap-3 bg-surface p-4 sm:p-5 sm:last:col-span-2 lg:last:col-span-1"
            >
              <Skeleton className="h-4 w-20 rounded-md" />
              <Skeleton className="h-14 w-full rounded-md" />
              <Skeleton className="h-10 w-full rounded-md" />
              <Skeleton className="h-10 w-full rounded-md" />
            </div>
          ))}
        </div>
      </div>

      <div className="grid gap-4">
        <Skeleton className="h-7 w-72 max-w-full rounded-md" />
        <div className="grid gap-px overflow-hidden rounded-panel border border-border bg-border md:grid-cols-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="grid gap-2 bg-surface p-5 sm:p-6">
              <Skeleton className="h-3.5 w-24 rounded-md" />
              <Skeleton className="h-7 w-48 rounded-md" />
              <Skeleton className="h-10 w-full rounded-md" />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
