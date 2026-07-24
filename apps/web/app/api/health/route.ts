// Liveness endpoint for the container HEALTHCHECK (and any external probe). Intentionally does no
// backend I/O — it reports that the Next.js process is accepting and serving requests, so a hung
// process is detected. Backend reachability is a separate concern (see /api/diagnostics/backend).
export function GET() {
  return Response.json({ status: "ok" }, { status: 200 });
}
