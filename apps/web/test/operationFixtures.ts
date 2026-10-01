export const operationId = "11111111-1111-1111-1111-111111111111";
export const childOperationId = "22222222-2222-2222-2222-222222222222";
export const observedAtUtc = "2026-10-01T12:00:00Z";

export const accepted = {
  operationId,
  statusUrl: `/api/lol/operations/${operationId}`,
  retryAfterSeconds: 0
};

export function operationStatus(overrides: Record<string, unknown> = {}) {
  return {
    operationId,
    kind: "live_game_probe",
    platformRegion: "NA1",
    gameName: "Kronic",
    tagLine: "NA1",
    status: "succeeded",
    queuedAtUtc: observedAtUtc,
    updatedAtUtc: observedAtUtc,
    completedAtUtc: observedAtUtc,
    retryAfterSeconds: 0,
    errorCode: null,
    phases: [],
    result: {
      snapshotId: "33333333-3333-3333-3333-333333333333",
      observedAtUtc,
      liveGame: { state: "offline", participants: [], lastUpdatedUtc: observedAtUtc }
    },
    ...overrides
  };
}
