import Link from "next/link";

import {
  bulkDeleteJobsAction,
  deleteJobAction,
  pauseRecurringJobAction,
  resumeRecurringJobAction,
  retryFailedJobAction,
  triggerRecurringJobAction
} from "@/app/admin/actions";
import { adminGet } from "@/lib/adminBackend";
import type {
  AdminJobListResponse,
  AdminQueueSummaryResponse,
  AdminRecurringJob
} from "@/lib/adminTypes";

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

const states = ["enqueued", "processing", "scheduled", "failed"] as const;

function toScalar(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function toInt(value: string | undefined, fallback: number, min: number, max: number) {
  if (!value) return fallback;
  const parsed = Number.parseInt(value, 10);
  if (Number.isNaN(parsed)) return fallback;
  return Math.min(max, Math.max(min, parsed));
}

function buildLogsHref(job: {
  exceptionType: string | null;
  stateChangedAtUtc: string | null;
  region: string | null;
}) {
  const params = new URLSearchParams({
    service: "service",
    limit: "200"
  });
  if (job.exceptionType) params.set("q", job.exceptionType);
  if (job.region) params.set("q", `${job.exceptionType ?? ""} ${job.region}`.trim());
  if (job.stateChangedAtUtc) {
    const at = new Date(job.stateChangedAtUtc);
    const since = new Date(at.getTime() - 15 * 60 * 1000);
    const until = new Date(at.getTime() + 15 * 60 * 1000);
    params.set("sinceUtc", since.toISOString().slice(0, 16));
    params.set("untilUtc", until.toISOString().slice(0, 16));
  }
  return `/admin/logs?${params.toString()}`;
}

function recurringAction(job: AdminRecurringJob) {
  if (!job.isPausable) return null;
  if (job.isPaused) {
    return (
      <form action={resumeRecurringJobAction}>
        <input type="hidden" name="id" value={job.id} />
        <button
          type="submit"
          className="rounded-full border border-emerald-400/30 bg-emerald-500/10 px-3 py-1 text-xs text-emerald-200 transition hover:bg-emerald-500/20"
        >
          Resume
        </button>
      </form>
    );
  }

  return (
    <form action={pauseRecurringJobAction}>
      <input type="hidden" name="id" value={job.id} />
      <button
        type="submit"
        className="rounded-full border border-amber-400/30 bg-amber-500/10 px-3 py-1 text-xs text-amber-200 transition hover:bg-amber-500/20"
      >
        Pause
      </button>
    </form>
  );
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

  const [recurring, queueSummary, jobs] = await Promise.all([
    adminGet<AdminRecurringJob[]>("/api/admin/jobs/recurring"),
    adminGet<AdminQueueSummaryResponse>("/api/admin/jobs/queues?scanLimit=12000"),
    adminGet<AdminJobListResponse>(
      `/api/admin/jobs/list?state=${encodeURIComponent(state)}&from=${from}&count=${count}&q=${encodeURIComponent(query)}&type=${encodeURIComponent(type)}&queue=${encodeURIComponent(queue)}&region=${encodeURIComponent(region)}&olderThanMinutes=${encodeURIComponent(olderThanMinutes)}&scanLimit=20000`
    )
  ]);

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
        </div>
        <div className="mt-4 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-fg/60">
              <tr>
                <th className="py-2">Id</th>
                <th className="py-2">Queue</th>
                <th className="py-2">Status</th>
                <th className="py-2">Next</th>
                <th className="py-2 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {recurring.map((job) => (
                <tr key={job.id} className="border-t border-border/40">
                  <td className="py-2 font-mono text-xs">{job.id}</td>
                  <td className="py-2">{job.queue || "default"}</td>
                  <td className="py-2">
                    {job.isPaused ? (
                      <span className="rounded-full bg-amber-500/15 px-2.5 py-1 text-xs uppercase tracking-wide text-amber-200">
                        paused
                      </span>
                    ) : job.isEnabledByConfiguration ? (
                      <span className="rounded-full bg-emerald-500/15 px-2.5 py-1 text-xs uppercase tracking-wide text-emerald-200">
                        active
                      </span>
                    ) : (
                      <span className="rounded-full bg-white/10 px-2.5 py-1 text-xs uppercase tracking-wide text-fg/60">
                        disabled
                      </span>
                    )}
                  </td>
                  <td className="py-2">{job.nextExecution ? new Date(job.nextExecution).toLocaleString() : "-"}</td>
                  <td className="py-2">
                    <div className="flex justify-end gap-2">
                      <form action={triggerRecurringJobAction}>
                        <input type="hidden" name="id" value={job.id} />
                        <button
                          type="submit"
                          className="rounded-full border border-border/80 px-3 py-1 text-xs text-fg/85 transition hover:bg-white/10"
                        >
                          Trigger
                        </button>
                      </form>
                      {recurringAction(job)}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
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
                href={`/admin/jobs?state=${entry}&count=${count}&queue=${encodeURIComponent(queue)}&region=${encodeURIComponent(region)}&type=${encodeURIComponent(type)}&q=${encodeURIComponent(query)}&olderThanMinutes=${encodeURIComponent(olderThanMinutes)}`}
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
                </p>
              </div>
              <form action={bulkDeleteJobsAction} className="flex flex-wrap items-center gap-2">
                <input type="hidden" name="state" value={state} />
                <input type="hidden" name="queue" value={queue} />
                <input type="hidden" name="region" value={region} />
                <input type="hidden" name="type" value={type} />
                <input type="hidden" name="q" value={query} />
                <input type="hidden" name="olderThanMinutes" value={olderThanMinutes} />
                <input type="hidden" name="limit" value={Math.max(jobs.totalMatched, 1)} />
                <button
                  type="submit"
                  className="rounded-full border border-amber-300/30 bg-amber-500/10 px-4 py-2 text-xs font-medium text-amber-100 transition hover:bg-amber-500/20"
                >
                  Clear Filtered {state}
                </button>
              </form>
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

        <div className="mt-4 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-fg/60">
              <tr>
                <th className="py-2">Job</th>
                <th className="py-2">Type</th>
                <th className="py-2">Queue</th>
                <th className="py-2">Region</th>
                <th className="py-2">When</th>
                <th className="py-2">Detail</th>
                <th className="py-2">Logs</th>
                <th className="py-2 text-right">Action</th>
              </tr>
            </thead>
            <tbody>
              {jobs.items.map((job) => (
                <tr key={job.jobId} className="border-t border-border/40">
                  <td className="py-2 font-mono text-xs">{job.jobId}</td>
                  <td className="py-2">{job.jobType?.split(".").pop() ?? "-"}</td>
                  <td className="py-2">{job.queue}</td>
                  <td className="py-2">{job.region ?? "-"}</td>
                  <td className="py-2">
                    {job.stateChangedAtUtc ? new Date(job.stateChangedAtUtc).toLocaleString() : "-"}
                  </td>
                  <td className="py-2">
                    <Link
                      href={`/admin/jobs/${encodeURIComponent(job.jobId)}`}
                      className="rounded-full border border-border/80 px-3 py-1 text-xs text-fg/85 transition hover:bg-white/10"
                    >
                      Inspect
                    </Link>
                  </td>
                  <td className="py-2">
                    <Link
                      href={buildLogsHref(job)}
                      className="rounded-full border border-border/80 px-3 py-1 text-xs text-fg/85 transition hover:bg-white/10"
                    >
                      Nearby Logs
                    </Link>
                  </td>
                  <td className="py-2">
                    <div className="flex justify-end gap-2">
                      {job.state === "Failed" ? (
                        <form action={retryFailedJobAction}>
                          <input type="hidden" name="jobId" value={job.jobId} />
                          <button
                            type="submit"
                            className="rounded-full border border-border/80 px-3 py-1 text-xs text-fg/85 transition hover:bg-white/10"
                          >
                            Retry
                          </button>
                        </form>
                      ) : null}
                      <form action={deleteJobAction}>
                        <input type="hidden" name="jobId" value={job.jobId} />
                        <input type="hidden" name="expectedState" value={job.state} />
                        <input type="hidden" name="reason" value="Deleted from admin jobs table" />
                        <button
                          type="submit"
                          className="rounded-full border border-rose-400/30 bg-rose-500/10 px-3 py-1 text-xs text-rose-100 transition hover:bg-rose-500/20"
                        >
                          Delete
                        </button>
                      </form>
                    </div>
                  </td>
                </tr>
              ))}
              {jobs.items.length === 0 ? (
                <tr>
                  <td className="py-3 text-fg/60" colSpan={8}>
                    No jobs matched the current filters.
                  </td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}
