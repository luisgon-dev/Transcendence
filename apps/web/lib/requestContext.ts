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
  headerReadError?: { name?: string; message?: string } | null;
};

export async function getSafeRequestContext(): Promise<SafeRequestContext> {
  try {
    const h = await headers();
    const out: SafeRequestContext["headers"] = {};

    for (const k of SAFE_HEADER_KEYS) {
      const v = h.get(k);
      if (v) out[k] = v;
    }

    return { headers: out, headerReadError: null };
  } catch (e) {
    if (e instanceof Error) {
      return { headers: {}, headerReadError: { name: e.name, message: e.message } };
    }
    return { headers: {}, headerReadError: { message: String(e) } };
  }
}
