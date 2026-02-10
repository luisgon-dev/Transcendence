export type FetchTimeoutOptions = {
  timeoutMs?: number;
};

export async function fetchWithTimeout(
  input: RequestInfo | URL,
  init: (RequestInit & Record<string, unknown>) | undefined,
  { timeoutMs }: FetchTimeoutOptions = {}
) {
  const ms = typeof timeoutMs === "number" && Number.isFinite(timeoutMs)
    ? Math.max(0, timeoutMs)
    : 0;

  if (!ms) return fetch(input, init);

  const ac = new AbortController();
  const t = setTimeout(() => ac.abort(), ms);
  try {
    return await fetch(input, { ...init, signal: ac.signal });
  } finally {
    clearTimeout(t);
  }
}

export function isAbortError(err: unknown): boolean {
  return (
    typeof err === "object" &&
    err !== null &&
    "name" in err &&
    (err as { name?: unknown }).name === "AbortError"
  );
}
