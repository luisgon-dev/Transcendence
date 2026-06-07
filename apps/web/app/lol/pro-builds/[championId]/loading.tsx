import { Card } from "@/components/ui/Card";
import { Skeleton } from "@/components/ui/Skeleton";

// Instant fallback while pro-build data loads (initial nav + lane/region/patch
// changes), mirroring the header, filters card, and build list.
export default function Loading() {
  return (
    <div className="grid gap-8">
      <header className="page-hero relative p-5 md:p-8">
        <div className="flex items-center gap-4">
          <Skeleton className="h-12 w-12 rounded-xl sm:h-16 sm:w-16" />
          <div className="grid gap-2">
            <Skeleton className="h-8 w-48" />
            <Skeleton className="h-4 w-40" />
          </div>
        </div>
      </header>

      <Card className="p-5">
        <Skeleton className="h-6 w-20" />
        <div className="mt-3 grid gap-3">
          <div className="flex flex-wrap gap-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} className="h-9 w-24 rounded-control" />
            ))}
          </div>
          <Skeleton className="h-10 w-72 rounded-lg" />
        </div>
      </Card>

      <div className="grid gap-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-32 w-full rounded-lg" />
        ))}
      </div>
    </div>
  );
}
