import { Skeleton } from "@/components/ui/Skeleton";

// Instant fallback while pro-build data loads (initial nav + lane/region/patch changes), mirroring
// the header with its filter row, the four-slot build strip, and the games/players columns.
export default function Loading() {
  return (
    <div className="grid gap-4">
      <header className="toolbar px-4 py-4 sm:px-5">
        <div className="flex items-center gap-4">
          <Skeleton className="size-14 rounded-card sm:size-16" />
          <div className="grid gap-2">
            <Skeleton className="h-3 w-20" />
            <Skeleton className="h-8 w-44" />
            <Skeleton className="h-3 w-64 max-w-full" />
          </div>
        </div>
        <div className="mt-4 flex flex-wrap gap-2 border-t border-border/70 pt-3">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-10 w-20 rounded-control" />
          ))}
        </div>
      </header>

      <div className="page-panel overflow-hidden">
        <div className="px-4 py-4 sm:px-5">
          <Skeleton className="h-7 w-48" />
        </div>
        <div className="grid gap-px border-t border-border bg-border sm:grid-cols-2 xl:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="grid gap-3 bg-surface p-4 sm:p-5">
              <Skeleton className="h-3 w-24" />
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-3 w-28" />
            </div>
          ))}
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="page-panel grid gap-3 p-4 sm:p-5">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-10 w-full" />
          ))}
        </div>
        <div className="page-panel grid content-start gap-3 p-4 sm:p-5">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-9 w-full" />
          ))}
        </div>
      </div>
    </div>
  );
}
