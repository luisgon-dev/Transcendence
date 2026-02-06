import { NextResponse } from "next/server";

import { clearAuthCookies } from "@/lib/authCookies";

export async function POST() {
  clearAuthCookies();
  return new NextResponse(null, { status: 204 });
}

