import { expect, it } from "vitest";
import { POST } from "./route";
import { JourneyMetrics } from "@/lib/journeyMetrics";

it("accepts only bounded anonymous event names and rejects cross-origin or oversized bodies", async () => {
  const send = (body: string, origin = "https://local.invalid") => POST(new Request("https://local.invalid/api/telemetry/journey", { method: "POST", headers: { origin }, body }));
  expect((await send(JSON.stringify({ event: "lookup_succeeded" }))).status).toBe(204);
  expect((await send(JSON.stringify({ event: "Kronic#NA1" }))).status).toBe(400);
  expect((await send(JSON.stringify({ event: "lookup_succeeded", player: "name" }))).status).toBe(400);
  expect((await send("x".repeat(129))).status).toBe(413);
  expect((await send("{}", "https://untrusted.invalid")).status).toBe(403);
  const metrics = new JourneyMetrics(); metrics.record("lookup_succeeded");
  expect(metrics.renderPrometheus()).toContain('event="lookup_succeeded"} 1');
});
