import createClient from "openapi-fetch";

import type { paths } from "./schema";

export type { paths } from "./schema";
export { default as createClient } from "openapi-fetch";

export function createTrnClient(baseUrl: string, fetchImpl?: typeof fetch) {
  return createClient<paths>({ baseUrl, fetch: fetchImpl });
}
