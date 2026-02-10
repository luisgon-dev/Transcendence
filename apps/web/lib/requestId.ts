export function newRequestId(): string {
  // Prefer UUIDs for correlation across logs, but fall back in older runtimes.
  const anyCrypto = globalThis.crypto as
    | { randomUUID?: () => string }
    | undefined;
  const uuid = anyCrypto?.randomUUID?.();
  if (uuid) return uuid;

  // Not cryptographically strong; only used for log correlation.
  return `trn_${Date.now().toString(16)}_${Math.random().toString(16).slice(2)}`;
}

