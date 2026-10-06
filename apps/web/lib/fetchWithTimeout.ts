export type FetchTimeoutOptions = {
  timeoutMs?: number;
};

export async function fetchWithTimeout(
  input: RequestInfo | URL,
  init: (RequestInit & Record<string, unknown>) | undefined,
  { timeoutMs }: FetchTimeoutOptions = {}
) {
  return fetchAndConsumeWithTimeout(input, init, options => Promise.resolve(options), { timeoutMs });
}

// Keep the deadline alive until the response has been consumed. Returning headers alone does not
// bound a stalled JSON body. Racing the operation also covers readers that swallow abort errors.
export async function fetchAndConsumeWithTimeout<T>(
  input: RequestInfo | URL,
  init: (RequestInit & Record<string, unknown>) | undefined,
  consume: (response: Response) => Promise<T>,
  { timeoutMs }: FetchTimeoutOptions = {}
): Promise<T> {
  const ms = typeof timeoutMs === "number" && Number.isFinite(timeoutMs)
    ? Math.max(0, timeoutMs)
    : 0;

  if (!ms) return consume(await fetch(input, init));

  const ac = new AbortController();
  const signal = init?.signal ? AbortSignal.any([init.signal, ac.signal]) : ac.signal;
  let t: ReturnType<typeof setTimeout> | undefined;
  const deadline = new Promise<never>((_, reject) => {
    t = setTimeout(() => {
      const error = new DOMException("Backend response timed out", "AbortError");
      ac.abort(error);
      reject(error);
    }, ms);
  });
  try {
    return await Promise.race([
      fetch(input, { ...init, signal }).then(consume),
      deadline
    ]);
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
