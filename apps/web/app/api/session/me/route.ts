import { NextResponse } from "next/server";

import { getSessionMe } from "@/lib/session";

export async function GET() {
  const session = await getSessionMe();
  return NextResponse.json(session);
}
