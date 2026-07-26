type NextFetchOptions = {
  revalidate?: number | false;
};

type BackendRequestInit = RequestInit & {
  next?: NextFetchOptions;
};

function isReadMethod(method: string | undefined) {
  const normalized = method?.toUpperCase() ?? "GET";
  return normalized === "GET" || normalized === "HEAD";
}

export function isExplicitlyCacheableBackendRequest(init: BackendRequestInit) {
  if (!isReadMethod(init.method)) return false;
  if (init.cache === "force-cache") return true;
  return init.next?.revalidate !== undefined && init.next.revalidate !== 0;
}

export function buildBackendRequestHeaders(
  init: BackendRequestInit,
  requestId: string
) {
  const headers = new Headers(init.headers);

  // Next includes custom request headers in the fetch-cache key. A random correlation
  // ID would therefore turn every explicitly cached read into a unique cache entry.
  // Cached reads still receive a local ID for frontend diagnostics; the backend creates
  // its own trace ID when this header is intentionally omitted.
  if (!isExplicitlyCacheableBackendRequest(init)) {
    headers.set("x-trn-request-id", requestId);
  }

  return headers;
}
