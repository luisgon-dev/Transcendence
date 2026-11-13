import { getJourneyMetrics, isJourneyEvent } from "@/lib/journeyMetrics";
import { getPublicOrigin } from "@/lib/env";

export async function POST(request: Request) {
  const origin = request.headers.get("origin");
  if ((origin && origin !== (getPublicOrigin() ?? new URL(request.url).origin)) || request.headers.get("sec-fetch-site") === "cross-site") return new Response(null, { status: 403 });
  const reader = request.body?.getReader();
  if (!reader) return new Response(null, { status: 400 });
  let size = 0;
  const chunks: Uint8Array[] = [];
  try {
    while (true) {
      const { done, value } = await reader.read(); if (done) break;
      size += value.length;
      if (size > 128) { await reader.cancel(); return new Response(null, { status: 413 }); }
      chunks.push(value);
    }
    const data = JSON.parse(new TextDecoder().decode(Uint8Array.from(chunks.flatMap(chunk => [...chunk]))));
    if (!data || Object.keys(data).length !== 1 || !isJourneyEvent(data.event)) return new Response(null, { status: 400 });
    getJourneyMetrics().record(data.event);
    return new Response(null, { status: 204 });
  } catch { return new Response(null, { status: 400 }); }
}
