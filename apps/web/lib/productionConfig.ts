type Environment = Record<string, string | undefined>;

/** Validate deployment assumptions before accepting traffic, without exposing supplied values. */
export function validateProductionConfig(env: Environment) {
  if (env.NODE_ENV !== "production" || env.NEXT_PHASE === "phase-production-build") return;
  let origin: URL;
  try { origin = new URL(env.TRN_PUBLIC_ORIGIN ?? ""); }
  catch { throw new Error("TRN_PUBLIC_ORIGIN must be configured for production."); }
  const loopback = ["localhost", "127.0.0.1", "[::1]"].includes(origin.hostname);
  if ((origin.protocol !== "https:" && !(loopback && origin.protocol === "http:"))
      || origin.username || origin.password || origin.pathname !== "/" || origin.search || origin.hash) {
    throw new Error("TRN_PUBLIC_ORIGIN must be a canonical HTTPS origin (loopback HTTP is allowed locally).");
  }
  let backend: URL;
  try { backend = new URL(env.TRN_BACKEND_BASE_URL ?? ""); }
  catch { throw new Error("TRN_BACKEND_BASE_URL must be configured for production."); }
  if (!["http:", "https:"].includes(backend.protocol) || backend.username || backend.password) {
    throw new Error("TRN_BACKEND_BASE_URL must be an HTTP(S) URL without embedded credentials.");
  }
  if (!env.TRN_BACKEND_API_KEY?.trim()) throw new Error("TRN_BACKEND_API_KEY must be configured for production.");
  if (env.TRN_TRUSTED_PROXY_HEADERS !== "true") {
    throw new Error("TRN_TRUSTED_PROXY_HEADERS=true requires an edge that overwrites client IP headers and blocks direct web access.");
  }
}
