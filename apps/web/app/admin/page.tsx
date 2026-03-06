import Link from "next/link";

import { AdminRefreshButton } from "@/app/admin/AdminRefreshButton";
import { invalidateAnalyticsCacheAction } from "@/app/admin/actions";
import { adminGet } from "@/lib/adminBackend";
import type {
  AdminAnalysisMetricsResponse,
  AdminOverview,
  AdminQueueSummaryResponse
} from "@/lib/adminTypes";

function stat(label: string, value: number | string, tone?: "primary" | "danger") {
  const toneClass =
    tone === "danger"
      ? "text-rose-300"
      : tone === "primary"
        ? "text-primary"
        : "text-fg";

  return (
    <div className="rounded-[1.5rem] border border-border/70 bg-surface/50 p-4">
      <p className="text-xs uppercase tracking-[0.24em] text-fg/55">{label}</p>
      <p className={`mt-2 text-2xl font-semibold ${toneClass}`}>{value}</p>
    </div>
  );
}

function healthTone(health: string) {
  switch (health) {
    case "healthy":
      return "bg-emerald-500/15 text-emerald-200";
    case "catching_up":
      return "bg-sky-500/15 text-sky-200";
    case "blocked":
      return "bg-amber-500/15 text-amber-200";
    default:
      return "bg-rose-500/15 text-rose-200";
  }
}

