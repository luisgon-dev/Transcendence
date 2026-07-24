import { adminGet } from "@/lib/adminBackend";
import type { ProPlayerDiscoveryCandidate, ProSummoner } from "@/lib/adminTypes";

import { ProSummonersPanel } from "./ProSummonersPanel";

export default async function AdminProSummonersPage() {
  const [rows, candidates] = await Promise.all([
    adminGet<ProSummoner[]>("/api/admin/pro-summoners?isActive=true"),
    adminGet<ProPlayerDiscoveryCandidate[]>("/api/admin/pro-summoners/candidates?status=pending")
  ]);

  return <ProSummonersPanel rows={rows} candidates={candidates} />;
}
