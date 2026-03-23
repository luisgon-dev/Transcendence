import Link from "next/link";

import { AdminRefreshButton } from "@/app/admin/AdminRefreshButton";
import { AdminJobDetailActionBar } from "@/app/admin/jobs/AdminJobsClient";
import { adminGet } from "@/lib/adminBackend";
import type { AdminJobDetail } from "@/lib/adminTypes";

function buildLogsHref(detail: AdminJobDetail) {
  const params = new URLSearchParams({
    service: "service",
    limit: "200"
  });

  if (detail.exceptionType) params.set("q", detail.exceptionType);
  if (detail.region) params.set("q", `${detail.exceptionType ?? ""} ${detail.region}`.trim());
  const anchor = detail.failedAtUtc ?? detail.startedAtUtc ?? detail.stateChangedAtUtc;
  if (anchor) {
    const at = new Date(anchor);
    const since = new Date(at.getTime() - 15 * 60 * 1000);
    const until = new Date(at.getTime() + 15 * 60 * 1000);
    params.set("sinceUtc", since.toISOString().slice(0, 16));
    params.set("untilUtc", until.toISOString().slice(0, 16));
  }

  return `/admin/logs?${params.toString()}`;
}

export default async function AdminJobDetailPage({
  params
}: {
  params: Promise<{ jobId: string }>;
}) {
  const { jobId } = await params;
  const detail = await adminGet<AdminJobDetail>(`/api/admin/jobs/inspect/${encodeURIComponent(jobId)}`);

  return (
    <section className="grid gap-6">
      <div className="page-panel p-5">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold">Job Detail</h2>
            <p className="mt-1 font-mono text-xs text-fg/70">{detail.jobId}</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <AdminRefreshButton label="Refresh Detail" />
            <Link
              href="/admin/jobs"
              className="surface-chip rounded-full px-3 py-1 text-xs text-fg/85 transition hover:bg-surface-2/72"
            >
              Back to Jobs
            </Link>
            <Link
              href={buildLogsHref(detail)}
              className="surface-chip rounded-full px-3 py-1 text-xs text-fg/85 transition hover:bg-surface-2/72"
            >
              Nearby Logs
            </Link>
          </div>
        </div>
        <div className="mt-4">
          <AdminJobDetailActionBar detail={detail} />
        </div>

        <dl className="mt-4 grid gap-3 text-sm md:grid-cols-3">
          <div>
            <dt className="text-fg/60">State</dt>
            <dd>{detail.currentState ?? "-"}</dd>
          </div>
          <div>
            <dt className="text-fg/60">Queue</dt>
            <dd>{detail.queue}</dd>
          </div>
          <div>
            <dt className="text-fg/60">Region</dt>
            <dd>{detail.region ?? "-"}</dd>
          </div>
          <div>
            <dt className="text-fg/60">Type</dt>
            <dd>{detail.jobType ?? "-"}</dd>
          </div>
          <div>
            <dt className="text-fg/60">Method</dt>
            <dd>{detail.jobMethod ?? "-"}</dd>
          </div>
          <div>
            <dt className="text-fg/60">Server</dt>
            <dd>{detail.serverId ?? "-"}</dd>
          </div>
          <div>
            <dt className="text-fg/60">Created</dt>
            <dd>{detail.createdAtUtc ? new Date(detail.createdAtUtc).toLocaleString() : "-"}</dd>
          </div>
          <div>
            <dt className="text-fg/60">State Changed</dt>
            <dd>{detail.stateChangedAtUtc ? new Date(detail.stateChangedAtUtc).toLocaleString() : "-"}</dd>
          </div>
          <div>
            <dt className="text-fg/60">Failed Attempts</dt>
            <dd>{detail.failedCount}</dd>
          </div>
        </dl>
      </div>

      {detail.currentState === "Processing" ? (
        <div className="rounded-panel border border-danger/20 bg-danger/10 p-5 text-sm text-danger/85">
          Deleting a processing job changes Hangfire state to deleted, but already-started side effects may still complete before the worker observes cancellation.
        </div>
      ) : null}

      <div className="page-panel p-5">
        <h3 className="text-base font-semibold">Exception</h3>
        <p className="mt-2 text-sm text-fg/75">{detail.exceptionType ?? "-"}</p>
        <p className="mt-1 text-sm">{detail.exceptionMessage ?? "-"}</p>
        <pre className="surface-subtle mt-3 overflow-x-auto whitespace-pre-wrap rounded-card p-3 text-xs text-fg/85">
          {detail.exceptionDetails ?? "No exception details captured."}
        </pre>
      </div>

      <div className="page-panel p-5">
        <h3 className="text-base font-semibold">State Timeline</h3>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-fg/60">
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

      <div className="page-panel p-5">
        <h3 className="text-base font-semibold">Invocation Arguments</h3>
        <pre className="surface-subtle mt-3 overflow-x-auto whitespace-pre-wrap rounded-card p-3 text-xs text-fg/85">
          {detail.arguments.length > 0 ? detail.arguments.join("\n") : "No arguments."}
        </pre>
      </div>
    </section>
  );
}
