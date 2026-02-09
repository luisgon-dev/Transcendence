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

