import { Skeleton } from "@/components/ui/Skeleton";

// Instant fallback while Build Lab's counts load. It is also the route's Suspense boundary: the page
// reads uncached data, which cacheComponents requires to sit inside one (without it `next dev`
// reported "encountered uncached data during prerendering" on every visit). Mirrors the real layout
// -- header, two-per-row controls, then stage rows -- so the page does not jump when it streams in.
export default function Loading() {
  return (
    <div className="grid min-w-0 gap-5">
      <header className="flex items-start justify-between gap-4 border-b border-border/60 pb-5">
        <div className="flex items-center gap-3 sm:gap-4">
          <Skeleton className="size-14 rounded-card sm:size-16" />
          <div className="grid gap-2">
            <Skeleton className="h-3 w-28" />
            <Skeleton className="h-7 w-36" />
          </div>
        </div>
        <Skeleton className="h-4 w-24" />
      </header>

      <div className="grid grid-cols-2 gap-3 border-b border-border/50 pb-5 lg:grid-cols-5">
        {Array.from({ length: 5 }).map((_, index) => (
          <Skeleton
            key={index}
            className={index === 4 ? "col-span-2 h-11 lg:col-span-1 sm:h-9" : "h-11 sm:h-9"}
          />
        ))}
      </div>

      <div className="overflow-hidden rounded-card border border-border/60 bg-surface">
        <div className="border-b border-border/50 px-4 py-3">
          <Skeleton className="h-3 w-72 max-w-full" />
        </div>
        {Array.from({ length: 3 }).map((_, stage) => (
          <div key={stage} className="border-b border-border/50 last:border-0">
            <div className="px-4 pb-2.5 pt-4">
              <Skeleton className="h-5 w-32" />
            </div>
            {Array.from({ length: 3 }).map((_, row) => (
              <div key={row} className="flex items-center gap-3 border-t border-border/30 px-4 py-3">
                <Skeleton className="size-8 shrink-0 rounded-control" />
                <Skeleton className="h-4 flex-1" />
                <Skeleton className="hidden h-4 w-24 md:block" />
                <Skeleton className="h-8 w-14 rounded-control" />
              </div>
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}
