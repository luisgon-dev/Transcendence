import { Skeleton } from "@/components/ui/Skeleton";

// Root fallback for segments without their own loading.tsx — primarily the
// search-forward landing (hero + top-picks / explore panels), plus the LoL
// index and account/admin pages. Mirrors the landing's structure so streamed
// content lands in place instead of replacing a differently-shaped skeleton.
// Flat placeholders, no shimmer (per the "Ladder" aesthetic).
export default function Loading() {
  return (
    <div className="grid gap-8">
      <section className="page-hero p-6 sm:p-8 md:p-10">
        {/* Three lines of display type: bar heights track the clamp()-sized
            headline (h-10→14) and the lead runs two lines, so the real hero
            (~476px on desktop) streams in without shoving the panels down. */}
        <div className="grid max-w-3xl gap-3 sm:gap-3.5 md:gap-4">
          <Skeleton className="h-10 w-full rounded-lg sm:h-12 md:h-14" />
          <Skeleton className="h-10 w-11/12 rounded-lg sm:h-12 md:h-14" />
          <Skeleton className="h-10 w-2/5 rounded-lg sm:h-12 md:h-14" />
        </div>
        <div className="mt-5 grid max-w-2xl gap-2">
          <Skeleton className="h-4 w-full rounded-md" />
          <Skeleton className="h-4 w-1/3 rounded-md" />
        </div>
        <div className="mt-8 grid gap-3">
          <Skeleton className="h-14 w-full max-w-2xl rounded-control" />
          <Skeleton className="h-3.5 w-64 rounded-md" />
        </div>
      </section>

      <div className="grid gap-5 lg:grid-cols-[1.5fr_1fr] lg:items-start">
        <div className="page-panel grid gap-4 p-5 sm:p-6">
          <Skeleton className="h-3 w-44 rounded-md" />
          <div className="grid gap-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} className="h-11 w-full rounded-md" />
            ))}
          </div>
        </div>
        <div className="page-panel grid content-start gap-4 p-5 sm:p-6">
          <Skeleton className="h-3 w-24 rounded-md" />
          <div className="grid gap-3">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-16 w-full rounded-md" />
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
