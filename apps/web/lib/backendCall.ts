import "server-only";

import { getBackendTimeoutMs } from "@/lib/env";
import { fetchWithTimeout, isAbortError } from "@/lib/fetchWithTimeout";
import { newRequestId } from "@/lib/requestId";
import { safeReadJson } from "@/lib/safeJson";

export type BackendErrorKind =
  | "timeout"
  | "unreachable"
  | "http_error"
  | "invalid_json";

export type BackendJsonResult<T> = {
  requestId: string;
  url: string;
  durationMs: number;
  status: number;
  ok: boolean;
  body: T | null;
  errorKind?: BackendErrorKind;
};

export async function fetchBackendJson<T>(
  url: string,
  init: (RequestInit & Record<string, unknown>) = {},
  {
    timeoutMs
  }: {
    timeoutMs?: number;
  } = {}
): Promise<BackendJsonResult<T>> {
  const requestId = newRequestId();
  const started = Date.now();
  const effectiveTimeoutMs = timeoutMs ?? getBackendTimeoutMs();

  try {
    const res = await fetchWithTimeout(
      url,
      {
        ...init,
        headers: {
          ...(init.headers ?? {}),
          "x-trn-request-id": requestId
        }
      },
      { timeoutMs: effectiveTimeoutMs }
    );

    const body = (await safeReadJson(res)) as T | null;
    const durationMs = Date.now() - started;

    if (!res.ok) {
      return {
        requestId,
        url,
        durationMs,
        status: res.status,
        ok: false,
        body,
        errorKind: "http_error"
      };
    }

    // If backend returns OK but body isn't JSON, treat it as an error for our callers.
    if (body === null) {
      return {
        requestId,
        url,
        durationMs,
        status: res.status,
        ok: false,
        body: null,
        errorKind: "invalid_json"
      };
    }

    return {
      requestId,
      url,
      durationMs,
      status: res.status,
      ok: true,
      body
    };
  } catch (err: unknown) {
    const durationMs = Date.now() - started;
    return {
      requestId,
      url,
      durationMs,
      status: isAbortError(err) ? 504 : 503,
      ok: false,
      body: null,
      errorKind: isAbortError(err) ? "timeout" : "unreachable"
    };
  }
}
