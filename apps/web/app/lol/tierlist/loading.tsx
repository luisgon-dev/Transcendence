import { Card } from "@/components/ui/Card";
import { Skeleton } from "@/components/ui/Skeleton";

// Instant fallback while the tier list loads (initial nav + rank/region/patch
// changes), mirroring the toolbar + ranked table.
export default function Loading() {
  return (
    <div className="grid gap-4">
      <Card className="p-5">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div className="grid gap-2">
            <Skeleton className="h-4 w-28" />
            <Skeleton className="h-8 w-40" />
          </div>
          <div className="flex flex-wrap gap-3">
            <Skeleton className="h-10 w-44 rounded-control" />
            <Skeleton className="h-10 w-56 rounded-lg" />
          </div>
        </div>
        <div className="mt-4 flex flex-wrap gap-2 border-t border-border/40 pt-4">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-9 w-24 rounded-control" />
          ))}
        </div>
      </Card>

      <Card className="p-5">
        <div className="grid gap-2">
          {Array.from({ length: 12 }).map((_, i) => (
            <Skeleton key={i} className="h-12 w-full rounded-md" />
          ))}
        </div>
      </Card>
    </div>
  );
}
