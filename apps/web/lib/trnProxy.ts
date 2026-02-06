import type { NextRequest } from "next/server";

import { getBackendBaseUrl } from "@/lib/env";

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
  const baseUrl = getBackendBaseUrl();
  const url = new URL(`/api/${path.join("/")}`, baseUrl);
  url.search = req.nextUrl.search;

  const headers = copyHeaders(req);
  if (addHeaders) {
    for (const [k, v] of Object.entries(addHeaders)) headers.set(k, v);
  }

  const body =
    req.method === "GET" || req.method === "HEAD" ? undefined : await req.text();

  const res = await fetch(url, {
    method: req.method,
    headers,
    body,
    redirect: "manual"
  });

  // Copy response headers, but never allow Set-Cookie from the backend to leak through.
  const outHeaders = new Headers(res.headers);
  outHeaders.delete("set-cookie");

  return new Response(res.body, { status: res.status, headers: outHeaders });
}
