// Pure header parsing — no server-only imports, so it stays unit-testable. Used only from
// server-side BFF code (route handlers, trnProxy, sessionToken).

/** Minimal read surface shared by `NextRequest.headers` (Headers) and `await headers()` (ReadonlyHeaders). */
type HeaderReader = { get(name: string): string | null };

// Headers our trusted edge sets to the real client, in trust order.
const TRUSTED_CLIENT_IP_HEADERS = ["cf-connecting-ip", "x-real-ip"] as const;

/**
 * Resolve the real client IP as seen by our trusted edge (nginx/openresty; optionally Cloudflare) so
 * the BFF can forward it to the backend, whose per-IP auth and read rate limiters partition on it.
 *
 * Trust order: `cf-connecting-ip` → `x-real-ip` (nginx `$remote_addr`) → the RIGHTMOST
 * `x-forwarded-for` entry. The rightmost XFF entry is the hop our edge appended
 * (nginx `$proxy_add_x_forwarded_for`); entries to the left of it can be forged by the client, so
 * they are never trusted. Returns `null` when no trusted source is present (e.g. local dev with no
 * proxy), in which case callers should forward nothing and let the backend fall back to the peer.
 */
export function resolveClientIp(headers: HeaderReader): string | null {
  for (const key of TRUSTED_CLIENT_IP_HEADERS) {
    const value = headers.get(key)?.trim();
    if (value) return value;
  }

  const forwardedFor = headers.get("x-forwarded-for");
  if (forwardedFor) {
    const entries = forwardedFor
      .split(",")
      .map((part) => part.trim())
      .filter(Boolean);
    if (entries.length > 0) return entries[entries.length - 1];
  }

  return null;
}
