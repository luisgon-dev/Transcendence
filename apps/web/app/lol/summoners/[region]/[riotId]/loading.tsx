import {
  MATCH_PLACEHOLDER_ROWS,
  RECENT_FORM_SLOTS
} from "@/components/lol-profile/shared";
import { Card } from "@/components/ui/Card";
import { Skeleton } from "@/components/ui/Skeleton";

// Mirrors the shipped profile shell (SummonerProfileUnified): a compact Toolbar
// hero over an xl sidebar + main split. Matching the real structure keeps the
// layout from jumping when the server fetch resolves. Flat placeholders, no
// shimmer, per the design system.
export default function Loading() {
  return (
    <div className="grid grid-cols-1 gap-8">
      {/* Hero — the Toolbar primitive: identity + actions, then a recent-form row */}
      <div className="toolbar flex flex-col gap-3 px-4 py-3.5 sm:px-5">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <Skeleton className="h-12 w-12 rounded-xl" />
            <div className="grid gap-2">
              <Skeleton className="h-6 w-44" />
              <Skeleton className="h-3.5 w-56" />
            </div>
          </div>
          <div className="flex gap-2">
            <Skeleton className="h-9 w-28 rounded-control" />
            <Skeleton className="h-9 w-28 rounded-control" />
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2 border-t border-border/70 pt-3">
          {Array.from({ length: RECENT_FORM_SLOTS }).map((_, i) => (
            <Skeleton key={i} className="h-2.5 w-7 rounded-full" />
          ))}
        </div>
      </div>

      {/* Sidebar + main, matching the xl:grid-cols split of the live page */}
      <div className="grid min-w-0 grid-cols-1 gap-6 xl:grid-cols-[20rem_minmax(0,1fr)] xl:items-start">
        <div className="grid min-w-0 content-start gap-5">
          <Card className="profile-section-card p-5">
            <Skeleton className="h-3 w-28" />
            <Skeleton className="mt-3 h-24 w-full" />
          </Card>
          <Card className="profile-section-card p-5">
            <Skeleton className="h-3 w-28" />
            <Skeleton className="mt-3 h-40 w-full" />
          </Card>
        </div>
        <div className="grid min-w-0 gap-6">
          <Card className="profile-section-card p-5">
            <Skeleton className="h-3 w-28" />
            <div className="mt-4 grid grid-cols-2 gap-4 sm:grid-cols-4">
              {Array.from({ length: 4 }).map((_, i) => (
                <Skeleton key={i} className="h-12 w-full" />
              ))}
            </div>
          </Card>
          <Card className="profile-section-card rounded-panel p-5 md:p-6">
            <Skeleton className="h-6 w-40" />
            <div className="mt-5 flex flex-col gap-4">
              {Array.from({ length: MATCH_PLACEHOLDER_ROWS }).map((_, i) => (
                <Skeleton key={i} className="h-28 w-full rounded-panel" />
              ))}
            </div>
          </Card>
        </div>
      </div>
    </div>
  );
}
