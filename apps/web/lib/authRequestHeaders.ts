import { resolveClientIp } from "@/lib/clientIp";

/** All auth entry points use the same edge-derived client address and correlation header. */
export function authRequestHeaders(incoming: Pick<Headers, "get">, requestId?: string): Record<string, string> {
  const outgoing: Record<string, string> = {};
  const clientIp = resolveClientIp(incoming);
  if (clientIp) outgoing["x-forwarded-for"] = clientIp;
  if (requestId) outgoing["x-trn-request-id"] = requestId;
  return outgoing;
}
