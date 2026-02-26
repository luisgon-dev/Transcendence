import "server-only";

import { requireAdminSession } from "@/lib/adminSession";
import { getBackendBaseUrl } from "@/lib/env";

type RequestMethod = "GET" | "POST" | "PUT" | "DELETE";

async function request<T>(
  method: RequestMethod,
  path: string,
  body?: unknown
): Promise<T> {
  const session = await requireAdminSession();
  const baseUrl = getBackendBaseUrl();
  const url = new URL(path, baseUrl);

  const response = await fetch(url, {
    method,
    headers: {
      authorization: `Bearer ${session.accessToken}`,
      "content-type": "application/json"
    },
    body: body === undefined ? undefined : JSON.stringify(body),
    cache: "no-store"
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`Admin backend request failed (${response.status}): ${text}`);
  }

  if (response.status === 204) {
    return null as T;
  }

  return (await response.json()) as T;
}

export function adminGet<T>(path: string) {
  return request<T>("GET", path);
}

export function adminPost<T>(path: string, body?: unknown) {
  return request<T>("POST", path, body);
}

export function adminPut<T>(path: string, body?: unknown) {
  return request<T>("PUT", path, body);
}

export function adminDelete<T>(path: string) {
  return request<T>("DELETE", path);
}
