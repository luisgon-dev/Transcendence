export const RSO_STATE_COOKIE = "trn_rso_state";
export const RSO_MODE_COOKIE = "trn_rso_mode";
export const RSO_REGION_COOKIE = "trn_rso_region";
export const RSO_RETURN_COOKIE = "trn_rso_return";

export type RiotRsoMode = "login" | "link";

export function normalizeRsoMode(value: string | null): RiotRsoMode {
  return value === "link" ? "link" : "login";
}

export function safeRsoReturnPath(value: string | null, mode: RiotRsoMode): string {
  const fallback = mode === "link" ? "/account/favorites" : "/account/favorites";
  if (!value || !value.startsWith("/") || value.startsWith("//") || value.includes("\\")) {
    return fallback;
  }
  return value;
}

export function appendRsoResult(path: string, key: "riot" | "riotError", value: string): string {
  const url = new URL(path, "https://local.invalid");
  url.searchParams.set(key, value);
  return `${url.pathname}${url.search}${url.hash}`;
}
