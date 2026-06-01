import { Card } from "@/components/ui/Card";
import { Skeleton } from "@/components/ui/Skeleton";

export default function Loading() {
  return (
    <div className="grid gap-8">
      <Card className="profile-hero-card rounded-hero p-5 md:p-8">
        <div className="flex flex-col gap-5 sm:flex-row sm:items-center">
          <Skeleton className="h-[88px] w-[88px] rounded-panel" />
          <div className="grid gap-3">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-7 w-56" />
            <Skeleton className="h-4 w-72" />
          </div>
        </div>
      </Card>

      <div className="grid gap-6 xl:grid-cols-[minmax(280px,0.32fr)_minmax(0,1fr)] xl:items-start">
        <Card className="profile-section-card p-5">
          <Skeleton className="h-4 w-32" />
          <Skeleton className="mt-4 h-24 w-full" />
        </Card>
        <Card className="profile-section-card rounded-panel p-5 md:p-6">
          <Skeleton className="h-5 w-48" />
          <div className="mt-5 grid gap-4">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} className="h-20 w-full rounded-xl" />
            ))}
          </div>
        </Card>
      </div>
    </div>
  );
}
