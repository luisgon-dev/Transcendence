import { headers } from "next/headers";
import { NextResponse } from "next/server";

import { fetchChampionMap } from "@/lib/staticData";

export async function GET() {
  // Failed metadata reads must be retryable at runtime, not baked into an image.
  await headers();
  try {
    const { version, champions } = await fetchChampionMap();

    return NextResponse.json(
      { version, champions },
      {
        headers: {
          "cache-control": "public, s-maxage=86400, stale-while-revalidate=86400"
        }
      }
    );
  } catch (err: unknown) {
    return NextResponse.json(
      { message: err instanceof Error ? err.message : "Static data error." },
      { status: 502, headers: { "cache-control": "no-store" } }
    );
  }
}
