import { describe, expect, it } from "vitest";

import {
  buildBackendRequestHeaders,
  isExplicitlyCacheableBackendRequest
} from "@/lib/backendRequestHeaders";

describe("backend request headers", () => {
  it("omits random correlation headers from revalidated reads", () => {
    const init = { next: { revalidate: 60 } };

    expect(isExplicitlyCacheableBackendRequest(init)).toBe(true);
    expect(buildBackendRequestHeaders(init, "request-1").has("x-trn-request-id")).toBe(false);
  });

  it("omits random correlation headers from force-cached reads", () => {
    const init = { cache: "force-cache" as const };

    expect(isExplicitlyCacheableBackendRequest(init)).toBe(true);
    expect(buildBackendRequestHeaders(init, "request-2").has("x-trn-request-id")).toBe(false);
  });

  it("preserves correlation for uncached and mutating requests", () => {
    expect(
      buildBackendRequestHeaders({ cache: "no-store" }, "request-3").get("x-trn-request-id")
    ).toBe("request-3");
    expect(
      buildBackendRequestHeaders({ method: "POST" }, "request-4").get("x-trn-request-id")
    ).toBe("request-4");
  });

  it("preserves caller headers without mutating the input", () => {
    const input = new Headers({ authorization: "Bearer token" });
    const output = buildBackendRequestHeaders(
      { headers: input, next: { revalidate: 300 } },
      "request-5"
    );

    expect(output.get("authorization")).toBe("Bearer token");
    expect(input.has("x-trn-request-id")).toBe(false);
  });
});
