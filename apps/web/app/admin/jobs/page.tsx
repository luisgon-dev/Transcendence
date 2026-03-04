import Link from "next/link";

import { retryFailedJobAction, triggerRecurringJobAction } from "@/app/admin/actions";
import { adminGet } from "@/lib/adminBackend";
import type { AdminFailedJob, AdminRecurringJob } from "@/lib/adminTypes";

type SearchParams = Promise<Record<string, string | string[] | undefined>>;

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
  const from = toInt(toScalar(searchParams.from), 0, 0, 10000);
  const count = toInt(toScalar(searchParams.count), 20, 1, 100);
  const query = (toScalar(searchParams.q) ?? "").trim().toLowerCase();
  const type = (toScalar(searchParams.type) ?? "").trim().toLowerCase();

  const [recurring, failed] = await Promise.all([
    adminGet<AdminRecurringJob[]>("/api/admin/jobs/recurring"),
    adminGet<AdminFailedJob[]>(`/api/admin/jobs/failed?from=${from}&count=${count}`)
  ]);

  const filteredFailed = failed.filter((job) => {
    if (type && !(job.exceptionType ?? "").toLowerCase().includes(type)) return false;
    if (!query) return true;
    const haystack = [job.jobId, job.reason, job.exceptionType, job.exceptionMessage]
      .filter(Boolean)
      .join(" ")
      .toLowerCase();
    return haystack.includes(query);
  });

  return (
    <div className="grid gap-6">
      <section className="rounded-2xl border border-border/70 bg-surface/40 p-4">
        <h2 className="text-lg font-semibold">Recurring Jobs</h2>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-fg/65">
              <tr>
                <th className="py-2">Id</th>
                <th className="py-2">Queue</th>
                <th className="py-2">Next</th>
                <th className="py-2">Last State</th>
                <th className="py-2 text-right">Action</th>
              </tr>
            </thead>
            <tbody>
              {recurring.map((job) => (
                <tr key={job.id} className="border-t border-border/40">
                  <td className="py-2 font-mono text-xs">{job.id}</td>
                  <td className="py-2">{job.queue ?? "default"}</td>
                  <td className="py-2">{job.nextExecution ? new Date(job.nextExecution).toLocaleString() : "-"}</td>
                  <td className="py-2">{job.lastJobState ?? "-"}</td>
                  <td className="py-2 text-right">
                    <form action={triggerRecurringJobAction}>
                      <input type="hidden" name="id" value={job.id} />
                      <button
                        type="submit"
                        className="rounded-full border border-border/80 px-3 py-1 text-xs text-fg/85 transition hover:bg-white/10"
                      >
                        Trigger
                      </button>
                    </form>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="rounded-2xl border border-border/70 bg-surface/40 p-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-lg font-semibold">Failed Jobs</h2>
          <form method="get" className="flex flex-wrap items-center gap-2 text-sm">
            <input
              type="number"
              name="from"
              defaultValue={from}
              min={0}
              className="w-24 rounded-full border border-border/70 bg-transparent px-3 py-1"
              aria-label="Offset"
            />
            <input
              type="number"
              name="count"
              defaultValue={count}
              min={1}
              max={100}
              className="w-20 rounded-full border border-border/70 bg-transparent px-3 py-1"
              aria-label="Count"
            />
            <input
              type="text"
              name="q"
              defaultValue={query}
              placeholder="Search"
              className="w-44 rounded-full border border-border/70 bg-transparent px-3 py-1"
              aria-label="Search"
            />
            <input
              type="text"
              name="type"
              defaultValue={type}
              placeholder="Type filter"
              className="w-44 rounded-full border border-border/70 bg-transparent px-3 py-1"
              aria-label="Type filter"
            />
            <button
              type="submit"
              className="rounded-full border border-border/80 px-3 py-1 text-xs text-fg/85 transition hover:bg-white/10"
            >
              Apply
            </button>
          </form>
        </div>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-fg/65">
              <tr>
                <th className="py-2">Job Id</th>
                <th className="py-2">Type</th>
                <th className="py-2">Failed At</th>
                <th className="py-2">Reason</th>
                <th className="py-2">Detail</th>
                <th className="py-2 text-right">Action</th>
              </tr>
            </thead>
            <tbody>
              {filteredFailed.map((job) => (
                <tr key={job.jobId} className="border-t border-border/40">
                  <td className="py-2 font-mono text-xs">{job.jobId}</td>
                  <td className="py-2">{job.exceptionType ?? "-"}</td>
                  <td className="py-2">{job.failedAt ? new Date(job.failedAt).toLocaleString() : "-"}</td>
                  <td className="py-2">{job.reason ?? job.exceptionMessage ?? "-"}</td>
                  <td className="py-2">
                    <Link
                      href={`/admin/jobs/${encodeURIComponent(job.jobId)}`}
                      className="rounded-full border border-border/80 px-3 py-1 text-xs text-fg/85 transition hover:bg-white/10"
                    >
                      Inspect
                    </Link>
                  </td>
                  <td className="py-2 text-right">
                    <form action={retryFailedJobAction}>
                      <input type="hidden" name="jobId" value={job.jobId} />
                      <button
                        type="submit"
                        className="rounded-full border border-border/80 px-3 py-1 text-xs text-fg/85 transition hover:bg-white/10"
                      >
                        Retry
                      </button>
                    </form>
                  </td>
                </tr>
              ))}
              {filteredFailed.length === 0 ? (
                <tr>
                  <td className="py-3 text-fg/60" colSpan={6}>
                    No failed jobs matched the current filters.
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
