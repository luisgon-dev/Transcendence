export function computeNextPollDelayMs(
  currentDelayMs: number,
  retryAfterSeconds?: number | null
) {
  const min = 1_000;
  const max = 10_000;

  if (retryAfterSeconds != null && Number.isFinite(retryAfterSeconds)) {
    const next = Math.round(Math.max(1, retryAfterSeconds) * 1000);
    return Math.max(min, Math.min(max, next));
  }

  const next = Math.round(Math.max(min, currentDelayMs) * 1.4);
  return Math.max(min, Math.min(max, next));
}
