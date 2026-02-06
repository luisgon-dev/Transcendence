import { Card } from "@/components/ui/Card";
import { Skeleton } from "@/components/ui/Skeleton";

export default function Loading() {
  return (
    <div className="grid gap-6">
      <Card className="p-5">
        <Skeleton className="h-6 w-48" />
        <Skeleton className="mt-3 h-4 w-80" />
      </Card>
      <div className="grid gap-6 md:grid-cols-2">
        <Card className="p-5">
          <Skeleton className="h-5 w-40" />
          <Skeleton className="mt-4 h-24 w-full" />
        </Card>
        <Card className="p-5">
          <Skeleton className="h-5 w-40" />
          <Skeleton className="mt-4 h-24 w-full" />
        </Card>
      </div>
    </div>
  );
}

