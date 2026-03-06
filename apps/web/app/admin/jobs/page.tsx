import Link from "next/link";

import { AdminRefreshButton } from "@/app/admin/AdminRefreshButton";
import {
  AdminBulkDeleteButton,
  AdminJobsTable,
  AdminRecurringJobsTable
} from "@/app/admin/jobs/AdminJobsClient";
import { adminGet } from "@/lib/adminBackend";
import type {
  AdminJobListResponse,
  AdminQueueSummaryResponse,
  AdminRecurringJob
} from "@/lib/adminTypes";

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

const states = ["enqueued", "processing", "scheduled", "failed"] as const;
const bulkDeleteMaxLimit = 5000;

function toScalar(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function toInt(value: string | undefined, fallback: number, min: number, max: number) {
  if (!value) return fallback;
  const parsed = Number.parseInt(value, 10);
  if (Number.isNaN(parsed)) return fallback;
  return Math.min(max, Math.max(min, parsed));
}

export default async function AdminJobsPage(props: { searchParams: SearchParams }) {
  const searchParams = await props.searchParams;
  const state = (toScalar(searchParams.state) ?? "failed").toLowerCase();
  const from = toInt(toScalar(searchParams.from), 0, 0, 10000);
  const count = toInt(toScalar(searchParams.count), 25, 1, 200);
  const query = (toScalar(searchParams.q) ?? "").trim();
  const type = (toScalar(searchParams.type) ?? "").trim();
  const queue = (toScalar(searchParams.queue) ?? "").trim();
  const region = (toScalar(searchParams.region) ?? "").trim();
  const olderThanMinutes = (toScalar(searchParams.olderThanMinutes) ?? "").trim();
  const scanLimit = toInt(toScalar(searchParams.scanLimit), 5000, 500, 50000);

  const [recurring, queueSummary, jobs] = await Promise.all([
    adminGet<AdminRecurringJob[]>("/api/admin/jobs/recurring"),
    adminGet<AdminQueueSummaryResponse>(`/api/admin/jobs/queues?scanLimit=${scanLimit}`),
    adminGet<AdminJobListResponse>(
      `/api/admin/jobs/list?state=${encodeURIComponent(state)}&from=${from}&count=${count}&q=${encodeURIComponent(query)}&type=${encodeURIComponent(type)}&queue=${encodeURIComponent(queue)}&region=${encodeURIComponent(region)}&olderThanMinutes=${encodeURIComponent(olderThanMinutes)}&scanLimit=${scanLimit}`
    )
  ]);
  const bulkDeleteLimit = Math.min(Math.max(jobs.totalMatched, 1), bulkDeleteMaxLimit);

  return (
    <div className="grid gap-6">
      <section className="rounded-[1.75rem] border border-border/70 bg-surface/50 p-5">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold">Recurring Producers</h2>
            <p className="mt-1 text-sm text-fg/65">
              Pause producers before clearing a large backlog so the queue stops refilling.
            </p>
          </div>
          <AdminRefreshButton label="Refresh Jobs" />
        </div>
        <AdminRecurringJobsTable jobs={recurring} />
      </section>

      <section className="rounded-[1.75rem] border border-border/70 bg-surface/50 p-5">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold">Backlog Explorer</h2>
            <p className="mt-1 text-sm text-fg/65">
              {jobs.totalMatched.toLocaleString()} matching jobs
              {jobs.truncated ? ` · scan truncated at ${jobs.scanLimit.toLocaleString()}` : ""}
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            {states.map((entry) => (
              <Link
                key={entry}
                href={`/admin/jobs?state=${entry}&count=${count}&queue=${encodeURIComponent(queue)}&region=${encodeURIComponent(region)}&type=${encodeURIComponent(type)}&q=${encodeURIComponent(query)}&olderThanMinutes=${encodeURIComponent(olderThanMinutes)}&scanLimit=${scanLimit}`}
                className={`rounded-full border px-3 py-1 text-xs uppercase tracking-wide transition ${
                  state === entry
                    ? "border-primary/40 bg-primary/10 text-primary"
                    : "border-border/70 text-fg/70 hover:bg-white/10 hover:text-fg"
                }`}
              >
                {entry}
              </Link>
            ))}
          </div>
        </div>

        <form method="get" className="mt-4 flex flex-wrap items-end gap-2 text-sm">
          <input type="hidden" name="state" value={state} />
          <label className="grid gap-1">
            <span className="text-fg/60">Queue</span>
            <input
              type="text"
              name="queue"
              defaultValue={queue}
              className="h-10 w-32 rounded-xl border border-border/70 bg-surface/80 px-3 text-fg"
            />
          </label>
          <label className="grid gap-1">
            <span className="text-fg/60">Region</span>
            <input
              type="text"
              name="region"
              defaultValue={region}
              className="h-10 w-28 rounded-xl border border-border/70 bg-surface/80 px-3 text-fg"
            />
          </label>
          <label className="grid gap-1">
            <span className="text-fg/60">Job Type</span>
            <input
              type="text"
              name="type"
              defaultValue={type}
              className="h-10 w-44 rounded-xl border border-border/70 bg-surface/80 px-3 text-fg"
            />
          </label>
          <label className="grid gap-1">
            <span className="text-fg/60">Search</span>
            <input
              type="text"
              name="q"
              defaultValue={query}
              className="h-10 w-44 rounded-xl border border-border/70 bg-surface/80 px-3 text-fg"
            />
          </label>
          <label className="grid gap-1">
            <span className="text-fg/60">Older Than Min</span>
            <input
              type="number"
              name="olderThanMinutes"
              defaultValue={olderThanMinutes}
              className="h-10 w-28 rounded-xl border border-border/70 bg-surface/80 px-3 text-fg"
            />
          </label>
          <label className="grid gap-1">
            <span className="text-fg/60">Offset</span>
            <input
              type="number"
              name="from"
              defaultValue={from}
              className="h-10 w-24 rounded-xl border border-border/70 bg-surface/80 px-3 text-fg"
            />
          </label>
          <label className="grid gap-1">
            <span className="text-fg/60">Count</span>
            <input
              type="number"
              name="count"
              defaultValue={count}
              className="h-10 w-24 rounded-xl border border-border/70 bg-surface/80 px-3 text-fg"
            />
          </label>
          <label className="grid gap-1">
            <span className="text-fg/60">Scan Limit</span>
            <input
              type="number"
              name="scanLimit"
              defaultValue={scanLimit}
              className="h-10 w-28 rounded-xl border border-border/70 bg-surface/80 px-3 text-fg"
            />
          </label>
          <button
            type="submit"
            className="h-10 rounded-xl border border-primary/40 bg-primary/10 px-4 text-xs font-medium text-primary transition hover:bg-primary/20"
          >
            Apply
          </button>
        </form>

        {state !== "processing" ? (
          <div className="mt-4 rounded-2xl border border-amber-400/20 bg-amber-500/10 p-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p className="text-sm font-medium text-amber-100">Bulk clear current filtered backlog</p>
                <p className="mt-1 text-xs text-amber-100/75">
                  This targets the currently filtered {state} jobs only. Preview count: {jobs.totalMatched.toLocaleString()}.
                  {" "}Each run deletes up to {bulkDeleteLimit.toLocaleString()} jobs.
                </p>
              </div>
              <AdminBulkDeleteButton
                request={{
                  state,
                  queue,
                  region,
                  type,
                  q: query,
                  olderThanMinutes,
                  limit: bulkDeleteLimit
                }}
                totalMatched={jobs.totalMatched}
                disabled={jobs.totalMatched === 0}
              />
            </div>
          </div>
        ) : (
          <div className="mt-4 rounded-2xl border border-rose-400/20 bg-rose-500/10 p-4 text-sm text-rose-100/85">
            Processing jobs can only be deleted one-by-one from the table or detail page. That operation changes Hangfire state but may not stop already-started side effects immediately.
          </div>
        )}

        <div className="mt-4 grid gap-3 md:grid-cols-3">
          {queueSummary.topGroups.slice(0, 6).map((group) => (
            <div
              key={`${group.state}-${group.queue}-${group.jobType}-${group.region}`}
              className="rounded-2xl border border-border/60 bg-black/15 p-4"
            >
              <p className="text-sm font-medium text-fg">{group.jobType?.split(".").pop() ?? "Unknown Job"}</p>
              <p className="mt-1 text-xs text-fg/55">
                {group.state} · {group.queue}
                {group.region ? ` · ${group.region}` : ""}
              </p>
              <p className="mt-3 text-xl font-semibold text-primary">{group.count}</p>
            </div>
          ))}
        </div>

        <AdminJobsTable jobs={jobs.items} />
      </section>
    </div>
  );
}
