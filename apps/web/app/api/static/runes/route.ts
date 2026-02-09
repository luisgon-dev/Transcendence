import { NextResponse } from "next/server";

import { fetchRunesReforged } from "@/lib/staticData";

export async function GET() {
  try {
    const { version, runeById, styleById } = await fetchRunesReforged();

    return NextResponse.json(
      { version, runeById, styleById },
      {
        headers: {
          "cache-control": "public, s-maxage=86400, stale-while-revalidate=86400"
        }
      }
    );
  } catch (err: unknown) {
    return NextResponse.json(
      { message: err instanceof Error ? err.message : "Static data error." },
      { status: 502 }
    );
  }
}

