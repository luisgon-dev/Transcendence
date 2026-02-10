import { NextResponse } from "next/server";

import { fetchBackendJson } from "@/lib/backendCall";
import { getBackendBaseUrl, getErrorVerbosity } from "@/lib/env";
import { logEvent } from "@/lib/serverLog";

export async function GET() {
  const baseUrl = getBackendBaseUrl();
  const url = `${baseUrl}/api/analytics/tierlist`;

  const result = await fetchBackendJson<unknown>(url, {
    method: "GET",
    cache: "no-store"
  });

  if (!result.ok) {
    // Avoid noisy logs during `next build`, which may evaluate route handlers.
    if (process.env.NEXT_PHASE !== "phase-production-build") {
      logEvent("warn", "backend diagnostics failed", {
        requestId: result.requestId,
        status: result.status,
        errorKind: result.errorKind,
        durationMs: result.durationMs
      });
    }

    const verbosity = getErrorVerbosity();
    return NextResponse.json(
      {
        ok: false,
        backend: {
          status: result.status,
          errorKind: result.errorKind
        },
        requestId: result.requestId,
        durationMs: result.durationMs,
        ...(verbosity === "verbose" ? { note: "Backend request failed." } : null)
      },
      { status: 200, headers: { "x-trn-request-id": result.requestId } }
    );
  }

  return NextResponse.json(
    {
      ok: true,
      backend: { status: result.status },
      requestId: result.requestId,
      durationMs: result.durationMs
    },
    { status: 200, headers: { "x-trn-request-id": result.requestId } }
  );
}
