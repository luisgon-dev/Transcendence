import { adminGet } from "@/lib/adminBackend";
import type { AdminAuditEntry } from "@/lib/adminTypes";

export default async function AdminAuditPage() {
  const rows = await adminGet<AdminAuditEntry[]>("/api/admin/audit-log?limit=200");

  return (
    <section className="rounded-2xl border border-border/70 bg-surface/40 p-4">
      <h2 className="text-lg font-semibold">Audit Log</h2>
      <div className="mt-3 overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="text-fg/65">
            <tr>
              <th className="py-2">When</th>
              <th className="py-2">Actor</th>
              <th className="py-2">Action</th>
              <th className="py-2">Target</th>
              <th className="py-2">Status</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id} className="border-t border-border/40">
                <td className="py-2">{new Date(row.createdAtUtc).toLocaleString()}</td>
                <td className="py-2">{row.actorEmail ?? row.actorUserAccountId ?? "unknown"}</td>
                <td className="py-2 font-mono text-xs">{row.action}</td>
                <td className="py-2">
                  {[row.targetType, row.targetId].filter(Boolean).join(":") || "-"}
                </td>
                <td className="py-2">{row.isSuccess ? "success" : "failed"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
