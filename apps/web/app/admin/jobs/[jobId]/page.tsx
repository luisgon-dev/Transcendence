import Link from "next/link";

import { retryFailedJobAction } from "@/app/admin/actions";
import { adminGet } from "@/lib/adminBackend";
import type { AdminFailedJobDetail } from "@/lib/adminTypes";

export default async function AdminFailedJobDetailPage({
  params
}: {
  params: Promise<{ jobId: string }>;
}) {
  const { jobId } = await params;
  const detail = await adminGet<AdminFailedJobDetail>(`/api/admin/jobs/failed/${encodeURIComponent(jobId)}`);

  return (
    <section className="grid gap-6">
      <div className="rounded-2xl border border-border/70 bg-surface/40 p-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold">Failed Job Detail</h2>
            <p className="mt-1 font-mono text-xs text-fg/70">{detail.jobId}</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Link
              href="/admin/jobs"
              className="rounded-full border border-border/80 px-3 py-1 text-xs text-fg/85 transition hover:bg-white/10"
            >
              Back to Jobs
            </Link>
            <form action={retryFailedJobAction}>
              <input type="hidden" name="jobId" value={detail.jobId} />
              <button
                type="submit"
                className="rounded-full border border-border/80 px-3 py-1 text-xs text-fg/85 transition hover:bg-white/10"
              >
                Retry
              </button>
            </form>
          </div>
        </div>
        <dl className="mt-4 grid gap-2 text-sm md:grid-cols-2">
          <div>
            <dt className="text-fg/65">Type</dt>
            <dd>{detail.jobType ?? "-"}</dd>
          </div>
          <div>
            <dt className="text-fg/65">Method</dt>
            <dd>{detail.jobMethod ?? "-"}</dd>
          </div>
          <div>
            <dt className="text-fg/65">Current State</dt>
            <dd>{detail.currentState ?? "-"}</dd>
          </div>
          <div>
            <dt className="text-fg/65">Failed Attempts</dt>
            <dd>{detail.failedCount}</dd>
          </div>
          <div>
            <dt className="text-fg/65">Failed At</dt>
            <dd>{detail.failedAtUtc ? new Date(detail.failedAtUtc).toLocaleString() : "-"}</dd>
          </div>
          <div>
            <dt className="text-fg/65">Reason</dt>
            <dd>{detail.reason ?? "-"}</dd>
          </div>
        </dl>
      </div>

      <div className="rounded-2xl border border-border/70 bg-surface/40 p-4">
        <h3 className="text-base font-semibold">Exception</h3>
        <p className="mt-2 text-sm text-fg/75">{detail.exceptionType ?? "-"}</p>
        <p className="mt-1 text-sm">{detail.exceptionMessage ?? "-"}</p>
        <pre className="mt-3 overflow-x-auto whitespace-pre-wrap rounded-xl border border-border/60 bg-black/20 p-3 text-xs text-fg/85">
          {detail.exceptionDetails ?? "No exception details captured."}
        </pre>
      </div>

      <div className="rounded-2xl border border-border/70 bg-surface/40 p-4">
        <h3 className="text-base font-semibold">State Timeline</h3>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-fg/65">
              <tr>
                <th className="py-2">When</th>
                <th className="py-2">State</th>
                <th className="py-2">Reason</th>
              </tr>
            </thead>
            <tbody>
              {detail.states.map((state) => (
                <tr key={`${state.stateName}-${state.createdAtUtc}`} className="border-t border-border/40">
                  <td className="py-2">{new Date(state.createdAtUtc).toLocaleString()}</td>
                  <td className="py-2">{state.stateName}</td>
                  <td className="py-2">{state.reason ?? "-"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="rounded-2xl border border-border/70 bg-surface/40 p-4">
        <h3 className="text-base font-semibold">Invocation Arguments</h3>
        <pre className="mt-3 overflow-x-auto whitespace-pre-wrap rounded-xl border border-border/60 bg-black/20 p-3 text-xs text-fg/85">
          {detail.arguments.length > 0 ? detail.arguments.join("\n") : "No arguments."}
        </pre>
      </div>
    </section>
  );
}
