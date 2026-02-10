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

export function getBackendTimeoutMs(): number {
  const raw = process.env.TRN_BACKEND_TIMEOUT_MS;
  const n = raw ? Number(raw) : NaN;
  // Default should be low enough to fail fast on bad DNS/network, but not too low for cold starts.
  if (Number.isFinite(n) && n > 0) return Math.floor(n);
  return 10_000;
}
