import { getPublicOrigin } from "@/lib/env";

type OriginRequest = {
  method: string;
  headers: Pick<Headers, "get">;
  nextUrl: { protocol: string };
};

export function isSafeMethod(method: string) {
  return method === "GET" || method === "HEAD" || method === "OPTIONS";
}

export function isSameOriginRequest(req: OriginRequest) {
  const origin = req.headers.get("origin");
  if (!origin) return true;

  let originUrl: URL;
  try {
    originUrl = new URL(origin);
  } catch {
    return false;
  }

  // Prefer the server-configured canonical origin: it cannot be influenced by a client-supplied
  // X-Forwarded-Host, so the check cannot be spoofed. Production requires this setting.
  const configured = getPublicOrigin();
  if (configured) return originUrl.origin === configured;

  // Development fallback only. SameSite=Lax cookies remain the primary CSRF control.
  const forwardedHost = req.headers.get("x-forwarded-host");
  const forwardedProto = req.headers.get("x-forwarded-proto");
  const host = forwardedHost ?? req.headers.get("host");
  const proto = forwardedProto ?? req.nextUrl.protocol.replace(":", "");
  if (!host) return false;
  return originUrl.host === host && originUrl.protocol === `${proto}:`;
}
