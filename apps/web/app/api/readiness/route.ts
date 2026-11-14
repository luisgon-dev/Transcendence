import { headers } from "next/headers";
import { getAssetReadinessProbe } from "@/lib/assetReadiness";

/** Content readiness is separate from process liveness; CDN faults must not cause restart loops. */
export async function GET() {
  await headers();
  const result = await getAssetReadinessProbe().get();
  return Response.json({ status: result.ready ? "ready" : "unavailable", checks: result.checks,
    checkedAt: new Date(result.checkedAt).toISOString(), release: process.env.TRN_RELEASE_SHA ?? "unknown" },
    { status: result.ready ? 200 : 503, headers: { "cache-control": "no-store" } });
}
