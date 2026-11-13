import { fetchAndConsumeWithTimeout } from "@/lib/fetchWithTimeout";

export const STATIC_JSON_TIMEOUT_MS = 4000;
export const OPTIONAL_STATIC_JSON_TIMEOUT_MS = 2000;

/** Bound headers and JSON consumption, including a CDN that sends headers then stalls. */
export function fetchStaticJson<T>(url: string, message: string, timeoutMs = STATIC_JSON_TIMEOUT_MS): Promise<T> {
  return fetchAndConsumeWithTimeout(url, { next: { revalidate: 60 * 60 * 24 } }, async response => {
    if (!response.ok) throw new Error(message);
    return await response.json() as T;
  }, { timeoutMs });
}