export default async function AdminOverviewPage() {
  const [overview, metrics, queues] = await Promise.all([
    adminGet<AdminOverview>("/api/admin/overview"),
    adminGet<AdminAnalysisMetricsResponse>("/api/admin/metrics/analysis"),
    adminGet<AdminQueueSummaryResponse>("/api/admin/jobs/queues?scanLimit=2000")
  ]);

  return (
    <div className="grid gap-6">
      <section className="grid gap-3 md:grid-cols-3 xl:grid-cols-6">
        {stat("Database", overview.databaseConnected ? "Connected" : "Unavailable")}
        {stat("Workers", overview.effectiveConcurrency, "primary")}
        {stat("Enqueued", overview.enqueued, overview.enqueued > 1000 ? "danger" : "primary")}
        {stat("Processing", overview.processing)}
        {stat("Scheduled", overview.scheduled)}
        {stat("Failed", overview.failed, overview.failed > 0 ? "danger" : undefined)}
      </section>

      <section className="rounded-[1.75rem] border border-border/70 bg-surface/50 p-5">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold">Pipeline Controls</h2>
            <p className="mt-1 text-sm text-fg/65">
              Snapshot generated at {new Date(overview.generatedAtUtc).toLocaleString()}.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <AdminRefreshButton label="Refresh Snapshot" />
            <Link
              href="/admin/jobs?state=enqueued"
              className="rounded-full border border-border/80 px-4 py-2 text-sm text-fg/85 transition hover:bg-white/10"
            >
              Inspect Backlog
            </Link>
            <form action={invalidateAnalyticsCacheAction}>
              <button
                type="submit"
                className="rounded-full border border-primary/40 bg-primary/10 px-4 py-2 text-sm text-primary transition hover:bg-primary/20"
              >
                Invalidate Analytics Cache
              </button>
            </form>
          </div>
        </div>
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.3fr_0.9fr]">
        <div className="rounded-[1.75rem] border border-border/70 bg-surface/50 p-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="text-lg font-semibold">Analysis Health</h2>
              <p className="mt-1 text-sm text-fg/65">
                Active patch {metrics.activePatchVersion ?? "unknown"}
                {metrics.activePatchReleasedAtUtc
                  ? ` · released ${new Date(metrics.activePatchReleasedAtUtc).toLocaleString()}`
                  : ""}
              </p>
            </div>
            <Link
              href="/admin/jobs?state=processing"
              className="rounded-full border border-border/80 px-3 py-1 text-xs text-fg/85 transition hover:bg-white/10"
            >
              View Processing Jobs
            </Link>
          </div>

          <div className="mt-4 grid gap-3 md:grid-cols-4">
            {stat("Summoners", metrics.summary.summoners)}
            {stat("Matches", metrics.summary.matches)}
            {stat("Patch Success", metrics.summary.currentPatchSuccessfulMatches)}
            {stat("Timeline Coverage", `${Math.round(metrics.summary.timelineCoverageRatio * 100)}%`)}
          </div>

          <div className="mt-4 overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-fg/60">
                <tr>
                  <th className="py-2">Region</th>
                  <th className="py-2">Health</th>
                  <th className="py-2">Patch Success</th>
                  <th className="py-2">Timeline</th>
                  <th className="py-2">Queued</th>
                  <th className="py-2">Latest Fetch</th>
                </tr>
              </thead>
              <tbody>
                {metrics.regions.map((region) => (
                  <tr key={region.region} className="border-t border-border/40">
                    <td className="py-2 font-medium">
                      {region.region}
                      {!region.enabled ? <span className="ml-2 text-xs text-fg/45">inactive</span> : null}
                    </td>
                    <td className="py-2">
                      <span className={`rounded-full px-2.5 py-1 text-xs uppercase tracking-wide ${healthTone(region.health)}`}>
                        {region.health.replace("_", " ")}
                      </span>
                    </td>
                    <td className="py-2">
                      {region.currentPatchSuccessfulMatches} / {region.currentPatchTotalMatches}
                    </td>
                    <td className="py-2">
                      {region.timelineSuccessfulMatches} ok, {region.timelinePendingMatches} pending,{" "}
                      {region.timelinePermanentFailures + region.timelineTemporaryFailures} failed
                    </td>
                    <td className="py-2">
                      {region.enqueuedJobs} enq · {region.processingJobs} proc · {region.scheduledJobs} sched
                    </td>
                    <td className="py-2">
                      {region.latestSuccessfulFetchUtc
                        ? new Date(region.latestSuccessfulFetchUtc).toLocaleString()
                        : "-"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        <div className="rounded-[1.75rem] border border-border/70 bg-surface/50 p-5">
          <h2 className="text-lg font-semibold">Top Backlog Groups</h2>
          <p className="mt-1 text-sm text-fg/65">
            Derived from the first {queues.scanLimit.toLocaleString()} jobs scanned.
          </p>
          <div className="mt-4 grid gap-3">
            {queues.topGroups.slice(0, 8).map((group) => (
              <div key={`${group.state}-${group.queue}-${group.jobType}-${group.region}`} className="rounded-2xl border border-border/60 bg-black/15 p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="text-sm font-medium text-fg">
                      {group.jobType?.split(".").pop() ?? "Unknown Job"}
                    </p>
                    <p className="mt-1 text-xs text-fg/55">
                      {group.state} · {group.queue}
                      {group.region ? ` · ${group.region}` : ""}
                    </p>
                  </div>
                  <p className="text-xl font-semibold text-primary">{group.count}</p>
                </div>
                <p className="mt-2 text-xs text-fg/55">
                  {group.oldestSeenAtUtc ? `oldest ${new Date(group.oldestSeenAtUtc).toLocaleString()}` : "no timestamp"}
                </p>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section className="grid gap-4 xl:grid-cols-2">
        <div className="rounded-[1.75rem] border border-border/70 bg-surface/50 p-5">
          <h2 className="text-lg font-semibold">Worker Servers</h2>
          <div className="mt-3 overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-fg/60">
                <tr>
                  <th className="py-2">Server</th>
                  <th className="py-2">Workers</th>
                  <th className="py-2">Heartbeat</th>
                  <th className="py-2">Queues</th>
                </tr>
              </thead>
              <tbody>
                {overview.servers.map((server) => (
                  <tr key={server.name} className="border-t border-border/40">
                    <td className="py-2 font-mono text-xs">{server.name}</td>
                    <td className="py-2">{server.workersCount}</td>
                    <td className="py-2">
                      {server.heartbeatUtc ? new Date(server.heartbeatUtc).toLocaleString() : "-"}
                    </td>
                    <td className="py-2">{server.queues.join(", ")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        <div className="rounded-[1.75rem] border border-border/70 bg-surface/50 p-5">
          <h2 className="text-lg font-semibold">Queue Pressure</h2>
          <div className="mt-3 overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-fg/60">
                <tr>
                  <th className="py-2">Queue</th>
                  <th className="py-2">Length</th>
                  <th className="py-2">Fetched</th>
                </tr>
              </thead>
              <tbody>
                {overview.queues.map((queue) => (
                  <tr key={queue.name} className="border-t border-border/40">
                    <td className="py-2 font-medium">{queue.name}</td>
                    <td className="py-2">{queue.length}</td>
                    <td className="py-2">{queue.fetched ?? "-"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </section>
    </div>
  );
}
