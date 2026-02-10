import "server-only";

export function toCodePoints(input: unknown): string[] {
  const s =
    typeof input === "string" ? input : input == null ? "" : String(input);
  const out: string[] = [];
  for (const ch of s) {
    const cp = ch.codePointAt(0);
    if (cp == null) continue;
    const hex = cp.toString(16).toUpperCase();
    out.push(`U+${hex.padStart(4, "0")}`);
  }
  return out;
}

export function safeDecodeURIComponent(input: unknown): {
  ok: true;
  value: string;
} | {
  ok: false;
  error: { name?: string; message?: string };
} {
  const s =
    typeof input === "string" ? input : input == null ? "" : String(input);
  try {
    return { ok: true, value: decodeURIComponent(s) };
  } catch (e) {
    if (e instanceof Error) {
      return { ok: false, error: { name: e.name, message: e.message } };
    }
    return { ok: false, error: { message: String(e) } };
  }
}
