import "server-only";

export function toCodePoints(input: string): string[] {
  const out: string[] = [];
  for (const ch of input) {
    const cp = ch.codePointAt(0);
    if (cp == null) continue;
    const hex = cp.toString(16).toUpperCase();
    out.push(`U+${hex.padStart(4, "0")}`);
  }
  return out;
}

export function safeDecodeURIComponent(input: string): {
  ok: true;
  value: string;
} | {
  ok: false;
  error: { name?: string; message?: string };
} {
  try {
    return { ok: true, value: decodeURIComponent(input) };
  } catch (e) {
    if (e instanceof Error) {
      return { ok: false, error: { name: e.name, message: e.message } };
    }
    return { ok: false, error: { message: String(e) } };
  }
}

