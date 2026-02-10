import { NextResponse, type NextRequest } from "next/server";

function isVerbose() {
  return (process.env.TRN_ERROR_VERBOSITY ?? "").toLowerCase().trim() === "verbose";
}

function pickHeader(req: NextRequest, key: string) {
  const v = req.headers.get(key);
  return v ?? undefined;
}

export const config = {
  matcher: ["/summoners/:path*"]
};

export function middleware(req: NextRequest) {
  if (!isVerbose()) return NextResponse.next();

  const requestId = req.headers.get("x-trn-request-id") ?? crypto.randomUUID();

  // Forward the request id to the app so server components can read it via next/headers.
  const requestHeaders = new Headers(req.headers);
  requestHeaders.set("x-trn-request-id", requestId);

  const pathname = req.nextUrl.pathname;
  const search = req.nextUrl.search;

  console.log(
    JSON.stringify({
      level: "info",
      msg: "middleware request",
      requestId,
      pathname,
      search,
      headers: {
        host: pickHeader(req, "host"),
        "x-forwarded-host": pickHeader(req, "x-forwarded-host"),
        "x-forwarded-proto": pickHeader(req, "x-forwarded-proto"),
        "x-forwarded-for": pickHeader(req, "x-forwarded-for"),
        "x-real-ip": pickHeader(req, "x-real-ip"),
        "user-agent": pickHeader(req, "user-agent")
      }
    })
  );

  const res = NextResponse.next({ request: { headers: requestHeaders } });
  res.headers.set("x-trn-request-id", requestId);
  return res;
}

