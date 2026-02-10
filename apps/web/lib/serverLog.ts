import "server-only";

type LogLevel = "info" | "warn" | "error";

function safeString(v: unknown): string | undefined {
  return typeof v === "string" ? v : undefined;
}

export function logEvent(
  level: LogLevel,
  message: string,
  fields: Record<string, unknown> = {}
) {
  const payload: Record<string, unknown> = {
    level,
    msg: message,
    ...fields
  };

  // Avoid accidentally serializing Error objects with huge stacks as nested objects.
  if ("error" in payload) {
    const e = payload.error;
    if (e instanceof Error) {
      payload.error = {
        name: safeString(e.name),
        message: safeString(e.message),
        stack: safeString(e.stack)
      };
    }
  }

  const line = JSON.stringify(payload);
  if (level === "error") console.error(line);
  else if (level === "warn") console.warn(line);
  else console.log(line);
}

