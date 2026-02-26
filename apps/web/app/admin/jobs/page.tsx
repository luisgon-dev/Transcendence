import { retryFailedJobAction, triggerRecurringJobAction } from "@/app/admin/actions";
import { adminGet } from "@/lib/adminBackend";
import type { AdminFailedJob, AdminRecurringJob } from "@/lib/adminTypes";

export default async function AdminJobsPage() {
  const [recurring, failed] = await Promise.all([
    adminGet<AdminRecurringJob[]>("/api/admin/jobs/recurring"),
    adminGet<AdminFailedJob[]>("/api/admin/jobs/failed?from=0&count=20")
  ]);

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
        <h2 className="text-lg font-semibold">Failed Jobs</h2>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-fg/65">
              <tr>
                <th className="py-2">Job Id</th>
                <th className="py-2">Type</th>
                <th className="py-2">Failed At</th>
                <th className="py-2">Reason</th>
                <th className="py-2 text-right">Action</th>
              </tr>
            </thead>
            <tbody>
              {failed.map((job) => (
                <tr key={job.jobId} className="border-t border-border/40">
                  <td className="py-2 font-mono text-xs">{job.jobId}</td>
                  <td className="py-2">{job.exceptionType ?? "-"}</td>
                  <td className="py-2">{job.failedAt ? new Date(job.failedAt).toLocaleString() : "-"}</td>
                  <td className="py-2">{job.reason ?? job.exceptionMessage ?? "-"}</td>
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
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}
