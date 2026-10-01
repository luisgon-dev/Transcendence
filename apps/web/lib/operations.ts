import type { components } from "@transcendence/api-client";
import { LOL_REGION_OPTIONS, platformRegionToSlug } from "@/lib/lolRegions";

export type OperationAccepted = components["schemas"]["OperationAcceptedResponse"];
export type OperationStatus = components["schemas"]["OperationStatusResponse"];

const OPERATION_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const STATES = new Set(["queued", "running", "retrying", "succeeded", "partial", "failed"]);
const TERMINAL = new Set(["succeeded", "partial", "failed"]);
export const OPERATION_TIMEOUT_MS = 120_000;

export function operationRequestSignal(signal: AbortSignal): AbortSignal {
  // Enqueue/stored-result requests also need a bound, before operation polling starts.
  return AbortSignal.any([signal, AbortSignal.timeout(30_000)]);
}

function regionIdentity(value: string): string {
  const normalized = value.trim().toUpperCase();
  const canonical = ["NA1", "EUW1", "EUN1", "KR", "BR1", "LA1", "LA2", "OC1", "JP1", "TR1", "RU"];
  if (canonical.includes(normalized) || LOL_REGION_OPTIONS.some((option) => option.value === value.trim().toLowerCase())) {
    return platformRegionToSlug(value);
  }
  return normalized;
}

export function operationPath(scope: "app" | "user", operationId: string): string {
  if (!OPERATION_ID.test(operationId)) throw new Error("The operation identifier is invalid.");
  // Never navigate to a returned backend URL or allow it to select a different BFF scope.
  return `/api/trn/${scope}/lol/operations/${operationId}`;
}

export function acceptedOperation(value: unknown): OperationAccepted {
  if (!value || typeof value !== "object") throw new Error("The operation response is invalid.");
  const accepted = value as OperationAccepted;
  if (
    typeof accepted.operationId !== "string" || !OPERATION_ID.test(accepted.operationId) ||
    typeof accepted.statusUrl !== "string" || !accepted.statusUrl ||
    !Number.isInteger(accepted.retryAfterSeconds) || accepted.retryAfterSeconds < 0
  ) throw new Error("The operation response is invalid.");
  return accepted;
}

function delay(ms: number, signal: AbortSignal): Promise<void> {
  signal.throwIfAborted();
  return new Promise((resolve, reject) => {
    const finish = () => {
      signal.removeEventListener("abort", abort);
      resolve();
    };
    const timer = setTimeout(finish, ms);
    const abort = () => {
      clearTimeout(timer);
      signal.removeEventListener("abort", abort);
      reject(signal.reason);
    };
    signal.addEventListener("abort", abort, { once: true });
  });
}

export async function pollOperation(
  accepted: OperationAccepted,
  scope: "app" | "user",
  signal: AbortSignal,
  onProgress?: (status: OperationStatus) => void,
  expectedKind?: string,
  target?: { region: string; gameName: string; tagLine: string }
): Promise<OperationStatus> {
  const path = operationPath(scope, accepted.operationId);
  const controller = new AbortController();
  const abort = () => controller.abort(signal.reason);
  signal.throwIfAborted();
  signal.addEventListener("abort", abort, { once: true });
  const deadline = setTimeout(() => controller.abort(new Error(
    "This operation is taking longer than expected. It may still be processing; completion has not been verified."
  )), OPERATION_TIMEOUT_MS);
  let retry = accepted.retryAfterSeconds;
  try {
    for (let attempt = 0; attempt < 32; attempt += 1) {
      if (retry > 0) await delay(Math.min(10_000, Math.max(1_000, retry * 1_000)), controller.signal);
      const response = await fetch(path, { cache: "no-store", signal: controller.signal });
      const json = await response.json().catch(() => null);
      if (!response.ok) throw new Error(json?.message ?? `Operation status request failed (${response.status}).`);
      const status = json as OperationStatus | null;
      if (
        !status || status.operationId !== accepted.operationId || !STATES.has(status.status) ||
        (expectedKind && status.kind !== expectedKind) ||
        (target && (
          typeof status.platformRegion !== "string" || typeof status.gameName !== "string" || typeof status.tagLine !== "string" ||
          regionIdentity(status.platformRegion) !== regionIdentity(target.region) ||
          status.gameName.trim().toLowerCase() !== target.gameName.trim().toLowerCase() ||
          status.tagLine.trim().toLowerCase() !== target.tagLine.trim().toLowerCase()
        )) ||
        !Array.isArray(status.phases) || !status.result ||
        !Number.isInteger(status.retryAfterSeconds) || status.retryAfterSeconds < 0 ||
        (TERMINAL.has(status.status) && !Number.isFinite(Date.parse(status.completedAtUtc ?? "")))
      ) throw new Error("The operation status response is invalid.");
      controller.signal.throwIfAborted();
      onProgress?.(status);
      if (TERMINAL.has(status.status)) return status;
      retry = Math.max(1, status.retryAfterSeconds);
    }
    throw new Error("Completion has not been verified. The operation may still be processing; check again shortly.");
  } catch (error) {
    if (controller.signal.aborted) throw controller.signal.reason;
    throw error;
  } finally {
    clearTimeout(deadline);
    signal.removeEventListener("abort", abort);
  }
}

export function operationFailure(status: OperationStatus): string {
  return `The ${status.kind.replaceAll("_", " ")} failed${status.errorCode ? ` (${status.errorCode})` : ""}. Stored data has been retained.`;
}
