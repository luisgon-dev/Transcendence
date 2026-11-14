export const JOURNEY_EVENTS = ["new_session", "return_session", "lookup_started", "lookup_succeeded", "lookup_failed", "multi_search_succeeded", "live_probe_succeeded"] as const;
export type JourneyEvent = (typeof JOURNEY_EVENTS)[number];
export function isJourneyEvent(value: unknown): value is JourneyEvent {
  return typeof value === "string" && (JOURNEY_EVENTS as readonly string[]).includes(value);
}
export class JourneyMetrics {
  private readonly counts = new Map<JourneyEvent, number>();
  record(event: JourneyEvent) { this.counts.set(event, Math.min(Number.MAX_SAFE_INTEGER, (this.counts.get(event) ?? 0) + 1)); }
  renderPrometheus() {
    return "# TYPE transcendence_journey_events_total counter\n" + JOURNEY_EVENTS.map(event =>
      `transcendence_journey_events_total{event="${event}"} ${this.counts.get(event) ?? 0}\n`).join("");
  }
}
const shared = globalThis as typeof globalThis & { trnJourneyMetrics?: JourneyMetrics };
export function getJourneyMetrics() { return shared.trnJourneyMetrics ??= new JourneyMetrics(); }

/** Closed labels only. No player identity, path, search text, device ID or session ID is sent. */
export function recordJourney(event: JourneyEvent) {
  if (typeof navigator === "undefined" || navigator.webdriver || process.env.NODE_ENV === "test") return;
  const body = JSON.stringify({ event });
  void fetch("/api/telemetry/journey", { method: "POST", headers: { "content-type": "application/json" },
    body, keepalive: true, credentials: "omit", referrerPolicy: "no-referrer" }).catch(() => undefined);
}
