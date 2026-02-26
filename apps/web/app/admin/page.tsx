import { invalidateAnalyticsCacheAction } from "@/app/admin/actions";
import { adminGet } from "@/lib/adminBackend";
import type { AdminOverview } from "@/lib/adminTypes";

function stat(label: string, value: number | string) {
  return (
    <div className="rounded-2xl border border-border/70 bg-surface/40 p-4">
      <p className="text-xs uppercase tracking-wide text-fg/65">{label}</p>
      <p className="mt-1 text-2xl font-semibold text-fg">{value}</p>
    </div>
  );
}

export default async function AdminOverviewPage() {
  const overview = await adminGet<AdminOverview>("/api/admin/overview");

  return (
    <div className="grid gap-6">
      <section className="grid gap-3 md:grid-cols-4">
        {stat("Database", overview.databaseConnected ? "Connected" : "Unavailable")}
        {stat("Enqueued", overview.enqueued)}
        {stat("Processing", overview.processing)}
        {stat("Failed", overview.failed)}
      </section>

      <section className="rounded-2xl border border-border/70 bg-surface/40 p-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-lg font-semibold">Operational Controls</h2>
          <form action={invalidateAnalyticsCacheAction}>
            <button
              type="submit"
              className="rounded-full border border-border/80 px-4 py-2 text-sm text-fg/85 transition hover:bg-white/10"
            >
              Invalidate Analytics Cache
            </button>
          </form>
        </div>
        <p className="mt-2 text-sm text-fg/70">
          Snapshot generated at {new Date(overview.generatedAtUtc).toLocaleString()}.
        </p>
      </section>

      <section className="rounded-2xl border border-border/70 bg-surface/40 p-4">
        <h2 className="text-lg font-semibold">Hangfire Queues</h2>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-fg/65">
              <tr>
                <th className="py-2">Queue</th>
                <th className="py-2">Length</th>
                <th className="py-2">Fetched</th>
              </tr>
            </thead>
            <tbody>
              {overview.queues.map((queue) => (
                <tr key={queue.name} className="border-t border-border/40">
                  <td className="py-2">{queue.name}</td>
                  <td className="py-2">{queue.length}</td>
                  <td className="py-2">{queue.fetched}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}
