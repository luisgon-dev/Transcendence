import { revokeApiKeyAction, rotateApiKeyAction } from "@/app/admin/actions";
import { adminGet } from "@/lib/adminBackend";
import type { ApiKeyListItem } from "@/lib/adminTypes";

export default async function AdminApiKeysPage() {
  const keys = await adminGet<ApiKeyListItem[]>("/api/auth/keys");

  return (
    <section className="rounded-2xl border border-border/70 bg-surface/40 p-4">
      <h2 className="text-lg font-semibold">API Keys</h2>
      <div className="mt-3 overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="text-fg/65">
            <tr>
              <th className="py-2">Name</th>
              <th className="py-2">Prefix</th>
              <th className="py-2">Created</th>
              <th className="py-2">Last Used</th>
              <th className="py-2">State</th>
              <th className="py-2 text-right">Actions</th>
            </tr>
          </thead>
          <tbody>
            {keys.map((key) => (
              <tr key={key.id} className="border-t border-border/40">
                <td className="py-2">{key.name}</td>
                <td className="py-2 font-mono text-xs">{key.prefix}</td>
                <td className="py-2">{new Date(key.createdAt).toLocaleString()}</td>
                <td className="py-2">{key.lastUsedAt ? new Date(key.lastUsedAt).toLocaleString() : "-"}</td>
                <td className="py-2">{key.isRevoked ? "Revoked" : "Active"}</td>
                <td className="py-2">
                  <div className="flex justify-end gap-2">
                    <form action={rotateApiKeyAction}>
                      <input type="hidden" name="id" value={key.id} />
                      <button
                        type="submit"
                        className="rounded-full border border-border/80 px-3 py-1 text-xs text-fg/85 transition hover:bg-white/10"
                      >
                        Rotate
                      </button>
                    </form>
                    <form action={revokeApiKeyAction}>
                      <input type="hidden" name="id" value={key.id} />
                      <button
                        type="submit"
                        className="rounded-full border border-danger/60 px-3 py-1 text-xs text-danger transition hover:bg-danger/10"
                      >
                        Revoke
                      </button>
                    </form>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
