export async function safeReadJson(res: Response): Promise<unknown | null> {
  try {
    return await res.json();
  } catch {
    return null;
  }
}

export function pickJsonMessage(body: unknown): string | null {
  if (!body || typeof body !== "object") return null;
  const msg = (body as Record<string, unknown>).message;
  return typeof msg === "string" ? msg : null;
}

