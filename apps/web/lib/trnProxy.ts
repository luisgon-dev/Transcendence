import type { NextRequest } from "next/server";

import { getBackendBaseUrl, getBackendTimeoutMs, getErrorVerbosity } from "@/lib/env";
import { fetchWithTimeout, isAbortError } from "@/lib/fetchWithTimeout";
import { newRequestId } from "@/lib/requestId";
import { logEvent } from "@/lib/serverLog";

function copyHeaders(req: NextRequest) {
  const headers = new Headers(req.headers);

  // Never forward browser cookies to the backend from our BFF endpoints.
  headers.delete("cookie");

  // Let fetch set host.
  headers.delete("host");
  headers.delete("content-length");

  return headers;
}

export async function proxyToBackend(
  req: NextRequest,
  path: string[],
  {
    addHeaders
  }: {
    addHeaders?: Record<string, string>;
  } = {}
) {
  const requestId = newRequestId();
  const started = Date.now();
  const baseUrl = getBackendBaseUrl();
  const url = new URL(`/api/${path.join("/")}`, baseUrl);
  url.search = req.nextUrl.search;

  const headers = copyHeaders(req);
  headers.set("x-trn-request-id", requestId);
  if (addHeaders) {
    for (const [k, v] of Object.entries(addHeaders)) headers.set(k, v);
  }

  const body =
    req.method === "GET" || req.method === "HEAD" ? undefined : await req.text();

  let res: Response;
  try {
    res = await fetchWithTimeout(
      url,
      {
        method: req.method,
        headers,
        body,
        redirect: "manual"
      },
      { timeoutMs: getBackendTimeoutMs() }
    );
  } catch (err: unknown) {
    const durationMs = Date.now() - started;
    const kind = isAbortError(err) ? "timeout" : "unreachable";

    logEvent("error", "proxyToBackend upstream fetch failed", {
      requestId,
      kind,
      method: req.method,
      url: url.toString(),
      durationMs,
      error: err
    });

    const verbosity = getErrorVerbosity();
    const status = isAbortError(err) ? 504 : 503;
    const message =
      status === 504
        ? "Timed out reaching the backend."
        : "We are having trouble reaching the backend.";

    const payload = {
      message,
      code: status === 504 ? "BACKEND_TIMEOUT" : "BACKEND_UNREACHABLE",
      requestId,
      ...(verbosity === "verbose"
        ? { detail: err instanceof Error ? err.message : String(err) }
        : null)
    };

    return new Response(JSON.stringify(payload), {
      status,
      headers: {
        "content-type": "application/json; charset=utf-8",
        "x-trn-request-id": requestId
      }
    });
  }

  // Copy response headers, but never allow Set-Cookie from the backend to leak through.
  const outHeaders = new Headers(res.headers);
  outHeaders.delete("set-cookie");
  outHeaders.set("x-trn-request-id", requestId);

  return new Response(res.body, { status: res.status, headers: outHeaders });
}
