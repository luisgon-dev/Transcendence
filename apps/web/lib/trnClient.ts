import "server-only";

import { createTrnClient } from "@transcendence/api-client";

import { getBackendBaseUrl } from "@/lib/env";

export function getTrnClient() {
  return createTrnClient(getBackendBaseUrl());
}

