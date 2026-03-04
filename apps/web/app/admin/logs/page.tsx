import { adminGet } from "@/lib/adminBackend";
import type { AdminServiceLog } from "@/lib/adminTypes";

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

export default async function AdminLogsPage(props: { searchParams: SearchParams }) {
  const searchParams = await props.searchParams;
  const service = (toScalar(searchParams.service) ?? "service").toLowerCase();
  const level = (toScalar(searchParams.level) ?? "").trim().toUpperCase();
  const q = (toScalar(searchParams.q) ?? "").trim();
  const limit = toInt(toScalar(searchParams.limit), 200, 1, 500);

  const query = new URLSearchParams({
    service,
    limit: String(limit)
  });
  if (level) query.set("level", level);
  if (q) query.set("q", q);

  const rows = await adminGet<AdminServiceLog[]>(`/api/admin/logs/services?${query.toString()}`);

  return (
    <section className="rounded-2xl border border-border/70 bg-surface/40 p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold">Service Logs</h2>
        <form method="get" className="flex flex-wrap items-center gap-2 text-sm">
          <select
            name="service"
            defaultValue={service}
            className="rounded-full border border-border/70 bg-transparent px-3 py-1"
          >
            <option value="service">service</option>
            <option value="webapi">webapi</option>
          </select>
          <select
            name="level"
            defaultValue={level}
            className="rounded-full border border-border/70 bg-transparent px-3 py-1"
          >
            <option value="">all levels</option>
            <option value="TRACE">TRACE</option>
            <option value="DEBUG">DEBUG</option>
            <option value="INFORMATION">INFORMATION</option>
            <option value="WARNING">WARNING</option>
            <option value="ERROR">ERROR</option>
            <option value="CRITICAL">CRITICAL</option>
          </select>
          <input
            type="number"
            name="limit"
            defaultValue={limit}
            min={1}
            max={500}
            className="w-24 rounded-full border border-border/70 bg-transparent px-3 py-1"
            aria-label="Limit"
          />
          <input
            type="text"
            name="q"
            defaultValue={q}
            placeholder="Search message/category"
            className="w-56 rounded-full border border-border/70 bg-transparent px-3 py-1"
            aria-label="Search"
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
              <th className="py-2">When</th>
              <th className="py-2">Service</th>
              <th className="py-2">Level</th>
              <th className="py-2">Category</th>
              <th className="py-2">Message</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={`${row.timestampUtc}-${row.eventId}-${row.category}`} className="border-t border-border/40 align-top">
                <td className="py-2 whitespace-nowrap">{new Date(row.timestampUtc).toLocaleString()}</td>
                <td className="py-2">{row.service}</td>
                <td className="py-2">{row.level}</td>
                <td className="py-2 font-mono text-xs">{row.category}</td>
                <td className="py-2">
                  <p>{row.message ?? "-"}</p>
                  {row.exception ? (
                    <pre className="mt-2 overflow-x-auto whitespace-pre-wrap rounded-lg border border-border/60 bg-black/20 p-2 text-xs text-fg/85">
                      {row.exception}
                    </pre>
                  ) : null}
                </td>
              </tr>
            ))}
            {rows.length === 0 ? (
              <tr>
                <td colSpan={5} className="py-3 text-fg/60">
                  No logs were found for the selected filters.
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
    </section>
  );
}
