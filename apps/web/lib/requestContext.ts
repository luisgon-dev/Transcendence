import "server-only";

import { headers } from "next/headers";

const SAFE_HEADER_KEYS = [
  "host",
  "x-forwarded-host",
  "x-forwarded-proto",
  "x-forwarded-for",
  "x-real-ip",
  "user-agent",
  // Cloudflare (optional)
  "cf-connecting-ip",
  "cf-ray"
] as const;

export type SafeRequestContext = {
  headers: Partial<Record<(typeof SAFE_HEADER_KEYS)[number], string>>;
};

export async function getSafeRequestContext(): Promise<SafeRequestContext> {
  const h = await headers();
  const out: SafeRequestContext["headers"] = {};

  for (const k of SAFE_HEADER_KEYS) {
    const v = h.get(k);
    if (v) out[k] = v;
  }

  return { headers: out };
}

