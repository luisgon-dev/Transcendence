export function getBackendBaseUrl() {
  return process.env.TRN_BACKEND_BASE_URL ?? "http://localhost:8080";
}

export function getBackendApiKey() {
  const key = process.env.TRN_BACKEND_API_KEY;
  if (!key) {
    throw new Error(
      "Missing TRN_BACKEND_API_KEY. Set it in apps/web/.env.local to use AppOnly endpoints."
    );
  }
  return key;
}

export type TrnErrorVerbosity = "safe" | "verbose";

export function getErrorVerbosity(): TrnErrorVerbosity {
  const raw = (process.env.TRN_ERROR_VERBOSITY ?? "").toLowerCase().trim();
  if (raw === "verbose") return "verbose";
  return "safe";
}

/**
 * Server-configured canonical public origin (e.g. `https://transcend.kronic.one`). When set, it is
 * the authoritative value for same-origin (CSRF) checks — unlike a host derived from a client-
 * forwardable `X-Forwarded-Host`. Optional; unset falls back to header-derived comparison.
 */
export function getPublicOrigin(): string | null {
  const raw = (process.env.TRN_PUBLIC_ORIGIN ?? "").trim();
  if (!raw) return null;
  try {
    return new URL(raw).origin;
  } catch {
    return null;
  }
}

export function getBackendTimeoutMs(): number {
  const raw = process.env.TRN_BACKEND_TIMEOUT_MS;
  const n = raw ? Number(raw) : NaN;
  // Default should be low enough to fail fast on bad DNS/network, but not too low for cold starts.
  if (Number.isFinite(n) && n > 0) return Math.floor(n);
  return 10_000;
}
