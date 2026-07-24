import { Card } from "@/components/ui/Card";

export default function LeaderboardsLoading() {
  return (
    <div className="grid gap-4" aria-label="Loading leaderboard">
      <div className="h-28 animate-pulse rounded-2xl border border-border/60 bg-surface" />
      <Card className="overflow-hidden p-0">
        <div className="grid divide-y divide-border/30">
          {Array.from({ length: 8 }, (_, index) => (
            <div key={index} className="flex h-[61px] animate-pulse items-center gap-4 px-4">
              <span className="h-3 w-8 rounded bg-surface-2" />
              <span className="size-9 rounded-lg bg-surface-2" />
              <span className="h-3 w-36 rounded bg-surface-2" />
              <span className="ml-auto h-3 w-24 rounded bg-surface-2" />
            </div>
          ))}
        </div>
      </Card>
    </div>
  );
}
