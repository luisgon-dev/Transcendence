import { adminGet } from "@/lib/adminBackend";
import type { AdminServiceLogsResponse } from "@/lib/adminTypes";

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

function buildLogsHref({
  service,
  level,
  q,
  sinceUtc,
  untilUtc,
  limit
}: {
  service: string;
  level?: string;
  q?: string;
  sinceUtc?: string;
  untilUtc?: string;
  limit?: number;
}) {
  const params = new URLSearchParams({
    service,
    limit: String(limit ?? 200)
  });
  if (level) params.set("level", level);
  if (q) params.set("q", q);
  if (sinceUtc) params.set("sinceUtc", sinceUtc);
  if (untilUtc) params.set("untilUtc", untilUtc);
  return `/admin/logs?${params.toString()}`;
}

function stat(label: string, value: string) {
  return (
    <div className="rounded-2xl border border-border/70 bg-surface/60 p-4">
      <p className="text-xs uppercase tracking-wide text-fg/60">{label}</p>
      <p className="mt-1 text-sm font-semibold text-fg">{value}</p>
    </div>
  );
}

export default async function AdminLogsPage(props: { searchParams: SearchParams }) {
  const searchParams = await props.searchParams;
  const service = (toScalar(searchParams.service) ?? "service").toLowerCase();
  const level = (toScalar(searchParams.level) ?? "").trim().toUpperCase();
  const q = (toScalar(searchParams.q) ?? "").trim();
  const sinceUtc = (toScalar(searchParams.sinceUtc) ?? "").trim();
  const untilUtc = (toScalar(searchParams.untilUtc) ?? "").trim();
  const limit = toInt(toScalar(searchParams.limit), 200, 1, 500);

  const query = new URLSearchParams({
    service,
    limit: String(limit)
  });
  if (level) query.set("level", level);
  if (q) query.set("q", q);
  if (sinceUtc) query.set("sinceUtc", sinceUtc);
  if (untilUtc) query.set("untilUtc", untilUtc);

  const response = await adminGet<AdminServiceLogsResponse>(
    `/api/admin/logs/services?${query.toString()}`
  );
  const rows = response.items;

  const emptyMessage = !response.source.available
    ? "The selected service is not currently writing operational logs."
    : rows.length === 0
      ? "No log rows matched the current filters."
      : null;

  return (
    <section className="grid gap-6">
      <div className="grid gap-3 md:grid-cols-4">
        {stat("Service", response.source.service)}
        {stat("Source", response.source.available ? "Available" : "Missing")}
        {stat("Files Scanned", String(response.source.filesScanned))}
        {stat(
          "Latest Event",
          response.source.latestTimestampUtc
            ? new Date(response.source.latestTimestampUtc).toLocaleString()
            : "None"
        )}
      </div>

      <div className="rounded-[1.75rem] border border-border/70 bg-surface/50 p-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold">Service Logs</h2>
            <p className="mt-1 text-sm text-fg/70">
              Inspect operational logs across the Web API and worker services.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2 text-xs">
            <a
              href={buildLogsHref({ service, limit })}
              className="rounded-full border border-border/70 bg-surface/70 px-3 py-1.5 text-fg/80 transition hover:bg-white/10"
            >
              Reset Filters
            </a>
            <a
              href={buildLogsHref({
                service,
                level,
                q,
                sinceUtc,
                untilUtc,
                limit
              })}
              className="rounded-full border border-primary/40 bg-primary/10 px-3 py-1.5 text-primary transition hover:bg-primary/20"
            >
              Refresh
            </a>
          </div>
        </div>

        <form method="get" className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-6">
          <label className="grid gap-1 text-xs text-fg/70">
            Service
            <select
              name="service"
              defaultValue={service}
              className="h-11 rounded-xl border border-border/70 bg-surface/80 px-3 text-sm text-fg outline-none focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
            >
              <option value="service">Worker Service</option>
              <option value="webapi">Web API</option>
            </select>
          </label>
          <label className="grid gap-1 text-xs text-fg/70">
            Level
            <select
              name="level"
              defaultValue={level}
              className="h-11 rounded-xl border border-border/70 bg-surface/80 px-3 text-sm text-fg outline-none focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
            >
              <option value="">All Levels</option>
              <option value="TRACE">TRACE</option>
              <option value="DEBUG">DEBUG</option>
              <option value="INFORMATION">INFORMATION</option>
              <option value="WARNING">WARNING</option>
              <option value="ERROR">ERROR</option>
              <option value="CRITICAL">CRITICAL</option>
            </select>
          </label>
          <label className="grid gap-1 text-xs text-fg/70">
            Search
            <input
              type="text"
              name="q"
              defaultValue={q}
              placeholder="Message, category, exception"
              className="h-11 rounded-xl border border-border/70 bg-surface/80 px-3 text-sm text-fg outline-none placeholder:text-muted focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
            />
          </label>
          <label className="grid gap-1 text-xs text-fg/70">
            Since UTC
            <input
              type="datetime-local"
              name="sinceUtc"
              defaultValue={sinceUtc}
              className="h-11 rounded-xl border border-border/70 bg-surface/80 px-3 text-sm text-fg outline-none focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
            />
          </label>
          <label className="grid gap-1 text-xs text-fg/70">
            Until UTC
            <input
              type="datetime-local"
              name="untilUtc"
              defaultValue={untilUtc}
              className="h-11 rounded-xl border border-border/70 bg-surface/80 px-3 text-sm text-fg outline-none focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
            />
          </label>
          <label className="grid gap-1 text-xs text-fg/70">
            Limit
            <div className="flex gap-2">
              <input
                type="number"
                name="limit"
                defaultValue={limit}
                min={1}
                max={500}
                className="h-11 w-full rounded-xl border border-border/70 bg-surface/80 px-3 text-sm text-fg outline-none focus:border-primary/70 focus:ring-2 focus:ring-primary/25"
              />
              <button
                type="submit"
                className="h-11 rounded-xl border border-primary/40 bg-primary/10 px-4 text-sm font-medium text-primary transition hover:bg-primary/20"
              >
                Apply
              </button>
            </div>
          </label>
        </form>
      </div>

      <div className="rounded-[1.75rem] border border-border/70 bg-surface/50 p-5">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border/40 pb-3">
          <div>
            <h3 className="text-base font-semibold">Results</h3>
            <p className="text-sm text-fg/70">
              {rows.length} row{rows.length === 1 ? "" : "s"} loaded
              {response.source.truncated ? " (truncated to current limit)" : ""}
            </p>
          </div>
        </div>

        {emptyMessage ? (
          <div className="mt-4 rounded-2xl border border-border/60 bg-black/10 p-4 text-sm text-fg/75">
            {emptyMessage}
          </div>
        ) : (
          <div className="mt-4 overflow-x-auto">
            <table className="w-full min-w-[980px] text-left text-sm">
              <thead className="text-[11px] uppercase tracking-wider text-fg/65">
                <tr className="border-b border-border/30">
                  <th className="py-2 pr-4">When</th>
                  <th className="py-2 pr-4">Service</th>
                  <th className="py-2 pr-4">Level</th>
                  <th className="py-2 pr-4">Category</th>
                  <th className="py-2">Message</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={`${row.timestampUtc}-${row.eventId}-${row.category}`} className="border-b border-border/20 align-top">
                    <td className="py-3 pr-4 whitespace-nowrap text-xs text-fg/80">
                      {new Date(row.timestampUtc).toLocaleString()}
                    </td>
                    <td className="py-3 pr-4 text-xs text-fg/80">{row.service}</td>
                    <td className="py-3 pr-4">
                      <span className="rounded-full border border-border/60 bg-surface/80 px-2 py-1 text-[11px] font-medium text-fg">
                        {row.level}
                      </span>
                    </td>
                    <td className="py-3 pr-4 font-mono text-xs text-fg/75">{row.category}</td>
                    <td className="py-3">
                      <p className="text-sm text-fg">{row.message ?? "-"}</p>
                      {row.exception ? (
                        <pre className="mt-2 overflow-x-auto whitespace-pre-wrap rounded-xl border border-danger/20 bg-danger/5 p-3 text-xs text-fg/85">
                          {row.exception}
                        </pre>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </section>
  );
}
