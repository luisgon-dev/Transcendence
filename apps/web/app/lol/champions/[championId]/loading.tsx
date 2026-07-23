import { Card } from "@/components/ui/Card";
import { Skeleton } from "@/components/ui/Skeleton";

// Instant fallback while the champion page's analytics + static data load.
// Mirrors the real layout (hero → current-patch decisions → trend history) so navigation
// from search / matchups / the champion pool feels immediate rather than blank.
export default function Loading() {
  return (
    <div className="grid gap-8">
      <header className="page-hero relative p-5 md:p-8">
        <div className="flex flex-col gap-5">
          <div className="flex items-center gap-4">
            <Skeleton className="h-12 w-12 rounded-xl sm:h-16 sm:w-16" />
            <div className="grid gap-2">
              <Skeleton className="h-8 w-44" />
              <Skeleton className="h-4 w-56" />
            </div>
          </div>
          <Skeleton className="h-14 w-full rounded-lg" />
          <div className="flex flex-wrap gap-3">
            <Skeleton className="h-10 w-64 rounded-lg" />
            <Skeleton className="h-10 w-36 rounded-control" />
            <Skeleton className="h-10 w-40 rounded-lg" />
          </div>
        </div>
      </header>

      <Skeleton className="h-12 w-full rounded-card" />

      <div className="grid gap-6 md:grid-cols-2">
        <Card className="p-5">
          <Skeleton className="h-6 w-24" />
          <div className="mt-4 grid gap-4">
            <Skeleton className="h-28 w-full rounded-lg" />
            <Skeleton className="h-28 w-full rounded-lg" />
          </div>
        </Card>
        <Card className="p-5">
          <Skeleton className="h-6 w-28" />
          <div className="mt-4 grid gap-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} className="h-10 w-full rounded-md" />
            ))}
          </div>
        </Card>
      </div>

      <Card className="p-5">
        <Skeleton className="h-6 w-28" />
        <Skeleton className="mt-4 h-32 w-full rounded-lg" />
      </Card>
    </div>
  );
}
