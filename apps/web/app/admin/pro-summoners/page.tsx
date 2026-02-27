import { adminGet } from "@/lib/adminBackend";
import type { ProSummoner } from "@/lib/adminTypes";

import { ProSummonersPanel } from "./ProSummonersPanel";

export default async function AdminProSummonersPage() {
  const rows = await adminGet<ProSummoner[]>("/api/admin/pro-summoners?isActive=true");

  return <ProSummonersPanel rows={rows} />;
}
