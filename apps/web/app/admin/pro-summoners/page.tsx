import { deleteProSummonerAction } from "@/app/admin/actions";
import { adminGet } from "@/lib/adminBackend";
import type { ProSummoner } from "@/lib/adminTypes";

export default async function AdminProSummonersPage() {
  const rows = await adminGet<ProSummoner[]>("/api/admin/pro-summoners?isActive=true");

  return (
    <section className="rounded-2xl border border-border/70 bg-surface/40 p-4">
      <h2 className="text-lg font-semibold">Tracked Pro Summoners</h2>
      <div className="mt-3 overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="text-fg/65">
            <tr>
              <th className="py-2">Identity</th>
              <th className="py-2">Region</th>
              <th className="py-2">Profile</th>
              <th className="py-2">Updated</th>
              <th className="py-2 text-right">Action</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id} className="border-t border-border/40">
                <td className="py-2">
                  {row.gameName && row.tagLine ? `${row.gameName}#${row.tagLine}` : row.puuid}
                </td>
                <td className="py-2">{row.platformRegion}</td>
                <td className="py-2">
                  {[row.proName, row.teamName].filter(Boolean).join(" / ") || "-"}
                </td>
                <td className="py-2">{new Date(row.updatedAtUtc).toLocaleString()}</td>
                <td className="py-2 text-right">
                  <form action={deleteProSummonerAction}>
                    <input type="hidden" name="id" value={row.id} />
                    <button
                      type="submit"
                      className="rounded-full border border-danger/60 px-3 py-1 text-xs text-danger transition hover:bg-danger/10"
                    >
                      Delete
                    </button>
                  </form>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
